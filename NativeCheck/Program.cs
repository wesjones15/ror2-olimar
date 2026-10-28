using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using OlimarTest;
using Ror2Common;

// A flat floor at y = 0, a wall along x = 400 and one creature 100 units ahead, answered from
// C# the way the plugin's UnityHost and PikminWorld answer them in the game.
static class Program
{
    static int failures;
    static int groundCalls, wallCalls;
    static readonly List<(ulong id, float amount, uint source)> hits = new List<(ulong, float, uint)>();

    static readonly Native.GroundFn groundFn = (IntPtr u, ref Native.Vec3 p, out float y) => { groundCalls++; y = 0f; return 1; };
    static readonly Native.WallsFn wallsFn = (IntPtr u, ref Native.Vec3 c, float r, out Native.Vec3 o) =>
    {
        wallCalls++;
        o = c;
        o.x = Math.Min(c.x, 400f - r);
    };
    static readonly Native.DamageFn damageFn = (u, creature, amount, part, source, id) => { hits.Add((creature, amount, source)); return 1; };

    static void CheckJumpButton()
    {
        var b = new JumpWhistleButton();
        b.Read(true, false);
        b.Read(false, false);
        Check(b.Jump && !b.Whistle, "brief B press survives until sim tick");
        b.Ticked();
        Check(!b.Jump, "jump press consumed after tick");
        b.Read(true, true);
        Check(b.Blocked && b.Whistle && !b.Jump, "LB + B whistles and blocks jumping");
        b.Read(true, false);
        Check(!b.Jump && !b.Whistle, "releasing LB with B held does not jump");
        b.Read(false, false);
        b.Read(true, false);
        Check(b.Jump, "fresh B press jumps after modifier release");
        b.Read(true, true);
        b.Read(false, false);
        Check(!b.Jump, "LB cancels a jump pending between ticks");
        b.Swallow();
        b.Read(true, false);
        Check(!b.Jump, "closing Onion menu cannot trigger jump");
        b.Read(true, true);
        Check(!b.Jump && !b.Whistle, "closing Onion menu cannot trigger whistle chord");
        b.Read(false, false);
        b.Read(true, false);
        Check(b.Jump, "menu suppression clears after release");
    }

