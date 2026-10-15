using System;
using Ror2Common;
using UnityEngine;

namespace OlimarTest
{
    // Pikmin's own models from the user's disc, through libpikmin's view (pik_view_*): every
    // frame the view poses Olimar, each Pikmin and its leaf, Onions, pellets, sprouts and the
    // cursor, and ror2-common's ModelView puts them in Unity (each material's TEV baked by
    // libpikmin into one texture, on RoR2's HGStandard; see-through ones such as Olimar's helmet
    // on Cloud Remap). Particles (the whistle, dead Pikmin's ghosts) are drawn by Particles. The
    // same view plays the game's sounds for the sim's events (not its music).
    internal sealed class PikminModels : IDisposable
    {
        IntPtr view;
        readonly Particles particles;

        /// The meshes (ror2-common), for hosts that place the draws themselves.
        public ModelView Models { get; }
        /// What loaded ("15 models; sound 48000 Hz, 2 channels").
        public string Status { get; }

        /// This frame's instances (after Update).
        public int DrawCount => Models.DrawCount;
        public ViewDraw DrawAt(int i) => Models.DrawAt(i);

        PikminModels(IntPtr view, ModelView models, Transform parent)
        {
            this.view = view;
            Models = models;
            Status = Native.Utf8(Native.pik_view_status(view));
            particles = new Particles(view, parent);
        }

        /// Loads the models and, with `sound`, opens the sound output. Null (and logs why) if
        /// the models can't be loaded; the caller then draws stand-ins. `parent` null keeps the
        /// objects across scenes.
        public static PikminModels Create(string assetsDir, Transform parent, bool sound, string name = "Pikmin")
        {
            // The Dolphin's parts are drawn as the sim fits them: all 30 unless the ship-parts mode is on.
            Native.pik_view_set_repaired_ship(1);
            var v = Native.pik_view_new(Native.CString(assetsDir), (byte)(sound ? 1 : 0));
            if (v == IntPtr.Zero)
            {
                Plugin.Log.LogWarning($"Pikmin's models didn't load from {assetsDir}; drawing stand-ins");
                return null;
            }
            Native.pik_view_set_captain_pikmin_jump(Plugin.PikminJump.Value ? (byte)1 : (byte)0);
            if (sound) Native.pik_view_set_trumpet_gain(v, Plugin.TrumpetGain.Value);
            if (sound)
            {
                var bell = new float[640000];
                if (Native.pik_view_take_death_sting(v, bell, (uint)bell.Length) != 0)
                    SpawnMusic.SetDeathPcm(bell, 32000, () => Plugin.Sound.Value
                        ? GameSettings.SfxVolume() * Mathf.Clamp(Plugin.SoundVolume.Value, 0f, 2f) : 0f);
                else Plugin.Log.LogWarning("Sundown death bell could not be prepared from the disc audio.");
            }
            var models = ModelView.Create(new PikViewSource(v), name, parent);
            if (models == null)
            {
                Native.pik_view_free(v);
                return null;
            }
            return new PikminModels(v, models, parent);
        }

        /// Draw each captain's cursor and marker (on by default).
        /// Captain `captain` appears in a beam, `progress` 0..1; anything else ends it.
        public void Materialize(uint captain, float progress)
        {
            if (view != IntPtr.Zero) Native.pik_view_set_materialize(view, captain, progress);
        }

        public void ShowCursors(bool show)
        {
            if (view != IntPtr.Zero) Native.pik_view_show_cursors(view, (byte)(show ? 1 : 0));
        }

        /// Poses `sim` (and plays its sounds) without moving the objects; `frame` carries the
        /// camera and volume.
        public void Pose(IntPtr sim, ref Native.ViewFrame frame)
        {
            if (view != IntPtr.Zero) Native.pik_view_update(view, sim, ref frame);
        }

        /// Poses `sim` and moves the objects and particles. `scale` is metres per sim unit; draws
        /// `hide` says no to (pellets standing in for a corpse the host carries itself) aren't
        /// shown.
        public void Update(IntPtr sim, ref Native.ViewFrame frame, float scale, Func<ViewDraw, bool> hide = null)
        {
            if (view == IntPtr.Zero) return;
            Pose(sim, ref frame);
            Models.Update(scale, null, hide);
            particles.Draw(scale);
        }

        public void Dispose()
        {
            Models.Dispose();
            particles.Dispose();
            if (view != IntPtr.Zero)
            {
                Native.pik_view_free(view);
                view = IntPtr.Zero;
            }
        }
    }
}
