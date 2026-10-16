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
        float enemyUntil, scanAt;
        void Awake() => MusicController.pickTrackHook += PickTrack;
        void OnDestroy() { MusicController.pickTrackHook -= PickTrack; Stop(); }
        void Stop()
        {
            ownsTrack = false;
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
                song = ShipRepairMusicPolicy.Track(current.seed, current.stageClearCount);
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
            ownsTrack = Native.pik_music_update(music, song, nearby ? (byte)1 : (byte)0, volume, Time.timeScale == 0f ? (byte)1 : (byte)0) != 0;
            if (!ownsTrack) { Stop(); failed = true; Plugin.Log.LogWarning("Pikmin stage music failed; restoring RoR2 music."); }
        }
    }
}