    static void Check(bool ok, string what)
    {
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok) failures++;
    }

    static void CheckRenderClock(IntPtr view, IntPtr sim, ref Native.Host host, int captain)
    {
        var frame = new Native.ViewFrame
        {
            forward = new Native.Vec3(0, 0, 1), up = new Native.Vec3(0, 1, 0), listener = -1,
        };
        ViewDraw[] Draw(float lag)
        {
            frame.lag = lag;
            Native.pik_view_update(view, sim, ref frame);
            var draws = new ViewDraw[Native.pik_view_draws(view, null, 0)];
            Native.pik_view_draws(view, draws, (uint)draws.Length);
            return draws;
        }
        Native.pik_sim_captain(sim, (uint)captain, out var state);
        var pose = new Native.Input
        {
            use_pose = 1, pose_pos = new Native.Vec3(state.pos.x + 40, state.pos.y, state.pos.z),
            pose_face = state.face + 0.5f,
        };
        Draw(0); // Seed the view's previous snapshot.
        Native.pik_sim_set_input(sim, (uint)captain, ref pose);
        Native.pik_sim_advance(sim, ref host, 1f / 30f);
        var first = Draw(1);
        var last = Draw(0);
        var startNavi = Array.Find(first, d => d.kind == Native.KindCaptain && d.id == captain);
        var endNavi = Array.Find(last, d => d.kind == Native.KindCaptain && d.id == captain);
        Check(Math.Abs(endNavi.pos.x - startNavi.pos.x) > 1, "render clock has a moving captain");
        foreach (int hz in new[] { 60, 120, 165 })
        {
            bool ok = true;
            for (int i = 0; i < hz / 30f; i++)
            {
                float alpha = i * 30f / hz;
                foreach (var d in Draw(1 - alpha))
                {
                    var a = Array.Find(first, p => p.key == d.key);
                    var b = Array.Find(last, p => p.key == d.key);
                    ok &= Math.Abs(d.pos.x - (a.pos.x + (b.pos.x - a.pos.x) * alpha)) < 0.001f
                       && Math.Abs(d.pos.y - (a.pos.y + (b.pos.y - a.pos.y) * alpha)) < 0.001f
                       && Math.Abs(d.pos.z - (a.pos.z + (b.pos.z - a.pos.z) * alpha)) < 0.001f;
                }
            }
            Check(ok, $"all model positions follow the render clock at {hz} Hz without a sim tick");
        }
        var mid = Array.Find(Draw(0.5f), d => d.key == startNavi.key);
        float sign = startNavi.rot_y * endNavi.rot_y + startNavi.rot_w * endNavi.rot_w < 0 ? -1 : 1;
        float y = startNavi.rot_y + sign * endNavi.rot_y, w = startNavi.rot_w + sign * endNavi.rot_w;
        float norm = (float)Math.Sqrt(y * y + w * w);
        Check(Math.Abs(mid.rot_y * y / norm + mid.rot_w * w / norm) > 0.99999f,
            "captain facing follows the same half-tick blend");
        var paused = Array.Find(Draw(0.5f), d => d.key == mid.key);
        Check(paused.pos.x == mid.pos.x && paused.rot_y == mid.rot_y, "paused render fraction stays still");
        var invalid = Array.Find(Draw(float.NaN), d => d.key == endNavi.key);
        Check(Math.Abs(invalid.pos.x - endNavi.pos.x) < 0.001f, "invalid lag safely draws the newest state");
    }

    // The server's world through pik_sim_snapshot, the host's envelope, and a replica applying it.
    static void CheckReplica(ref Native.Host host)
    {
        IntPtr server = Native.pik_sim_new(null, 41);
        var origin = new Native.Vec3(0, 0, 0);
        int cap = Native.pik_sim_add_captain(server, ref origin);
        for (int i = 0; i < 20; i++)
        {
            var at = new Native.Vec3(-20 - i * 3, 0, i % 4 * 6);
            Native.pik_sim_spawn_piki(server, (uint)(i % 3), ref at, cap);
        }
        var onionAt = new Native.Vec3(-300, 0, 0);
        Native.pik_sim_add_onion(server, ref onionAt, 7);
        var walk = new Native.Input { stick_x = 1f };
        Native.pik_sim_set_input(server, (uint)cap, ref walk);
        for (int i = 0; i < 30; i++) Native.pik_sim_advance(server, ref host, 1f / 30f);
        var buffer = new byte[128 * 1024];
        uint n = Native.pik_sim_snapshot(server, buffer, (uint)buffer.Length);
        Check(n > 100 && n < 20000, $"a snapshot of 20 Pikmin is {n} bytes");
        Check(Native.pik_sim_snapshot(server, new byte[8], 8) == 0, "a buffer that is too small writes nothing");

        var sent = new List<CaptainInfo> { new CaptainInfo { id = 0, netId = 77,
            flags = CaptainInfo.Arriving | CaptainInfo.Following | CaptainInfo.MenuBeam | CaptainInfo.MenuReady,
            beam = 0.5f, onion = 3 } };
        var envelope = PikminEnvelope.Write(3, sent, buffer, (int)n);
        var infos = new List<CaptainInfo>();
        var status = PikminEnvelope.Read(new ByteReader(envelope), out uint epoch, infos, out var snapshot, out _);
        Check(status == PikminEnvelope.Status.Ok && epoch == 3 && infos.Count == 1 && infos[0].netId == 77
            && infos[0].IsArriving && !infos[0].IsTemporary && infos[0].beam == 0.5f && infos[0].onion == 3
            && infos[0].OnionFollowing && !infos[0].OnionAirborne && infos[0].OnionMenuBeam
            && infos[0].OnionMenuReady && snapshot.Length == (int)n, "envelope round trip");
        sent[0] = new CaptainInfo { id = 0, netId = 77, flags = CaptainInfo.Healing, beam = 0.25f, healingCount = 7, onion = 3 };
        infos.Clear();
        var healingEnvelope = PikminEnvelope.Write(3, sent, buffer, (int)n);
        Check(PikminEnvelope.Read(new ByteReader(healingEnvelope), out _, infos, out _, out _) == PikminEnvelope.Status.Ok
            && infos[0].IsHealing && !infos[0].IsArriving && infos[0].healingCount == 7 && infos[0].beam == 0.25f,
            "healing progress and exact ghost count replicate independently of arrival");
        var cut = new byte[20];
        Array.Copy(envelope, cut, 20);
        Check(PikminEnvelope.Read(new ByteReader(cut), out _, new List<CaptainInfo>(), out _, out _) == PikminEnvelope.Status.Corrupt, "a cut envelope is corrupt");
        var wrong = (byte[])envelope.Clone();
        wrong[1] ^= 1;
        Check(PikminEnvelope.Read(new ByteReader(wrong), out _, new List<CaptainInfo>(), out _, out _) == PikminEnvelope.Status.WrongAbi, "another ABI is refused");

        // A client's input and Onion commands.
        var packet = new PikminInputPacket { netId = 9, px = 1, py = 2, pz = 3, vx = 4, vy = 5, vz = 6, face = 0.5f, cx = 7, cy = 8, cz = 9,
            buttons = PikminInputPacket.Throw | PikminInputPacket.Cycle, swarmX = -1, swarmY = 0.25f, lockNetId = 42 };
        var packetBytes = PikminInputPacket.Write(packet);
        Check(PikminInputPacket.Read(new ByteReader(packetBytes), out var back) && back.netId == 9 && back.pz == 3 && back.vy == 5
            && back.cz == 9 && back.buttons == 10 && back.swarmY == 0.25f && back.lockNetId == 42 && back.face == 0.5f, "input packet round trip");
        Check(!PikminInputPacket.Read(new ByteReader(packetBytes, 0, packetBytes.Length - 1), out _), "a short input packet is refused");
        var command = OnionCommandPacket.Write(new OnionCommandPacket { netId = 9, kind = OnionCommandPacket.CallOut, colour = 2, count = 5 });
        Check(OnionCommandPacket.Read(new ByteReader(command), out var cmd) && cmd.netId == 9 && cmd.kind == 2 && cmd.colour == 2 && cmd.count == 5, "Onion command round trip");
        command[5] = 77;
        Check(!OnionCommandPacket.Read(new ByteReader(command), out _), "an unknown Onion command is refused");
        foreach (byte kind in new[] { OnionCommandPacket.CallOutAny, OnionCommandPacket.PutAwayAny,
            OnionCommandPacket.ReleaseAll, OnionCommandPacket.ReturnAll, OnionCommandPacket.Heal })
        {
            var bulk = OnionCommandPacket.Write(new OnionCommandPacket { netId = 9, kind = kind, count = 0 });
            Check(OnionCommandPacket.Read(new ByteReader(bulk), out var decoded) && decoded.kind == kind,
                "mixed-colour Onion command round trip: " + kind);
            Check(!OnionCommandPacket.Read(new ByteReader(bulk, 0, bulk.Length - 1), out _), "short bulk command refused");
        }

        IntPtr replica = Native.pik_replica_new(null);
        Check(replica != IntPtr.Zero, "replica starts");
        Check(Native.pik_replica_apply(replica, snapshot, (uint)snapshot.Length) == 1, "replica takes the snapshot");
        Check(Native.pik_replica_apply(replica, new byte[] { 1, 2, 3 }, 3) == 0, "replica refuses nonsense");
        Check(Native.pik_replica_apply(server, snapshot, (uint)snapshot.Length) == 0, "a plain sim isn't a replica");
        Native.pik_replica_update(replica, 0.05f);
        var a = new Native.Piki[32];
        var b = new Native.Piki[32];
        uint na = Native.pik_sim_pikis(server, a, 32), nb = Native.pik_sim_pikis(replica, b, 32);
        Check(na == 20 && nb == 20, $"the replica has the server's {na} Pikmin ({nb})");
        bool same = true;
        for (int i = 0; i < na; i++) same &= a[i].color == b[i].color && Math.Abs(a[i].pos.x - b[i].pos.x) < 1f && a[i].state == b[i].state;
        Check(same, "the replica's Pikmin are where the server's are");
        Native.pik_sim_captain(server, (uint)cap, out var capA);
        Native.pik_sim_captain(replica, (uint)cap, out var capB);
        Check(Math.Abs(capA.pos.x - capB.pos.x) < 1f && capA.pos.x > 30f, $"and so is the captain ({capA.pos.x} vs {capB.pos.x})");
        Native.pik_sim_free(server);
        Native.pik_sim_free(replica);
    }

    static void CheckOnionRevival(ref Native.Host host)
    {
        var sim = Native.pik_sim_new(null, 27);
        var ground = new Native.Vec3(0, 0, 0);
        int captain = Native.pik_sim_add_captain(sim, ref ground);
        int onion = Native.pik_sim_add_onion(sim, ref ground, 7);
        Native.pik_sim_store_in_onion(sim, (uint)onion, Native.Red, 0, 4);
        Native.pik_sim_make_onion_dormant(sim, (uint)onion, 0.2f, ref ground);
        var onions = new Native.Onion[1];
        Native.pik_sim_onions(sim, onions, 1);
        Check(onions[0].anim == 7 && onions[0].stored1 == 4, "dormancy crosses ABI without erasing stock");
        Check(Native.pik_sim_onion_call_out(sim, (uint)captain, (uint)onion, Native.Red, 4) == 0, "dormant stock is inaccessible");
        for (int i = 0; i < 150; i++) Native.pik_sim_advance(sim, ref host, 1f / 30f);
        Native.pik_sim_onions(sim, onions, 1);
        Check(onions[0].anim == 0 && onions[0].stored1 == 4, "Onion revives through ABI with stock intact");
        int pellet = Native.pik_sim_spawn_numbered_pellet(sim, 1, Native.Red, ref ground, 0);
        var hover = new Native.Vec3(0, 125, 0);
        Native.pik_sim_set_onion_pose(sim, (uint)onion, ref hover, 1);
        Check(Native.pik_sim_collect_ground_pellet(sim, (uint)onion, (uint)pellet) == 0, "high Onion cannot vacuum a ground pellet");
        hover.y = 40;
        Native.pik_sim_set_onion_pose(sim, (uint)onion, ref hover, 1);
        Check(Native.pik_sim_collect_ground_pellet(sim, (uint)onion, (uint)pellet) == 1, "descended Onion picks up with no Pikmin alive");
        for (int i = 0; i < 150; i++) Native.pik_sim_advance(sim, ref host, 1f / 30f);
        Native.pik_sim_onions(sim, onions, 1);
        Check(onions[0].stored1 == 6 && Native.pik_sim_poisoned(sim) == 0, "ground pickup adds stock and keeps native sim healthy");
        Native.pik_sim_free(sim);
    }

    static void CheckShipRepair(string assets, ref Native.Host host)
    {
        IntPtr sim = Native.pik_sim_new(Native.CString(assets), 43);
        Check(sim != IntPtr.Zero, "ship repair sim created");
        if (sim == IntPtr.Zero) return;
        try
        {
            for (uint part = 0; part < 30; part++)
            {
                uint weight = Native.pik_sim_ship_part_weight(sim, part);
                Check(weight > 0 && weight <= 100, $"ship part {part} disc weight {weight}");
            }
            var name = new byte[128];
            Check(Native.pik_sim_ship_part_name(sim, 4, name, 128) > 0, "part name crosses UTF-8 ABI");
            var origin = new Native.Vec3(0, 0, 0);
            var goal = new Native.Vec3(-300, 0, 0);
            int captain = Native.pik_sim_add_captain(sim, ref origin);
            int onion = Native.pik_sim_add_onion(sim, ref origin, 7);
            int ship = Native.pik_sim_add_ship(sim, ref goal, 0);
            Check(Native.pik_sim_onion_goal(sim, (uint)ship, out var shipGoal) == 1 && shipGoal.z != goal.z &&
                Native.pik_sim_onion_goal(sim, uint.MaxValue, out _) == 0, "Dolphin carry goal and invalid receiver cross ABI");
            Native.pik_sim_set_ship_part_host_delivery(sim, 1);
            Native.pik_sim_set_auto_carry(sim, 1000f);
            int pellet = Native.pik_sim_spawn_ship_part(sim, 4, ref origin, 0);
            Check(pellet >= 0 && Native.pik_sim_spawn_ship_part(sim, 30, ref origin, 0) == -1, "disc part spawn and ID validation");
            var pickups = new Native.Pellet[4];
            Native.pik_sim_pellets(sim, pickups, (uint)pickups.Length);
            var partFrame = Array.Find(pickups, p => p.id == pellet);
            Check(partFrame.has_ship_part == 1, "native ship-part classification crosses the ABI");
            for (byte state = 0; state <= 3; state++)
                foreach (int receiver in new[] { -1, onion, ship })
                {
                    partFrame.state = state; partFrame.onion = receiver;
                    Check(!OnionPickupPolicy.CanTrack(partFrame, onion), $"Onion tracking rejects ship part in state {state}, receiver {receiver}");
                }
            var regular = new Native.Pellet { state = 0, onion = -1 };
            Check(OnionPickupPolicy.CanTrack(regular, onion), "ordinary loose pellet pickup stays available");
            regular.state = 1; regular.onion = ship;
            Check(!OnionPickupPolicy.CanTrack(regular, onion), "ordinary hauls to another receiver are not intercepted");
            uint weightNeeded = Native.pik_sim_ship_part_weight(sim, 4);
            for (int i = 0; i < weightNeeded + 5; i++)
            {
                var at = new Native.Vec3(-25 + i, 0, -25);
                Native.pik_sim_spawn_piki(sim, Native.Red, ref at, captain);
            }
            var events = new Native.Event[256];
            int receipts = 0;
            for (int tick = 0; tick < 1800; tick++)
            {
                Native.pik_sim_advance(sim, ref host, 1f / 30f);
                uint count;
                do
                {
                    count = Native.pik_sim_events(sim, events, (uint)events.Length);
                    for (int i = 0; i < count; i++) if (events[i].kind == Native.EventKind.ShipPartCollected)
                    {
                        receipts++;
                        Check(events[i].a == pellet && events[i].b == ship && events[i].c == 4 && events[i].value == 0,
                            "host receives part identity and Dolphin at completed suction");
                    }
                } while (count == events.Length);
            }
            Check(receipts == 1 && Native.pik_sim_poisoned(sim) == 0, "P/Invoke haul completes exactly once without poisoning");
            Native.pik_sim_give_ship_part(sim, (uint)ship, 4);
            Native.pik_sim_set_ship_part_competitive_delivery(sim, 1);
            Native.pik_sim_set_ship_part_owner(sim, (uint)ship, (uint)captain);
            Native.pik_sim_set_ship_part_owner(sim, (uint)onion, (uint)captain);
            int duplicate = Native.pik_sim_spawn_ship_part(sim, 4, ref origin, 0);
            int salvage = 0;
            for (int tick = 0; tick < 1800; tick++)
            {
                Native.pik_sim_advance(sim, ref host, 1f / 30f);
                uint count;
                do
                {
                    count = Native.pik_sim_events(sim, events, (uint)events.Length);
                    for (int i = 0; i < count; i++) if (events[i].kind == Native.EventKind.ShipPartCollected && events[i].a == duplicate)
                    {
                        salvage++;
                        Check(events[i].b == onion && events[i].c == 4 && events[i].value == 0, "competitive duplicate travels to its owner's Onion through P/Invoke");
                        Native.pik_sim_give_onion_seeds(sim, (uint)onion, Native.Red, weightNeeded * 2);
                    }
                } while (count == events.Length);
            }
            Check(salvage == 1 && Native.pik_sim_poisoned(sim) == 0, "duplicate salvage intake completes exactly once without poisoning");
            uint samples = Native.pik_victory_pcm(Native.CString(assets), null, 0, out uint rate);
            Check(samples > rate * 2 && rate == 44100, "complete-ending PCM crosses ABI");
            IntPtr music = Native.pik_music_new(Native.CString(assets));
            Check(music != IntPtr.Zero, "independent stage-music stream opens from the disc cache");
            if (music != IntPtr.Zero)
            {
                try
                {
                    foreach (uint song in new uint[] { 4, 5, 10, 9, 17 })
                        Check(Native.pik_music_update(music, song, 1, 0f, 1) == 1, "stage song, enemy layers and pause cross music ABI: " + song);
                    Check(Native.pik_music_update(music, 99, 0, 0f, 0) == 0 && Native.pik_music_update(music, 4, 0, 0f, 0) == 1,
                        "invalid stage song is rejected without poisoning the music stream");
                    Check(Native.pik_music_update(music, 0, 0, 0f, 0) == 1, "stage music stops for vanilla bosses/results");
                }
                finally { Native.pik_music_free(music); }
            }
        }
        finally { Native.pik_sim_free(sim); }
    }

    static void CheckUnifiedOnionMenu(ref Native.Host host)
    {
        IntPtr sim = Native.pik_sim_new(null, 19);
        try
        {
            var origin = new Native.Vec3(0, 0, 0);
            int captain = Native.pik_sim_add_captain(sim, ref origin);
            int onion = Native.pik_sim_add_onion(sim, ref origin, 7);
            var stock = new uint[] { 2, 3, 5 };
            for (uint colour = 0; colour < 3; colour++)
                Native.pik_sim_store_in_onion(sim, (uint)onion, colour, 0, stock[colour]);
            var plan = OnionMenuPolicy.Distribute(OnionMenuPolicy.ReleaseCount(10, 100), 2, 3, 5);
            for (uint colour = 0; colour < 3; colour++)
                Check(Native.pik_sim_onion_call_out(sim, (uint)captain, (uint)onion, colour, plan[colour]) == plan[colour],
                    "mixed release reserves native exits: " + colour);
            uint inside = 0;
            for (uint colour = 0; colour < 3; colour++)
            {
                Native.pik_sim_onion_counts(sim, (uint)onion, (uint)captain, colour, out var counts);
                inside += counts.in_onion;
            }
            Check(inside == 1 && OnionMenuPolicy.ReleaseCount(inside, 100) == 0,
                "pending native exits are excluded; a repeated dump keeps the single reserve");
            for (int tick = 0; tick < 180; tick++) Native.pik_sim_advance(sim, ref host, 1f / 30f);
            uint squad = 0;
            for (uint colour = 0; colour < 3; colour++)
            {
                Native.pik_sim_onion_counts(sim, (uint)onion, (uint)captain, colour, out var counts);
                squad += counts.in_squad;
                Check(Native.pik_sim_onion_put_away(sim, (uint)captain, (uint)onion, colour, counts.in_squad) == counts.in_squad,
                    "whole-squad return accepts colour " + colour);
            }
            Check(squad == 9, "all nine released Pikmin joined the squad");
            for (int tick = 0; tick < 900; tick++) Native.pik_sim_advance(sim, ref host, 1f / 30f);
            inside = 0;
            for (uint colour = 0; colour < 3; colour++)
            {
                Native.pik_sim_onion_counts(sim, (uint)onion, (uint)captain, colour, out var counts);
                inside += counts.in_onion;
            }
            Check(inside == 10 && Native.pik_sim_poisoned(sim) == 0, "return stores every colour without losing Pikmin");
            float healed = Native.pik_sim_sacrifice_onion(sim, (uint)onion, 0.11f, out uint sacrificed);
            Check(sacrificed == 3 && Math.Abs(healed - 0.11f) < 0.0001f, "Onion-only sacrifice count and health fraction cross ABI");
            Native.pik_sim_set_captain_jump_height(sim, (uint)captain, 150f);
            var jump = new Native.Input { jump = 1 };
            Native.pik_sim_set_input(sim, (uint)captain, ref jump);
            float peak = 0f;
            for (int tick = 0; tick < 120; tick++)
            {
                Native.pik_sim_advance(sim, ref host, 1f / 30f);
                Native.pik_sim_captain(sim, (uint)captain, out var state);
                peak = Math.Max(peak, state.pos.y);
            }
            Check(peak >= 150f, "host-sized jump reaches its requested apex through ABI");
        }
        finally { Native.pik_sim_free(sim); }
    }

    static void CheckIntroCache(string assets)
    {
        var message = new byte[2048];
        Check(Native.pik_render_stings(Native.CString(assets), message, (uint)message.Length) != 0,
            "intro rendered from extracted banks via P/Invoke: " + Native.Utf8(message));
        string path = Path.Combine(assets, "ror2_audio", "spawn.wav");
        if (!File.Exists(path)) { Check(false, "intro cache exists"); return; }
        var wav = File.ReadAllBytes(path);
        Check(wav.Length == 448044 && BitConverter.ToInt32(wav, 24) == 32000 && BitConverter.ToInt16(wav, 22) == 2,
            "cached intro is 3.5 seconds of stereo PCM16 at 32 kHz");
        DateTime before = File.GetLastWriteTimeUtc(path);
        Check(Native.pik_render_stings(Native.CString(assets), message, (uint)message.Length) != 0
            && File.GetLastWriteTimeUtc(path) == before, "later startups reuse completed intro cache");
    }

    static int Main(string[] args)
    {
        CheckJumpButton();
        string dll = Path.GetFullPath(args.Length > 0 ? args[0] : "pikmin.dll");
        Check(Native.Load(dll, out string err), $"load {dll} {err}");
        if (err != null) return 1;
        Check(Native.pik_tick_hz() == 30, "30 Hz");
        if (args.Length > 1)
        {
            CheckIntroCache(args[1]);
            NumberAtlasCheck.Run(args[1], Check, args.Length > 3 ? args[2] : null, args.Length > 3 ? args[3] : null);
        }

        var host = new Native.Host
        {
            ground_height = Marshal.GetFunctionPointerForDelegate(groundFn),
            walls = Marshal.GetFunctionPointerForDelegate(wallsFn),
            damage = Marshal.GetFunctionPointerForDelegate(damageFn),
        };
        if (args.Length > 1) CheckShipRepair(args[1], ref host);
        CheckOnionRevival(ref host);
        CheckUnifiedOnionMenu(ref host);
        // An assets folder as the second argument checks the disc tuning loads too.
        IntPtr sim = Native.pik_sim_new(args.Length > 1 ? Native.CString(args[1]) : null, 7);
        if (args.Length > 1) Check(Native.pik_assets_complete(Native.CString(args[1])) == 1, "assets complete");
        Check(sim != IntPtr.Zero, "sim started: " + Native.Utf8(Native.pik_sim_params_source(sim)));

        var origin = new Native.Vec3(0, 0, 0);
        int c = Native.pik_sim_add_captain(sim, ref origin);
        IntPtr arrivalSim = Native.pik_sim_new(null, 17);
        int arrivalCaptain = Native.pik_sim_add_captain(arrivalSim, ref origin);
        var air = new Native.Vec3(0, 600, 0);
        int arrivalOnion = Native.pik_sim_add_onion(arrivalSim, ref origin, 7);
        Native.pik_sim_set_onion_color(arrivalSim, (uint)arrivalOnion, Native.Yellow);
        Native.pik_sim_store_in_onion(arrivalSim, (uint)arrivalOnion, Native.Blue, 0, 4);
        Native.pik_sim_set_onion_pose(arrivalSim, (uint)arrivalOnion, ref air, 1);
        Check(Native.pik_sim_onion_call_out(arrivalSim, (uint)arrivalCaptain, (uint)arrivalOnion, Native.Blue, 1) == 1, "flying Onion queues leg exits");
        Native.pik_sim_set_onion_flight_motion(arrivalSim, (uint)arrivalOnion, 1, 0.5f);
        int ship = Native.pik_sim_add_ship(arrivalSim, ref air, 0f);
        Native.pik_sim_set_onion_pose(arrivalSim, (uint)ship, ref air, 1);
        Native.pik_sim_set_onion_flight_motion(arrivalSim, (uint)ship, 1, 0.5f);
        Check(Native.pik_sim_poisoned(arrivalSim) == 0, "host-timed Onion and Dolphin animation ABI");
        var goals = new Native.Onion[4];
        Check(Native.pik_sim_onions(arrivalSim, goals, 4) == 2 && goals[0].colors == 7 && goals[0].anim == 6
            && goals[1].is_ship == 1, "one unified flying Onion alongside Dolphin");
        Native.pik_sim_set_onion_pose(arrivalSim, (uint)arrivalOnion, ref origin, 0);
        Native.pik_sim_set_onion_pose(arrivalSim, (uint)ship, ref origin, 0);
        Check(Native.pik_sim_onion_counts(arrivalSim, (uint)arrivalOnion, (uint)arrivalCaptain, Native.Blue, out var stock) != 0
            && stock.in_onion == 3, "cosmetic colour and landing preserve blue storage minus queued exit");
        Native.pik_sim_free(arrivalSim);
        CheckReplica(ref host);
        IntPtr jumpSim = Native.pik_sim_new(null, 31);
        int jumper = Native.pik_sim_add_captain(jumpSim, ref origin);
        var followerAt = new Native.Vec3(-60, 0, 0);
        Native.pik_sim_spawn_piki(jumpSim, Native.Red, ref followerAt, jumper);
        Native.pik_sim_advance(jumpSim, ref host, 0.04f);
        var jumpInput = new Native.Input { jump = 1, jump_blocked = 1 };
        Native.pik_sim_set_input(jumpSim, (uint)jumper, ref jumpInput);
        Native.pik_sim_advance(jumpSim, ref host, 0.04f);
        Native.pik_sim_captain(jumpSim, (uint)jumper, out var jumpCaptain);
        Check(jumpCaptain.on_ground == 1, "jump modifier crosses ABI and blocks takeoff");
        jumpInput = new Native.Input();
        Native.pik_sim_set_input(jumpSim, (uint)jumper, ref jumpInput);
        jumpInput.jump = 1;
        Native.pik_sim_set_input(jumpSim, (uint)jumper, ref jumpInput);
        Native.pik_sim_advance(jumpSim, ref host, 0.04f);
        Native.pik_sim_captain(jumpSim, (uint)jumper, out jumpCaptain);
        var jumpPikis = new Native.Piki[2];
        Native.pik_sim_pikis(jumpSim, jumpPikis, 2);
        Check(jumpCaptain.on_ground == 0 && jumpCaptain.pos.y > 0 && jumpPikis[0].pos.y > 0,
            "B jump crosses ABI and stationary squad hops with Olimar");
        for (int i = 0; i < 90; i++) Native.pik_sim_advance(jumpSim, ref host, 0.04f);
        Native.pik_sim_captain(jumpSim, (uint)jumper, out jumpCaptain);
        Check(jumpCaptain.on_ground == 1 && jumpCaptain.pos.y == 0,
            "holding B lands without repeating jump");
        Native.pik_sim_free(jumpSim);
        IntPtr padSim = Native.pik_sim_new(null, 32);
        int padCaptain = Native.pik_sim_add_captain(padSim, ref origin);
        Native.pik_sim_spawn_piki(padSim, Native.Red, ref followerAt, padCaptain);
        var heldInput = new Native.Input { stick_y = 1, jump = 1, throw_ = 1, dismiss = 1 };
        Native.pik_sim_set_captain_hold(padSim, (uint)padCaptain, 2);
        Native.pik_sim_set_input(padSim, (uint)padCaptain, ref heldInput);
        Native.pik_sim_advance(padSim, ref host, 1f / 30f);
        Native.pik_sim_captain(padSim, (uint)padCaptain, out var padState);
        Check(padState.pos.y == 0 && padState.pos.z == 0, "NativeBody stun holds movement and jumping through ABI");
        var padVelocity = new Native.Vec3(0, 400, 180);
        Native.pik_sim_launch_captain(padSim, (uint)padCaptain, ref padVelocity, 200f, 1);
        Native.pik_sim_advance(padSim, ref host, 1f / 30f);
        Native.pik_sim_captain(padSim, (uint)padCaptain, out padState);
        Check(Math.Abs(padState.pos.z - 6f) < 0.01f && padState.pos.y > 13f,
            "pad launch crosses ABI and continues while stunned");
        Native.pik_sim_set_captain_hold(padSim, (uint)padCaptain, 0);
        heldInput = default;
        Native.pik_sim_set_input(padSim, (uint)padCaptain, ref heldInput);
        float followerApex = 0;
        for (int t = 0; t < 160; t++)
        {
            Native.pik_sim_advance(padSim, ref host, 1f / 30f);
            Native.pik_sim_pikis(padSim, jumpPikis, 2);
            followerApex = Math.Max(followerApex, jumpPikis[0].pos.y);
        }
        Check(followerApex > 395f, "Pikmin repeat the full pad launch, exceeding voluntary jump height");
        Check(Native.pik_sim_poisoned(padSim) == 0, "NativeBody and jump-pad ABI stays healthy");
        Native.pik_sim_free(padSim);
        for (int i = 0; i < 6; i++)
        {
            var at = new Native.Vec3(-20f - 4f * i, 0, 0);
            Native.pik_sim_spawn_piki(sim, Native.Red, ref at, c);
        }
        var target = new Native.Vec3(100, 0, 0);
        var creatures = new[] { new Native.Creature { id = 0xDEADBEEF01, pos = target, alive = 1, parts_start = 0, parts_len = 1 } };
        var parts = new[] { new Native.Part { centre = new Native.Vec3(100, 15, 0), radius = 20, stickable = 1 } };

        void Run(Native.Input input, float secs)
        {
            Native.pik_sim_set_input(sim, (uint)c, ref input);
            for (int i = 0; i < (int)(secs * 50); i++)
            {
                Native.pik_sim_set_creatures(sim, creatures, 1, parts, 1);
                Native.pik_sim_advance(sim, ref host, 0.02f);
            }
        }
        var aim = new Native.Input { use_cursor = 1, cursor = target };
        Run(aim, 1f);
        var throwing = aim;
        throwing.throw_ = 1;
        Run(throwing, 1f);
        Run(aim, 3f);

        var events = new Native.Event[256];
        uint n = Native.pik_sim_events(sim, events, (uint)events.Length);
        bool thrown = false, latched = false;
        for (int i = 0; i < n; i++)
        {
            thrown |= events[i].kind == Native.EventKind.Thrown;
            latched |= events[i].kind == Native.EventKind.Latched && events[i].has_creature == 1 && events[i].creature == 0xDEADBEEF01;
        }
        Check(thrown && latched, $"threw and latched on ({n} events)");
        Check(hits.Exists(h => h.id == 0xDEADBEEF01 && h.amount > 0 && h.source == Native.SourcePiki), $"damage callback ({hits.Count} hits)");
        Check(groundCalls > 0 && wallCalls > 0, $"ground {groundCalls}, walls {wallCalls} calls");

        Native.pik_sim_captain(sim, (uint)c, out var cap);
        Check(Math.Abs(cap.cursor_world.x - 100f) < 1f, $"cursor at {cap.cursor_world.x}");
        var pikis = new Native.Piki[4];
        uint total = Native.pik_sim_pikis(sim, pikis, 4);
        Check(total == 6 && pikis[0].color == Native.Red && pikis[0].alive == 1, $"{total} Pikmin read back into a short buffer");

        // Walk into the wall, with the creature (which he'd bump into) out of the way.
        creatures[0].pos = new Native.Vec3(100, 0, 600);
        parts[0].centre = new Native.Vec3(100, 15, 600);
        Run(new Native.Input { stick_x = 1 }, 6f);
        Native.pik_sim_captain(sim, (uint)c, out cap);
        Check(cap.pos.x > 300f && cap.pos.x <= 400f, $"stopped at the wall, x = {cap.pos.x}");

        // A corpse and an Onion.
        var desc = new Native.PelletDesc { pos = new Native.Vec3(50, 0, 50), radius = 20, height = 10, min_carriers = 1, max_carriers = 2, seeds_matching = 2, seeds_other = 1, color = -1 };
        Check(Native.pik_sim_spawn_pellet(sim, ref desc) == 0, "corpse pellet");
        var onionAt = new Native.Vec3(cap.pos.x - 30f, 0, 0);
        int onion = Native.pik_sim_add_onion(sim, ref onionAt, 0b111);
        Native.pik_sim_store_in_onion(sim, (uint)onion, Native.Yellow, 0, 4);
        Run(default, 0.2f);
        Check(Native.pik_sim_onion_in_reach(sim, (uint)c) == onion, "Onion in reach");
        Check(Native.pik_sim_onion_counts(sim, (uint)onion, (uint)c, Native.Yellow, out var counts) == 1 && counts.in_onion == 4,
            $"Onion counts {counts.in_onion}/{counts.in_squad}/{counts.on_field}/{counts.max_on_field}");
        Check(Native.pik_sim_onion_call_out(sim, (uint)c, (uint)onion, Native.Yellow, 10) == 4, "called 4 out");
        var onions = new Native.Onion[2];
        Check(Native.pik_sim_onions(sim, onions, 2) == 1 && onions[0].colors == 0b111, "Onion read back");
        var sprouts = new Native.Sprout[2];
        Native.pik_sim_sprouts(sim, sprouts, 2);
        var pelletAt = new Native.Vec3(-80, 0, 50);
        Check(Native.pik_sim_spawn_numbered_pellet(sim, 7, Native.Red, ref pelletAt, 0f) == 1, "numbered pellet");
        var pellets = new Native.Pellet[3];
        Check(Native.pik_sim_pellets(sim, pellets, 3) == 2 && pellets[0].min_carriers == 1, "pellet read back");
        Check(pellets[1].min_carriers == 5 && pellets[1].max_carriers == 10 && pellets[1].radius == 20f,
            $"5 pellet from the disc: {pellets[1].min_carriers}-{pellets[1].max_carriers} carriers, radius {pellets[1].radius}");
        Check(Native.pik_sim_captain_throw_color(sim, (uint)c) == -1, "no throw colour picked yet");
        Native.pik_sim_set_auto_rejoin(sim, 1);
        Native.pik_sim_set_shake_protection(sim, 2f);
        Native.pik_sim_shake_off(sim, 12345);
        Native.pik_sim_set_captain_reach(sim, (uint)c, 2f);
        var centre = new Native.Vec3(cap.pos.x, 0, 0);
        Native.pik_sim_attack_pikis_in_sphere(sim, ref centre, 1000f, 1000f);
        Check(Native.pik_sim_poisoned(sim) == 0, "not poisoned");

        if (args.Length > 1)
        {
            // The Pianta skin (needs the Sunshine cache the Mario plugin extracts): it joins the model table,
            // a captain can wear it and the view draws the captain with that model.
            string sunshine = Environment.GetEnvironmentVariable("SMS_ASSETS")
                ?? Path.GetFullPath(Path.Combine(args[1], "..", "..", "libsms", "assets"));
            if (File.Exists(Path.Combine(sunshine, "npc", "montem", "mom_model.bmd")))
            {
                IntPtr pv = Native.pik_view_new(Native.CString(args[1]), 0);
                int pianta = Native.pik_view_load_pianta(pv, Native.CString(sunshine), 0, uint.MaxValue);
                Check(pianta >= 0 && Native.pik_view_model_count(pv) == (uint)pianta + 1, "the Pianta joined the model table");
                Native.pik_view_set_pianta_height(pv, 30f);
                IntPtr psim = Native.pik_sim_new(Native.CString(args[1]), 7);
                var pOrigin = new Native.Vec3();
                Check(Native.pik_sim_add_captain(psim, ref pOrigin) == 0, "pianta: captain");
                Native.pik_view_set_captain_skin(pv, 0, 2);
                var pframe = new Native.ViewFrame { dt = 1f / 60f, listener = -1, volume = 0f,
                    forward = new Native.Vec3(0, 0, 1), up = new Native.Vec3(0, 1, 0) };
                var pidle = new Native.Input();
                Native.pik_sim_set_input(psim, 0, ref pidle);
                Native.pik_sim_advance(psim, ref host, 1f / 30f);
                Native.pik_view_update(pv, psim, ref pframe);
                var pdraws = new ViewDraw[64];
                uint pCount = Native.pik_view_draws(pv, pdraws, (uint)pdraws.Length);
                bool worn = false;
                for (int i = 0; i < pCount; i++) if (pdraws[i].kind == Native.KindCaptain) worn = pdraws[i].model == (uint)pianta;
                Check(worn, "the captain is drawn as the Pianta");
                Native.pik_sim_free(psim);
                Native.pik_view_free(pv);
                Console.WriteLine("ok   Pianta skin: model " + pianta + " worn by the captain");
            }
            else Console.WriteLine("SKIP Pianta skin: no Sunshine cache at " + sunshine);

            // Louie (needs the Pikmin 2 cache extracted into <assets>/p2).
            string p2 = Path.Combine(args[1], "p2");
            if (Native.pik_p2_complete(Native.CString(p2)) != 0)
            {
                IntPtr lv = Native.pik_view_new(Native.CString(args[1]), 0);
                int louie = Native.pik_view_load_louie(lv, Native.CString(p2));
                Check(louie >= 0 && Native.pik_view_model_count(lv) == (uint)louie + 1, "Louie joined the model table");
                IntPtr lsim = Native.pik_sim_new(Native.CString(args[1]), 7);
                var lorigin = new Native.Vec3();
                Check(Native.pik_sim_add_captain(lsim, ref lorigin) == 0, "louie: captain");
                Native.pik_view_set_captain_skin(lv, 0, 1);
                var lframe = new Native.ViewFrame { dt = 1f / 60f, listener = -1, volume = 0f,
                    forward = new Native.Vec3(0, 0, 1), up = new Native.Vec3(0, 1, 0) };
                var lidle = new Native.Input();
                Native.pik_sim_set_input(lsim, 0, ref lidle);
                Native.pik_sim_advance(lsim, ref host, 1f / 30f);
                Native.pik_view_update(lv, lsim, ref lframe);
                var ldraws = new ViewDraw[64];
                uint lCount = Native.pik_view_draws(lv, ldraws, (uint)ldraws.Length);
                bool wearing = false;
                for (int i = 0; i < lCount; i++) if (ldraws[i].kind == Native.KindCaptain) wearing = ldraws[i].model == (uint)louie;
                Check(wearing, "the captain is drawn as Louie");
                Native.pik_sim_free(lsim);
                Native.pik_view_free(lv);
                Console.WriteLine("ok   Louie skin: model " + louie + " worn by the captain");
            }
            else Console.WriteLine("SKIP Louie skin: no Pikmin 2 cache at " + p2);

            // The view: models through the same structs the plugin builds meshes from, then a
            // frame posed (no sound: nothing should play from a check).
            IntPtr view = Native.pik_view_new(Native.CString(args[1]), 0);
            Check(view != IntPtr.Zero, "view: " + Native.Utf8(Native.pik_view_status(view)));
            uint models = Native.pik_view_model_count(view);
            int badModels = 0;
            for (uint m = 0; m < models; m++)
            {
                Native.pik_view_model(view, m, out var info);
                var batches = new ViewBatch[info.batch_count];
                var idx = new int[info.index_count];
                var uv = new float[info.vertex_count * 2];
                bool ok = Native.pik_view_batches(view, m, batches, info.batch_count) == info.batch_count
                          && Native.pik_view_indices(view, m, idx, info.index_count) == info.index_count
                          && Native.pik_view_uvs(view, m, uv, info.vertex_count) == info.vertex_count
                          && Array.TrueForAll(idx, i => i >= 0 && i < info.vertex_count);
                foreach (var b in batches)
                    if (b.texture >= 0)
                    {
                        ok &= Native.pik_view_texture(view, (uint)b.texture, out var t, null, 0) == 1;
                        var px = new byte[t.width * t.height * 4];
                        ok &= Native.pik_view_texture(view, (uint)b.texture, out t, px, (uint)px.Length) == 1;
                    }
                if (!ok) badModels++;
            }
            Check(models == 19 && badModels == 0, $"{models} models read back ({badModels} bad)");
            var frame = new Native.ViewFrame
            {
                dt = 1f / 60f, forward = new Native.Vec3(0, 0, 1), up = new Native.Vec3(0, 1, 0), listener = c, volume = 1f,
            };
            Native.pik_view_update(view, sim, ref frame);
            uint nd = Native.pik_view_draws(view, null, 0);
            var draws = new ViewDraw[nd];
            Native.pik_view_draws(view, draws, nd);
            Native.pik_view_positions(view, out uint vc);
            Native.pik_view_colors(view, out uint cc);
            IntPtr idsPtr = Native.pik_view_texture_ids(view, out uint tc);
            var ids = new int[tc];
            Marshal.Copy(idsPtr, ids, 0, (int)tc);
            int textured = 0, badTextures = 0;
            foreach (int id in ids)
            {
                if (id < 0) continue;
                textured++;
                if (Native.pik_view_texture(view, (uint)id, out var t, null, 0) == 0 || t.width == 0) badTextures++;
            }
            Check(tc == cc && textured > 0 && badTextures == 0, $"{textured} textured batches drawn ({badTextures} bad)");
            var navi = Array.Find(draws, d => d.kind == Native.KindCaptain);
            Native.pik_view_model(view, navi.model, out var ni);
            Console.WriteLine($"  Olimar's model: {ni.hi.y - ni.lo.y:F1} units tall (bind pose)");
            var last = draws[nd - 1];
            Native.pik_view_model(view, last.model, out var li);
            Check(Array.Exists(draws, d => d.kind == Native.KindCaptain) && Array.Exists(draws, d => d.kind == Native.KindOnion)
                  && last.first_vertex + li.vertex_count == vc && last.first_color + li.batch_count == cc,
                $"{nd} draws, {vc} vertices, {cc} colours");

            var hover = new Native.Vec3(onionAt.x, 125f, onionAt.z);
            Native.pik_sim_set_onion_pose(sim, (uint)onion, ref hover, 1);
            Native.pik_view_set_onion_ground(view, (uint)onion, 0f);
            for (int i = 0; i < 60; i++) Native.pik_view_update(view, sim, ref frame);
            var beamDraws = new ViewDraw[Native.pik_view_draws(view, null, 0)];
            Native.pik_view_draws(view, beamDraws, (uint)beamDraws.Length);
            Check(!Array.Exists(beamDraws, d => d.kind == Native.KindBeam), "flying Onion retracts its beam");
            Native.pik_view_set_onion_menu_beam(view, (uint)onion, 1);
            Native.pik_view_update(view, sim, ref frame);
            beamDraws = new ViewDraw[Native.pik_view_draws(view, null, 0)];
            Native.pik_view_draws(view, beamDraws, (uint)beamDraws.Length);
            Check(Array.Exists(beamDraws, d => d.kind == Native.KindBeam), "flying menu stretches its beam to the ground");
            Native.pik_view_set_onion_menu_beam(view, (uint)onion, 0);
            for (int i = 0; i < 60; i++) Native.pik_view_update(view, sim, ref frame);
            beamDraws = new ViewDraw[Native.pik_view_draws(view, null, 0)];
            Native.pik_view_draws(view, beamDraws, (uint)beamDraws.Length);
            Check(!Array.Exists(beamDraws, d => d.kind == Native.KindBeam), "leaving the flying menu retracts its beam");
            Native.pik_sim_set_onion_pose(sim, (uint)onion, ref onionAt, 0);

            CheckRenderClock(view, sim, ref host, c);

            // Whistling makes particles, each with a texture.
            var whistle = new Native.Input { whistle = 1 };
            Native.pik_sim_set_input(sim, (uint)c, ref whistle);
            for (int i = 0; i < 20; i++)
            {
                Native.pik_sim_set_creatures(sim, creatures, 1, parts, 1);
                Native.pik_sim_advance(sim, ref host, 1f / 60f);
                Native.pik_view_update(view, sim, ref frame);
            }
            Native.pik_view_particles(view, out uint pn);
            Check(pn > 0 && Native.pik_view_particle_texture(view, 0, out var pt, null, 0) == 1 && pt.width > 0,
                $"{pn} whistle particles");

            // The character select screen's view: no cursor or marker.
            Native.pik_view_show_cursors(view, 0);
            Native.pik_view_update(view, sim, ref frame);
            var quiet = new ViewDraw[Native.pik_view_draws(view, null, 0)];
            Native.pik_view_draws(view, quiet, (uint)quiet.Length);
            Check(!Array.Exists(quiet, d => d.kind == Native.KindCursor || d.kind == Native.KindMarker)
                  && Array.Exists(quiet, d => d.kind == Native.KindCaptain), "cursors hidden");
            Native.pik_view_free(view);

            // The sound output opens and closes in a plain process (nothing is played).
            IntPtr loud = Native.pik_view_new(Native.CString(args[1]), 1);
            Check(loud != IntPtr.Zero, "view with sound: " + Native.Utf8(Native.pik_view_status(loud)));
            var bell = new float[640000];
            Check(Native.pik_view_take_death_sting(loud, bell, 1) == 0, "death bell rejects a short buffer");
            Check(Native.pik_view_take_death_sting(loud, bell, (uint)bell.Length) == bell.Length,
                "full sundown bell crosses the ABI as 10 seconds of stereo 32 kHz PCM");
            Native.pik_view_free(loud);
            double Energy(int first, int last)
            {
                double energy = 0;
                for (int i = first * 64000; i < last * 64000; i++) energy += bell[i] * bell[i];
                return energy;
            }
            Check(Array.TrueForAll(bell, sample => !float.IsNaN(sample) && !float.IsInfinity(sample)), "death PCM is finite");
            Check(Energy(0, 3) > 0.01 && Energy(4, 6) > 0.01,
                "both original bell phrases remain available after the native stream is freed");
            Check(Energy(9, 10) < 0.00001, "full bell decay finishes before the PCM ends");
        }
        Native.pik_sim_free(sim);

        Console.WriteLine(failures == 0 ? "all good" : $"{failures} failed");
        return failures == 0 ? 0 : 1;
    }
}
