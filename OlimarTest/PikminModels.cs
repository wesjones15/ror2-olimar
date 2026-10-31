using System;
using System.Collections.Generic;
using RoR2;
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
        bool hasPianta, hasLouie, hasPikachu;
        float piantaUnits = -1f, louieRatio = -1f, pikachuRatio = -1f, climberRatio = -1f;
        static bool victoryLoaded;
        readonly Particles particles;

        /// The meshes (ror2-common), for hosts that place the draws themselves.
        public ModelView Models { get; }
        /// What loaded ("15 models; sound 48000 Hz, 2 channels").
        public string Status { get; }

        /// This frame's instances (after Update).
        public int DrawCount => Models.DrawCount;
        public ViewDraw DrawAt(int i) => Models.DrawAt(i);
        public void Celebrate(bool enabled)
        {
            Native.pik_view_set_ship_repair_mode(view, ShipRepairMode.Active ? (byte)1 : (byte)0);
            Native.pik_view_set_celebrating(view, enabled ? (byte)1 : (byte)0);
        }
        public uint RepairsPending => Native.pik_view_repairs_pending(view);

        PikminModels(IntPtr view, ModelView models, Transform parent)
        {
            this.view = view;
            Models = models;
            models.NativePoseBudget = true;
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
            // Ship Repair's enemies and Classic's Olimar-only Pellet Posies share this renderer.
            TekiNative.pik_view_set_teki_models((byte)(TekiBodies.Enabled.Value && (ShipRepairMode.Active || TekiBodies.ClassicPosiesPerOlimar.Value > 0 || TekiBodies.FlintOtherChance.Value > 0f) ? 1 : 0));
            var v = Native.pik_view_new(Native.CString(assetsDir), (byte)(sound ? 1 : 0));
            TekiNative.pik_view_set_teki_models(0);
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
            // The Pianta skin joins the model table before the meshes are built from it.
            bool pianta = OlimarSkin.LoadInto(v);
            bool louie = OlimarSkin.LoadLouieInto(v);
            bool pikachu = OlimarSkin.LoadPikachuInto(v);
            OlimarSkin.LoadPikmin2Into(v);
            if (ShipRepairMode.Active) ShipRepairSession.LoadTreasuresInto(v);
            PikminWorld.LoadBulbsInto(v);
            var models = ModelView.Create(new PikViewSource(v), name, parent);
            if (models == null)
            {
                Native.pik_view_free(v);
                return null;
            }
            if (!victoryLoaded)
            {
                uint n = Native.pik_victory_pcm(Native.CString(assetsDir), null, 0, out uint rate);
                if (n > 0)
                {
                    var pcm = new float[n];
                    if (Native.pik_victory_pcm(Native.CString(assetsDir), pcm, n, out rate) == n)
                    {
                        SpawnMusic.SetVictoryPcm(pcm, (int)rate);
                        victoryLoaded = true;
                    }
                }
            }
            return new PikminModels(v, models, parent) { hasPianta = pianta, hasLouie = louie, hasPikachu = pikachu };
        }

        /// Who wears which body this frame: a captain's skin from OlimarSkin (0 Olimar, 2 the Pianta). Ignored when
        /// the Pianta did not load.
        public void SetCaptainSkin(uint captain, uint skin)
        {
            if (view != IntPtr.Zero && (hasPianta || hasLouie || hasPikachu)) Native.pik_view_set_captain_skin(view, captain, (byte)skin);
        }

        /// The ship of Onion `onion` is Louie's Hocotate Ship (`on`) or the Dolphin. Louie's files load with him.
        public void SetShipHocotate(uint onion, bool on)
        {
            if (view != IntPtr.Zero && hasLouie) Native.pik_view_set_ship_hocotate(view, onion, (byte)(on ? 1 : 0));
        }

        /// Plays EffectMgr's effect `id` once at `at` (sim units): an enemy's death smoke or soul.
        public void SpawnEffect(int id, Native.Vec3 at)
        {
            if (view != IntPtr.Zero) TekiNative.pik_view_spawn_effect(view, (uint)id, ref at);
        }

        /// As SpawnEffect, emitting along `dir` (a flame, a gust of wind).
        public void SpawnEffect(int id, Native.Vec3 at, Native.Vec3 dir)
        {
            if (view != IntPtr.Zero) TekiNative.pik_view_spawn_effect_dir(view, (uint)id, ref at, ref dir);
        }

        public void FollowEffect(uint key,int id,Native.Vec3 at,Native.Vec3 dir,float scale,bool on)
        {
            if(view!=IntPtr.Zero) TekiNative.pik_view_follow_effect(view,key,(uint)id,ref at,ref dir,scale,on?(byte)1:(byte)0);
        }
        /// A sound effect the game's tables know by name (SE_*), from `at` (sim units).
        public void PlayNamed(string name, Native.Vec3 at)
        {
            if (view != IntPtr.Zero) TekiNative.pik_view_teki_named_sound(view, Native.CString(name), ref at);
        }

        /// The enemies to draw at the next Update (the host's, see TekiBody).
        public void SetTekis(TekiNative.Instance[] list, int count)
        {
            if (view != IntPtr.Zero) TekiNative.pik_view_set_tekis(view, list, (uint)count);
        }

        /// The Long Legs' legs for those enemies (by the draw instance's id), which the view folds onto their feet.
        public void SetTekiLegs(TekiAiNative.Legs[] list, int count)
        {
            if (view != IntPtr.Zero) TekiNative.pik_view_set_teki_legs(view, list, (uint)count);
        }

        /// Draw each captain's cursor and marker (on by default).
        /// Captain `captain` appears in a beam, `progress` 0..1; anything else ends it.
        public void Materialize(uint captain, float progress)
        {
            if (view != IntPtr.Zero) Native.pik_view_set_materialize(view, captain, progress);
        }

        public void Heal(uint captain, float progress, uint ghosts)
        {
            if (view != IntPtr.Zero) Native.pik_view_set_healing(view, captain, progress, ghosts);
        }

        public void SetOnionGround(uint onion, float height)
        {
            if (view != IntPtr.Zero) Native.pik_view_set_onion_ground(view, onion, height);
        }

        public void SetOnionMenuBeam(uint onion, bool active)
        {
            if (view != IntPtr.Zero) Native.pik_view_set_onion_menu_beam(view, onion, (byte)(active ? 1 : 0));
        }

        public void PlaySound(byte sound, Native.Vec3 at)
        {
            if (view != IntPtr.Zero) Native.pik_view_play_sound(view, sound, at);
        }

        public void ShowCursors(bool show)
        {
            if (view != IntPtr.Zero) Native.pik_view_show_cursors(view, (byte)(show ? 1 : 0));
        }

        /// Poses `sim` (and plays its sounds) without moving the objects; `frame` carries the
        /// camera and volume.
        public void Pose(IntPtr sim, ref Native.ViewFrame frame)
        {
            if (view == IntPtr.Zero) return;
            // RoR2's pause menu stops the Pikmin's sounds where they are.
            Native.pik_view_set_paused(view, (byte)(PauseManager.isPaused ? 1 : 0));
            if (hasPianta)
            {
                float units = OlimarSkin.PiantaUnits();
                if (units != piantaUnits) { piantaUnits = units; Native.pik_view_set_pianta_height(view, units); }
            }
            if (hasPikachu)
            {
                float captain = Mathf.Max(0.2f, Plugin.PikachuHeight.Value), climber = Mathf.Max(0.1f, Plugin.IceClimberHeight.Value);
                if (captain != pikachuRatio || climber != climberRatio)
                {
                    pikachuRatio = captain;
                    climberRatio = climber;
                    Native.pik_view_set_pikachu_ratios(view, captain, climber);
                }
            }
            if (hasLouie)
            {
                float ratio = Mathf.Max(0.2f, Plugin.LouieHeight.Value);
                if (ratio != louieRatio) { louieRatio = ratio; Native.pik_view_set_louie_height(view, ratio); }
            }
            Native.pik_view_update(view, sim, ref frame);
        }

        /// Poses `sim` and moves the objects and particles. `scale` is metres per sim unit; draws
        /// `hide` says no to (pellets standing in for a corpse the host carries itself) aren't
        /// shown.
        public void Update(IntPtr sim, ref Native.ViewFrame frame, float scale, Func<ViewDraw, bool> hide = null)
        {
            if (view == IntPtr.Zero) return;
            Native.pik_view_set_pose_budget(view, ref frame.eye, scale > 0f ? Models.PoseBudgetDistance / scale : 0f);
            Pose(sim, ref frame);
            Models.Update(scale, null, hide);
            particles.Draw(scale);
            LogNewPellets();
        }

        readonly HashSet<ulong> loggedPellets = new HashSet<ulong>();

        /// Once per pellet drawn: the texture the view named for it, that texture's mean colour as the view
        /// baked it, and what the instance on screen is made of (the colour of a pellet that looks wrong).
        void LogNewPellets()
        {
            for (int i = 0; i < Models.DrawCount; i++)
            {
                var d = Models.DrawAt(i);
                if (d.kind != Native.KindPellet || !loggedPellets.Add(d.key)) continue;
                var ids = Native.pik_view_texture_ids(view, out uint n);
                var text = new System.Text.StringBuilder();
                for (int b = 0; ids != IntPtr.Zero && d.first_color + b < n && b < 2; b++)
                {
                    int id = System.Runtime.InteropServices.Marshal.ReadInt32(ids, (int)(d.first_color + b) * 4);
                    text.Append($" batch {b} texture {id}");
                    if (id < 0 || Native.pik_view_texture(view, (uint)id, out var info, null, 0) == 0) continue;
                    var px = new byte[info.width * info.height * 4];
                    if (Native.pik_view_texture(view, (uint)id, out info, px, (uint)px.Length) == 0) continue;
                    long r = 0, g = 0, bl = 0, c = 0;
                    for (int p = 0; p + 3 < px.Length; p += 4)
                        if (px[p + 3] > 0) { r += px[p]; g += px[p + 1]; bl += px[p + 2]; c++; }
                    if (c > 0) text.Append($" (mean colour {r / c},{g / c},{bl / c})");
                }
                Native.pik_view_model(view, d.model, out var modelInfo);
                Plugin.Log.LogInfo($"Pellet {d.id} drawn: model {d.model} ({modelInfo.texture_count} textures baked in all);{text}; on screen: {Models.DescribeInstance(d.key)}");
            }
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
