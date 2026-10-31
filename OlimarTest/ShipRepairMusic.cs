using System;
using System.Linq;
using Ror2Common;
using RoR2;
using UnityEngine;

namespace OlimarTest
{
    // Local soundtrack only. Native sequencing loops the disc's original songs;
    // Wwise's normal selection is restored for bosses, other modes and teardown.
    public sealed class ShipRepairMusic : MonoBehaviour
    {
        IntPtr music;
        Run run;
        int stage = -1;
        uint song;
        bool failed, ownsTrack;
        uint lastMenuSong;
        float enemyUntil, scanAt;
        void Awake() => MusicController.pickTrackHook += PickTrack;
        void OnDestroy() { MusicController.pickTrackHook -= PickTrack; Stop(); }
        void Stop()
        {
            ownsTrack = false;
            lastMenuSong = 0;
            if (music != IntPtr.Zero) { Native.pik_music_free(music); music = IntPtr.Zero; }
        }
        void PickTrack(MusicController controller, ref MusicTrackDef track)
        {
            if (ownsTrack) track = null;
        }
        void Update()
        {
            var current = Run.instance;
            if (run != current) { Stop(); run = current; stage = -1; song = 0; failed = false; }
            bool menu = ShipRepairMode.LobbyActive;
            bool results = RunAudio.Ended && ShipRepairSession.ResultSummary != null && FindObjectOfType<GameOverController>();
            if ((menu || results) && Plugin.StageMusic.Value && Plugin.AssetsReady)
            {
                if (failed) return;
                if (music == IntPtr.Zero)
                {
                    music = Native.pik_music_new(Native.CString(Plugin.AssetsDir));
                    if (music == IntPtr.Zero) { failed = true; Plugin.Log.LogWarning("Pikmin menu music could not start; retaining RoR2 music."); return; }
                }
                // piki_scene.c maps the title to BGM_Jungle and the space-themed
                // world map to BGM_Map. The same independent stream serves gameplay.
                uint menuSong = menu ? 7u : 12u;
                float gain = GameSettings.MusicVolume() * Mathf.Clamp(Plugin.StageMusicVolume.Value, 0f, 2f);
                ownsTrack = Native.pik_music_update(music, menuSong, 0, gain, 0) != 0;
                if (ownsTrack && lastMenuSong != menuSong)
                {
                    Native.pik_music_menu_sound(music, menu ? 0u : 2u);
                    lastMenuSong = menuSong;
                }
                if (!ownsTrack) { Stop(); failed = true; Plugin.Log.LogWarning("Pikmin menu music failed; restoring RoR2 music."); }
                return;
            }
            lastMenuSong = 0;
            bool local = LocalUserManager.readOnlyLocalUsersList.Any(u => u.currentNetworkUser && u.currentNetworkUser.isParticipating);
            bool eligible = ShipRepairMode.Eligible(SceneCatalog.GetSceneDefForCurrentScene());
            bool terminal = ShipRepairSession.Instance && ShipRepairSession.Instance.Terminal;
            var teleporter = TeleporterInteraction.instance;
            bool boss = teleporter && !teleporter.isIdle && !teleporter.isCharged;
            bool explore = ShipRepairMusicPolicy.Explore(Plugin.StageMusic.Value && local && !RunAudio.Ended,
                current && ShipRepairMode.Active && Plugin.AssetsReady, eligible, terminal, boss);
            if (!explore)
            {
                // Victory's existing PCM owns the soundtrack until results.
                ownsTrack = music != IntPtr.Zero && Plugin.StageMusic.Value && ShipRepairMode.Active &&
                    ShipRepairSession.Instance && ShipRepairSession.Instance.Winning;
                if (music != IntPtr.Zero) Native.pik_music_update(music, boss ? song : 0, 0, 0f, boss || Time.timeScale == 0f ? (byte)1 : (byte)0);
                if (!current || !ShipRepairMode.Active || RunAudio.Ended || !Plugin.StageMusic.Value) Stop();
                return;
            }
            if (failed) return;
            if (music == IntPtr.Zero)
            {
                music = Native.pik_music_new(Native.CString(Plugin.AssetsDir));
                if (music == IntPtr.Zero) { failed = true; Plugin.Log.LogWarning("Pikmin stage music could not start; retaining RoR2 music."); return; }
            }
            if (stage != current.stageClearCount)
            {
                song = ShipRepairMusicPolicy.MixedTrack(current.seed, current.stageClearCount);
                stage = current.stageClearCount;
                Plugin.Log.LogInfo($"Ship Repair: stage {stage + 1}, disc BGM {song}.");
            }
            if (Time.unscaledTime >= scanAt)
            {
                scanAt = Time.unscaledTime + 0.25f;
                var user = LocalUserManager.GetFirstLocalUser();
                var body = user?.cachedBody;
                if (body && CharacterBody.readOnlyInstancesList.Any(enemy => enemy.teamComponent && enemy.teamComponent.teamIndex == TeamIndex.Monster &&
                    enemy.healthComponent && enemy.healthComponent.alive && Vector3.Distance(enemy.corePosition, body.corePosition) < 30f)) enemyUntil = Time.time + 2f;
            }
            bool nearby = Plugin.AdaptiveStageMusic.Value && Time.time < enemyUntil;
            float volume = GameSettings.MusicVolume() * Mathf.Clamp(Plugin.StageMusicVolume.Value, 0f, 2f);
            uint context=nearby ? 1u : 0u;
            var localBody=LocalUserManager.GetFirstLocalUser()?.cachedBody;
            if (localBody && Plugin.AdaptiveStageMusic.Value)
            {
                if (CharacterBody.readOnlyInstancesList.Any(e=>e.teamComponent && e.teamComponent.teamIndex==TeamIndex.Monster && e.healthComponent && e.healthComponent.alive && Vector3.Distance(e.corePosition,localBody.corePosition)<12f)) context|=2;
                var world=PikminWorld.Instance;
                if (world != null) for (int i=0;i<world.PikiCount;i++) {
                    var p=world.Pikis[i]; if (p.alive==0 || Vector3.Distance(UnityHost.ToUnity(p.pos),localBody.footPosition)>50f) continue;
                    context|=16;
                    if (p.state==Native.PikiState.Carry) context|=4;
                }
                if (world!=null) for (int i=0;i<world.PelletCount;i++) if (world.Pellets[i].has_ship_part!=0 && Vector3.Distance(UnityHost.ToUnity(world.Pellets[i].pos),localBody.footPosition)<30f) {context|=8;break;}
            }
            ownsTrack = Native.pik_music_update_context(music,song,context,volume,Time.timeScale==0f?(byte)1:(byte)0,Time.unscaledDeltaTime)!=0;
            if (!ownsTrack && song>=0x100) {
                song=ShipRepairMusicPolicy.Track(current.seed,current.stageClearCount);
                ownsTrack=Native.pik_music_update_context(music,song,context,volume,Time.timeScale==0f?(byte)1:(byte)0,Time.unscaledDeltaTime)!=0;
                Plugin.Log.LogInfo("Ship Repair: Pikmin 2 music unavailable; using Pikmin 1 stage music.");
            }
            if (!ownsTrack) { Stop(); failed = true; Plugin.Log.LogWarning("Pikmin stage music failed; restoring RoR2 music."); }
        }
    }
}
