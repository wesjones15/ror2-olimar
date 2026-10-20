using System;
using System.Collections.Generic;
using System.Linq;
using Ror2Common;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace OlimarTest
{
    /// What another plugin's character calls to play Ship Repair (Mario's plugin finds this class by reflection, so
    /// its members take only plain and Unity types). A guest registers his body, and the mode then lands him a
    /// Dolphin by the teleporter; he takes a ship part from the ground (Takeables, Take), keeps it in his hands
    /// (Hold every frame), and sets it down or throws it (Put, Throw), and it goes into his Dolphin's receiver if
    /// it lands there. Parts are the Pikmin sim's pellets, so everything runs on the server and clients send it
    /// their requests (ShipRepairGuests).
    public static class ShipRepairApi
    {
        public const int Version = 1;

        /// A Ship Repair run is going.
        public static bool Active => ShipRepairMode.Active;

        /// Lets players of the body with this name (its catalog name, "MarioTestBody") choose it for Ship Repair.
        public static void AllowGuest(string bodyName) => ShipRepairGuests.Allow(bodyName);

        /// A guest's body started (on every machine). On the server, the mode lands his Dolphin.
        public static void RegisterGuest(CharacterBody body) => ShipRepairGuests.Register(body);

        /// Parts within `radius` metres of `at` that can be picked up: fills the arrays (positions in metres, radii
        /// in metres) and returns how many.
        public static int Takeables(Vector3 at, float radius, uint[] ids, Vector3[] positions, float[] radii) =>
            ShipRepairGuests.Takeables(at, radius, ids, positions, radii);

        /// The local guest closes his hands on the part. False if it can't be had.
        public static bool Take(uint part) => ShipRepairGuests.Take(part);

        /// The part is in the local guest's hands at `at`, facing `yaw` (radians); call it every frame.
        public static void Hold(uint part, Vector3 at, float yaw) => ShipRepairGuests.Hold(part, at, yaw);

        /// He sets it down at `at`; into the receiver if that is where it is.
        public static void Put(uint part, Vector3 at, float yaw) => ShipRepairGuests.Put(part, at, yaw);

        /// He throws it from `from` at `velocity` (metres a second); into the receiver if it flies in.
        public static void Throw(uint part, Vector3 from, Vector3 velocity) => ShipRepairGuests.Throw(part, from, velocity);

        /// Lets go of whatever the local guest holds (he died, or the stage ended).
        public static void Drop(uint part) => ShipRepairGuests.Drop(part);
    }

    internal static class ShipRepairGuests
    {
        const short TakeMsg = 0x4F07, PoseMsg = 0x4F08, PutMsg = 0x4F09, ThrowMsg = 0x4F0A, DropMsg = 0x4F0B;
        /// How close (metres, from his feet to the edge of the part) a guest has to be to take it, and how far a
        /// held part may be from him.
        const float TakeReach = 7f, HoldReach = 14f;
        const float PoseInterval = 0.05f, HoldTimeout = 1.5f, MaxFlight = 8f;
        static readonly HashSet<string> allowed = new HashSet<string>();
        static readonly List<CharacterBody> guests = new List<CharacterBody>();
        static bool registered;

        sealed class Held
        {
            public uint user;
            public Vector3 pos;
            public float yaw, at;
        }
        sealed class Flight
        {
            public uint pellet, user;
            public Vector3 pos, vel;
            public float yaw, age;
        }
        static readonly Dictionary<uint, Held> held = new Dictionary<uint, Held>();
        static readonly List<Flight> flights = new List<Flight>();
        static readonly Dictionary<uint, float> lastSent = new Dictionary<uint, float>();

        public static void Allow(string bodyName)
        {
            allowed.Add(bodyName);
            if (registered) return;
            registered = true;
            Mp.Register(TakeMsg, OnTake, null);
            Mp.Register(PoseMsg, OnPose, null);
            Mp.Register(PutMsg, OnPut, null);
            Mp.Register(ThrowMsg, OnThrow, null);
            Mp.Register(DropMsg, OnDrop, null);
        }

        internal static bool IsAllowed(SurvivorDef survivor) =>
            survivor && survivor.bodyPrefab && allowed.Contains(survivor.bodyPrefab.name);

        internal static bool IsGuestUser(NetworkUser user)
        {
            var prefab = user ? BodyCatalog.GetBodyPrefab(user.bodyIndexPreference) : null;
            return prefab && allowed.Contains(prefab.name);
        }

        internal static bool AnyGuests => allowed.Count > 0;

        public static void Register(CharacterBody body)
        {
            if (!body || guests.Contains(body)) return;
            guests.RemoveAll(b => !b);
            guests.Add(body);
        }

        static uint UserOf(CharacterBody body)
        {
            var master = body ? body.master : null;
            var controller = master ? master.GetComponent<PlayerCharacterMasterController>() : null;
            return controller && controller.networkUser ? controller.networkUser.netId.Value : 0;
        }

        static CharacterBody LocalBody()
        {
            var user = LocalUserManager.GetFirstLocalUser();
            return user != null ? user.cachedBody : null;
        }

        static CharacterBody BodyOfUser(uint user)
        {
            foreach (var b in guests) if (b && UserOf(b) == user) return b;
            return null;
        }

        static uint UserOfConnection(NetworkConnection from)
        {
            foreach (var u in NetworkUser.readOnlyInstancesList)
                if (u.connectionToClient == from) return u.netId.Value;
            return 0;
        }

        // ---------------------------------------------------------------------------------------------
        // Server

        /// Each fixed step on the server: lands the guests' Dolphins, keeps held parts where their guests have
        /// them and flies the thrown ones.
        internal static void Tick()
        {
            if (!NetworkServer.active || !ShipRepairMode.Active || !Stage.instance) return;
            guests.RemoveAll(b => !b);
            if (guests.Count > 0 && Plugin.AssetsSettled)
            {
                var world = PikminWorld.Ensure();
                foreach (var body in guests)
                {
                    uint user = UserOf(body);
                    if (user != 0 && body.healthComponent && body.healthComponent.alive && (!world.StageCurrent || !world.GuestReady(user)))
                        world.RegisterGuest(body, user);
                }
            }
            var w = PikminWorld.Instance;
            if (!w || w.ClientMode || w.SimHandle == IntPtr.Zero) return;
            float now = Time.time;
            List<uint> drop = null;
            foreach (var kv in held)
            {
                var h = kv.Value;
                var body = BodyOfUser(h.user);
                bool gone = !body || !body.healthComponent || !body.healthComponent.alive || now - h.at > HoldTimeout;
                if (gone || !w.HoldPart(kv.Key, h.pos, h.yaw)) (drop ??= new List<uint>()).Add(kv.Key);
            }
            if (drop != null)
                foreach (uint p in drop) Release(w, p, true);
            for (int i = flights.Count - 1; i >= 0; i--)
            {
                if (!Fly(w, flights[i])) flights.RemoveAt(i);
            }
        }

        /// Sets a held part down on the ground below it (or into a receiver in reach).
        static void Release(PikminWorld w, uint pellet, bool tryHand)
        {
            if (!held.TryGetValue(pellet, out var h))
            {
                return;
            }
            held.Remove(pellet);
            lastSent.Remove(pellet);
            LandAt(w, pellet, h.user, h.pos, h.yaw, tryHand);
        }

        static void LandAt(PikminWorld w, uint pellet, uint user, Vector3 at, float yaw, bool tryHand)
        {
            var ground = at;
            if (Physics.Raycast(at + Vector3.up * 1.5f, Vector3.down, out var hit, 60f, UnityHost.Mask, QueryTriggerInteraction.Ignore))
                ground = hit.point;
            w.HoldPart(pellet, at, yaw);
            if (tryHand && w.HandPart(user, pellet)) return;
            w.ReleasePart(pellet, ground, yaw);
        }

        /// One tick of a thrown part's arc; false once it has landed.
        static bool Fly(PikminWorld w, Flight f)
        {
            float dt = Time.fixedDeltaTime;
            f.age += dt;
            f.vel += Physics.gravity * dt;
            var next = f.pos + f.vel * dt;
            bool hitSomething = Physics.Linecast(f.pos, next, out var hit, UnityHost.Mask, QueryTriggerInteraction.Ignore);
            if (hitSomething) next = hit.point + hit.normal * 0.15f;
            f.pos = next;
            f.yaw += dt * 6f;
            if (!w.HoldPart(f.pellet, f.pos, f.yaw)) return false;
            // Flying into a receiver puts it in.
            if (w.HandPart(f.user, f.pellet)) return false;
            if (hitSomething || f.age > MaxFlight)
            {
                LandAt(w, f.pellet, f.user, f.pos, f.yaw, true);
                return false;
            }
            return true;
        }

        static bool ServerTake(uint user, uint pellet)
        {
            var w = PikminWorld.Instance;
            var body = BodyOfUser(user);
            if (!w || w.ClientMode || !body || !w.TryPart(pellet, out var part)) return false;
            if (part.state != 0 && part.state != 1) return false;
            if (held.ContainsKey(pellet) || flights.Any(f => f.pellet == pellet)) return false;
            var at = UnityHost.ToUnity(part.pos);
            float reach = part.radius * UnityHost.Scale + TakeReach;
            if (Vector3.ProjectOnPlane(at - body.footPosition, Vector3.up).magnitude > reach || Mathf.Abs(at.y - body.footPosition.y) > 6f) return false;
            held[pellet] = new Held { user = user, pos = at, yaw = part.yaw, at = Time.time };
            return w.HoldPart(pellet, at, part.yaw);
        }

        static bool OwnedBy(uint pellet, uint user, out Held h) => held.TryGetValue(pellet, out h) && h.user == user;

        static void ServerPose(uint user, uint pellet, Vector3 at, float yaw)
        {
            if (!OwnedBy(pellet, user, out var h)) return;
            var body = BodyOfUser(user);
            if (!body || !Finite(at) || float.IsNaN(yaw)) return;
            var offset = at - body.footPosition;
            if (offset.magnitude > HoldReach) at = body.footPosition + offset.normalized * HoldReach;
            h.pos = at; h.yaw = yaw; h.at = Time.time;
        }

        static void ServerPut(uint user, uint pellet, Vector3 at, float yaw)
        {
            ServerPose(user, pellet, at, yaw);
            var w = PikminWorld.Instance;
            if (w && OwnedBy(pellet, user, out _)) Release(w, pellet, true);
        }

        static void ServerThrow(uint user, uint pellet, Vector3 from, Vector3 velocity)
        {
            var w = PikminWorld.Instance;
            if (!w || !OwnedBy(pellet, user, out var h) || !Finite(velocity)) return;
            ServerPose(user, pellet, from, h.yaw);
            if (velocity.magnitude > 40f) velocity = velocity.normalized * 40f;
            held.Remove(pellet);
            lastSent.Remove(pellet);
            flights.Add(new Flight { pellet = pellet, user = user, pos = h.pos, vel = velocity, yaw = h.yaw });
        }

        static bool Finite(Vector3 v) => !(float.IsNaN(v.x + v.y + v.z) || float.IsInfinity(v.x + v.y + v.z));

        // ---------------------------------------------------------------------------------------------
        // The calls of the local guest

        public static int Takeables(Vector3 at, float radius, uint[] ids, Vector3[] positions, float[] radii)
        {
            var w = PikminWorld.Instance;
            if (!w || !ShipRepairMode.Active) return 0;
            int n = 0;
            for (int i = 0; i < w.PelletCount && n < ids.Length; i++)
            {
                var p = w.Pellets[i];
                if (p.has_ship_part == 0 || (p.state != 0 && p.state != 1)) continue;
                var pos = UnityHost.ToUnity(p.pos);
                float r = p.radius * UnityHost.Scale;
                if (Vector3.Distance(pos, at) > radius + r) continue;
                ids[n] = p.id; positions[n] = pos; radii[n] = r; n++;
            }
            return n;
        }

        public static bool Take(uint part)
        {
            var w = PikminWorld.Instance;
            var body = LocalBody();
            uint user = UserOf(body);
            if (!w || user == 0) return false;
            if (NetworkServer.active)
            {
                Register(body);
                return ServerTake(user, part);
            }
            if (!w.TryPart(part, out var p) || (p.state != 0 && p.state != 1)) return false;
            var w2 = new ByteWriter(8);
            w2.U32(part);
            Mp.SendToServer(TakeMsg, w2.ToArray(), true);
            return true;
        }

        public static void Hold(uint part, Vector3 at, float yaw)
        {
            uint user = UserOf(LocalBody());
            if (user == 0) return;
            if (NetworkServer.active) { ServerPose(user, part, at, yaw); return; }
            float now = Time.unscaledTime;
            if (lastSent.TryGetValue(part, out float last) && now - last < PoseInterval) return;
            lastSent[part] = now;
            Mp.SendToServer(PoseMsg, Pose(part, at, yaw), false);
        }

        static byte[] Pose(uint part, Vector3 at, float yaw)
        {
            var w = new ByteWriter(24);
            w.U32(part); w.F32(at.x); w.F32(at.y); w.F32(at.z); w.F32(yaw);
            return w.ToArray();
        }

        public static void Put(uint part, Vector3 at, float yaw)
        {
            uint user = UserOf(LocalBody());
            if (user == 0) return;
            if (NetworkServer.active) { ServerPut(user, part, at, yaw); return; }
            Mp.SendToServer(PutMsg, Pose(part, at, yaw), true);
        }

        public static void Throw(uint part, Vector3 from, Vector3 velocity)
        {
            uint user = UserOf(LocalBody());
            if (user == 0) return;
            if (NetworkServer.active) { ServerThrow(user, part, from, velocity); return; }
            var w = new ByteWriter(32);
            w.U32(part); w.F32(from.x); w.F32(from.y); w.F32(from.z); w.F32(velocity.x); w.F32(velocity.y); w.F32(velocity.z);
            Mp.SendToServer(ThrowMsg, w.ToArray(), true);
        }

        public static void Drop(uint part)
        {
            uint user = UserOf(LocalBody());
            if (user == 0) return;
            if (NetworkServer.active)
            {
                var w = PikminWorld.Instance;
                if (w && OwnedBy(part, user, out _)) Release(w, part, false);
                return;
            }
            var wr = new ByteWriter(8);
            wr.U32(part);
            Mp.SendToServer(DropMsg, wr.ToArray(), true);
        }

        // ---------------------------------------------------------------------------------------------
        // Messages from clients

        static void OnTake(NetworkConnection from, ByteReader r)
        {
            uint part = r.U32();
            uint user = UserOfConnection(from);
            if (!r.Failed && user != 0) ServerTake(user, part);
        }

        static void OnPose(NetworkConnection from, ByteReader r)
        {
            uint part = r.U32();
            var at = new Vector3(r.F32(), r.F32(), r.F32());
            float yaw = r.F32();
            uint user = UserOfConnection(from);
            if (!r.Failed && user != 0) ServerPose(user, part, at, yaw);
        }

        static void OnPut(NetworkConnection from, ByteReader r)
        {
            uint part = r.U32();
            var at = new Vector3(r.F32(), r.F32(), r.F32());
            float yaw = r.F32();
            uint user = UserOfConnection(from);
            if (!r.Failed && user != 0) ServerPut(user, part, at, yaw);
        }

        static void OnThrow(NetworkConnection from, ByteReader r)
        {
            uint part = r.U32();
            var at = new Vector3(r.F32(), r.F32(), r.F32());
            var vel = new Vector3(r.F32(), r.F32(), r.F32());
            uint user = UserOfConnection(from);
            if (!r.Failed && user != 0) ServerThrow(user, part, at, vel);
        }

        static void OnDrop(NetworkConnection from, ByteReader r)
        {
            uint part = r.U32();
            uint user = UserOfConnection(from);
            var w = PikminWorld.Instance;
            if (!r.Failed && user != 0 && w && OwnedBy(part, user, out _)) Release(w, part, false);
        }
    }
}
