using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using EntityStates;
using Ror2Common;
using RoR2;
using RoR2.ContentManagement;
using UnityEngine;
using Path = System.IO.Path;

namespace OlimarTest
{
    public enum CameraMode { Vanilla, Orbit }
    public enum ControlScheme
    {
        /// Pikmin with VRMod running in VR, Aim otherwise.
        Auto,
        /// The sim moves Olimar; GameCube-style pad buttons, a Pikmin-style camera.
        Pikmin,
        /// RoR2 moves the body; the Pikmin cursor is wherever you aim (made for VR).
        Aim,
    }

    // A test survivor for libpikmin in Risk of Rain 2: Commando's body as Olimar, with a squad
    // of Pikmin that follow, get thrown onto enemies, attack them through RoR2's damage, and
    // carry corpses home to an Onion for more Pikmin. No R2API: content goes in through RoR2's
    // own content-pack hook (ror2-common's ContentProvider).
    [BepInPlugin(Guid, "Olimar", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.libpikmin.olimartest";
        const string VrModGuid = "com.DrBibop.VRMod";

        internal static ManualLogSource Log;
        internal static string AssetsDir;
        static volatile bool assetsBusy;

        internal static ControlScheme Scheme;
        internal static ConfigEntry<CameraMode> Camera;
        internal static ConfigEntry<int> StartRed, StartYellow, StartBlue;
        internal static ConfigEntry<bool> OnionItems, AutoRejoin;
        internal static ConfigEntry<float> DamageCoefficient, ProcCoefficient, PikminHealth, PikminHurtRadius;
        internal static ConfigEntry<float> OlimarHealth, OlimarHealthPerLevel;
        internal static ConfigEntry<float> LookUpDegrees;
        internal static ConfigEntry<float> OlimarHeight, MetresPerUnit, ThrowReach, ShakeProtection, LockRange;
        internal static ConfigEntry<int> MaxPellets;
        internal static ConfigEntry<float> CarriersPerMetre;
        internal static ConfigEntry<bool> CarryBodies;
        internal static ConfigEntry<bool> LogEvents;
        // Items (OlimarItems): the shared minion scaling policy and the timers items shorten.
        internal static ConfigEntry<float> ProcInterval, MinionMoveScale, MinionHealScale, ItemEffectScale,
            CooldownExponent, RelocationRecharge, AutoPluckDelay, EmergencySeconds, AutoCarryMetres,
            StompPikminDamage, RazorwireInterval;
        // Growth: planted sprouts ripen on a timer, Pikmin grow on kills they fought.
        internal static ConfigEntry<float> SecondsPerStage, FlowerSeconds;
        // The dismiss strike and Pikmin pace (see PikminWorld.StartSim).
        internal static ConfigEntry<float> StrikeDamage, StrikeReachMetres, StrikeCooldown, PaceLeaf, PaceBud, PaceFlower;
        internal static ConfigEntry<bool> CursorHoming, PikminJump, ShipParts;
        internal static ConfigEntry<float> PunchDamage, StrikeChargeSeconds;
        internal static ConfigEntry<bool> StrikeStun, StrikeKillReset;
        internal static ConfigEntry<int> KillsPerStage;
        internal static ConfigEntry<int> EmergencyCount, BackupSeedsPerStack, LarvaSquad, StompPikminCap;
        internal static ConfigEntry<bool> AttackSpeedFromItems, GooboSquad, GummyClones, FallDamageEnabled;
        internal static ConfigEntry<bool> Sound;
        internal static ConfigEntry<float> SoundVolume, TrumpetGain;

        /// Extraction has finished (or wasn't needed). The sim waits for this so it starts on
        /// the disc's tuning.
        internal static bool AssetsSettled => !assetsBusy;

        void Awake()
        {
            Log = Logger;
            CommonLog.Log = Logger;
            OnionCard.Install(gameObject);
            string here = Path.GetDirectoryName(Info.Location);
            SpawnMusic.Initialize(Config, here);
            RunAudio.Initialize(() => { if (PikminWorld.Instance) PikminWorld.Instance.StopPresentation(); });
            Camera = Config.Bind("Controls", "CameraMode", CameraMode.Orbit,
                "Orbit: right stick pans, LB + right stick swarms, LT recentres, RT locks, RB cycles targets (or zoom when unlocked). Vanilla uses the fixed camera. Both use B to jump and LB + B to whistle.");

            var assets = Config.Bind("Paths", "AssetsDir", "",
                "libpikmin's assets/ folder (extracted from your own Pikmin disc). Empty: read " +
                "assets_dir.txt next to the plugin, then $PIKMIN_ASSETS, then assets/ next to the plugin.");
            var disc = Config.Bind("Paths", "Disc", "",
                "Your Pikmin (GameCube) disc image (.iso/.gcm/.ciso), used once to build AssetsDir " +
                "if it's missing. Empty: look in the plugin folder and next to AssetsDir.");
            OlimarHeight = Config.Bind("Scale", "OlimarHeight", 0.667f,
                "Olimar's height as a share of a standard survivor's (Commando's). Pikmin, the " +
                "Onion and everything else from the sim scale with him. Try 0.75 too.");
            MetresPerUnit = Config.Bind("Scale", "MetresPerUnit", 0f,
                "Metres per Pikmin unit, overriding OlimarHeight when above 0. Olimar is 25 units " +
                "tall.");
            ThrowReach = Config.Bind("Controls", "ThrowReach", 2f,
                "How far the cursor reaches (so how far Olimar throws) as a multiple of Pikmin's " +
                "own reach, which is short for RoR2's fights.");
            LookUpDegrees = Config.Bind("Controls", "LookUpDegrees", 70f,
                new ConfigDescription("How far the Orbit camera can tilt up past level when you push the right stick " +
                    "down (RoR2's own camera looks up 70). 0 keeps Pikmin's look from above (never below 10 degrees down).",
                    new AcceptableValueRange<float>(0f, 85f)));
            LockRange = Config.Bind("Controls", "LockRange", 40f,
                new ConfigDescription("How far away, in metres, the lock-on can pick an enemy (with a clear line). " +
                    "Past the throw (about 24 m at the default ThrowReach) a thrown Pikmin falls short, so " +
                    "this reaches only a little beyond it: enough to lock a flyer, then close in.",
                    new AcceptableValueRange<float>(5f, 100f)));
            var scheme = Config.Bind("Controls", "Scheme", ControlScheme.Auto,
                "Pikmin: the sim moves Olimar, pad buttons by position (A throw/pluck, B jump, LB + B whistle, " +
                "X dismiss, LB blocks jumping, LT recentres). Keyboard: secondary jumps; hold jump + secondary to whistle. Aim: RoR2 moves the body " +
                "and the cursor is where you aim (primary throw, secondary whistle, utility " +
                "dismiss, special swarm). Auto: Aim in VR (VRMod), Pikmin otherwise.");
            StartRed = Config.Bind("Pikmin", "StartRed", 10, "Red Pikmin in the squad at the start of a run.");
            StartYellow = Config.Bind("Pikmin", "StartYellow", 5, "Yellow Pikmin at the start of a run.");
            StartBlue = Config.Bind("Pikmin", "StartBlue", 5, "Blue Pikmin at the start of a run.");
            AutoRejoin = Config.Bind("Pikmin", "AutoRejoin", true,
                "Pikmin that get separated (done carrying or fighting, thrown with nothing to " +
                "do, left behind) run back to Olimar by themselves. Dismissed Pikmin still wait. " +
                "Off: Pikmin 1, where they wait to be whistled.");
            StrikeDamage = Config.Bind("Combat", "StrikeDamage", 4f,
                "The dismiss button (RB, F on keyboard) is a melee strike that no longer dismisses the squad: this many " +
                "times Olimar's punch damage to every enemy around him. 4 is about 4.5 times his damage stat: two strikes kill a Beetle at the start " +
                "(80 health, 20 armor). 0: it dismisses the squad as in Pikmin 1.");
            PunchDamage = Config.Bind("Combat", "PunchDamage", 22.5f,
                "Olimar's punch, in Pikmin attack points (times Combat.DamageCoefficient and his damage stat). Pikmin 1's is 10, half " +
                "his damage stat; 22.5 is a quarter of the dismiss strike's 4x, about 1.1 times his damage stat. 0: the disc's. " +
                "The strike is a multiple of this.");
            StrikeChargeSeconds = Config.Bind("Combat", "StrikeChargeSeconds", 3f,
                "After a strike the squad swarms the way he struck (at the enemy he is locked onto, else the way he faces) for this many seconds, " +
                "counted from the strike. Moving the right stick swarms instead. 0: no charge.");
            StrikeStun = Config.Bind("Combat", "StrikeStun", true, "The strike staggers what it hits (RoR2's one-second stun; bosses resist).");
            StrikeKillReset = Config.Bind("Combat", "StrikeKillReset", true,
                "A kill with the strike gives its charge straight back. Off: the cooldown always runs in full.");
            StrikeReachMetres = Config.Bind("Combat", "StrikeReachMetres", 4f,
                "How far from an enemy's edge the strike reaches, all around Olimar.");
            StrikeCooldown = Config.Bind("Combat", "StrikeCooldown", 4f, "Seconds between strikes.");
            ShipParts = Config.Bind("Dolphin", "ShipPartsMode", false,
                "Not built yet: the ship-repair mode, where the Dolphin is wrecked and its parts are scattered over the stages. " +
                "Off (the only mode for now): the Dolphin is the repaired ship, with all 30 parts and its lights and effects.");
            PikminJump = Config.Bind("Pikmin", "OlimarUsesPikminJump", true,
                "Olimar jumps with the Pikmin's own jump animation (they share the game's motion table). Off: his geyser-ride pose, tipped upright.");
            CursorHoming = Config.Bind("Combat", "CursorHoming", true,
                "A thrown Pikmin is steered in flight onto the cursor, the way a lock-on steers it onto its enemy, so it lands where " +
                "the cursor was whatever Olimar's speed or the cursor's height. Off: Pikmin 1's plain arc, whose height follows how long the throw was held.");
            PaceFlower = Config.Bind("Pikmin", "PaceFlower", 1.2f,
                "A flower Pikmin's top speed as a multiple of Olimar's full-tilt speed (his items and sprint included), so " +
                "it keeps up and stays in reach for a throw. 0 for any of the three: the disc's speeds.");
            PaceBud = Config.Bind("Pikmin", "PaceBud", 0.97f, "The same for a bud Pikmin: a little slower than Olimar.");
            PaceLeaf = Config.Bind("Pikmin", "PaceLeaf", 0.93f, "The same for a leaf Pikmin.");
            SecondsPerStage = Config.Bind("Growth", "SecondsPerStage", 20f,
                "Seconds a planted sprout is a leaf, then a bud, before it is a flower (15 to 30 suggested). The " +
                "timer starts when the sprout can be plucked and starts over at each stage, so one left alone " +
                "for Items.AutoPluckDelay (60 s) is plucked as a flower. 0: the disc's pace.");
            FlowerSeconds = Config.Bind("Growth", "FlowerSeconds", 300f,
                "Seconds a planted flower stands before it wilts back to a leaf (5 minutes). Keep it well " +
                "longer than the gap between flowering and the auto-pluck, or the plucker finds a wilted sprout. " +
                "0: the disc's (a few seconds).");
            KillsPerStage = Config.Bind("Growth", "KillsPerStage", 2,
                "Kills it takes to raise a Pikmin one stage (leaf, bud, flower). Only the Pikmin that attacked " +
                "the enemy earn the kill, whoever lands the last blow. 0: off.");
            ShakeProtection = Config.Bind("Pikmin", "ShakeProtection", 2f,
                "Seconds a Pikmin holds on after latching onto an enemy. When the enemy attacks, " +
                "Pikmin latched for longer are shaken off and the rest stay on. 0: every attack " +
                "shakes off every Pikmin, as Pikmin 1's flick does.");
            OnionItems = Config.Bind("Pikmin", "OnionItems", true,
                "Each Onion is Olimar's minion and gets his items, so items that work where you " +
                "stand still (Bustling Fungus) work around it. It can't be hurt or targeted.");
            DamageCoefficient = Config.Bind("Combat", "DamageCoefficient", 0.05f,
                "RoR2 damage per point of Pikmin attack power, times Olimar's damage stat. A Red " +
                "hits for 15 points: 0.75x Olimar's damage per swing at the default.");
            ProcCoefficient = Config.Bind("Combat", "PikminProcCoefficient", 0.2f,
                "Proc coefficient of a Pikmin hit that's allowed to proc (items like Ukulele). " +
                "Each Pikmin procs at most once per Items.ProcInterval; its hits between land at 0.");
            PikminHealth = Config.Bind("Combat", "PikminHealth", 0.5f,
                "Each Pikmin's health as a share of Olimar's max health, so they scale with his level " +
                "and health items. At 0.5 a Lemurian's bite (12 at level 1) takes a fifth of a " +
                "Pikmin's health.");
            PikminHurtRadius = Config.Bind("Combat", "PikminHurtRadius", 1.5f,
                "Metres around an enemy hit's impact point in which Pikmin are hurt too.");
            OlimarHealth = Config.Bind("Combat", "OlimarHealth", 220f,
                "Olimar's base max health (Commando's is 110).");
            OlimarHealthPerLevel = Config.Bind("Combat", "OlimarHealthPerLevel", 66f,
                "Max health Olimar gains per level (Commando's is 33).");
            MaxPellets = Config.Bind("Corpses", "MaxPellets", 30, "Most enemy corpses on the ground at once.");
            CarryBodies = Config.Bind("Corpses", "CarryBodies", false,
                "Pikmin carry the enemy's own body instead of a numbered pellet. Experimental: the " +
                "pellet the Pikmin go for stays where the enemy died while the body may roll away.");
            CarriersPerMetre = Config.Bind("Corpses", "CarriersPerMetre", 2f,
                "How big a pellet an enemy leaves, per metre of its radius: the largest of 1, 5, 10 " +
                "and 20 not above radius x this (a Beetle leaves a 1 pellet). With " +
                "CarryBodies, the Pikmin needed to lift the body.");
            Sound = Config.Bind("Sound", "Enabled", true,
                "Play Pikmin's sound effects (from your disc) for whistles, throws, Pikmin and the " +
                "Onion. Arrival music has separate SpawnMusic settings; RoR2's soundtrack stays.");
            SoundVolume = Config.Bind("Sound", "Volume", 1f,
                "Pikmin sound volume, on top of RoR2's master and sound effects volume (0 to 2).");
            TrumpetGain = Config.Bind("Sound", "TrumpetGain", 1.4f,
                "How loud Olimar's swarming trumpet plays against the disc's level (1), so it carries over RoR2's " +
                "combat. 0 to 4; restart to apply.");
            ProcInterval = Config.Bind("Items", "ProcInterval", 0.25f,
                "Seconds between procs for each Pikmin (its own clock, checked when it hits). A tuning " +
                "choice: Pikmin 1 has no such rule.");
            AttackSpeedFromItems = Config.Bind("Items", "AttackSpeedFromItems", true,
                "Attack speed items (Soldier's Syringe) speed up his punches and plucks and his Pikmin's attacks.");
            MinionMoveScale = Config.Bind("Items", "MinionMoveScale", 0.75f,
                "Share of his move speed bonus his Pikmin get (Goat Hoof, Energy Drink). Not used while the Pikmin.Pace settings are on, which follow his speed exactly.");
            MinionHealScale = Config.Bind("Items", "MinionHealScale", 0.5f,
                "How strongly Pikmin take healing: his regeneration as a share of their health, and " +
                "healing wards they stand in (Bustling Fungus).");
            ItemEffectScale = Config.Bind("Items", "ItemEffectScale", 0.25f,
                "Damage of item effects a Pikmin sets off (Genesis Loop, Razorwire) against Olimar's own.");
            RazorwireInterval = Config.Bind("Items", "RazorwireInterval", 0.5f,
                "Razorwire fires for hurt Pikmin at most once per this many seconds.");
            CooldownExponent = Config.Bind("Items", "CooldownExponent", 1f,
                "How strongly cooldown items (Alien Head, Purity) speed up sprouting, seeds after feeding, " +
                "regrouping, the plucker and emergency Pikmin: times 1 / (cooldown ratio ^ this).");
            RelocationRecharge = Config.Bind("Items", "RelocationRecharge", 8f,
                "Seconds for an Onion relocation charge (utility) to come back. Lifting and landing each take one. " +
                "Hardlight Afterburner adds charges; restart to apply.");
            AutoPluckDelay = Config.Bind("Items", "AutoPluckDelay", 60f,
                "Seconds a sprout can stand ready before a Pikmin from the landed Onion comes out to " +
                "pluck every ready sprout (the special). Below 0: off. Restart to apply to the skill.");
            EmergencySeconds = Config.Bind("Items", "EmergencySeconds", 15f,
                "Seconds after Olimar has no Pikmin anywhere (field, Onion, sprouts) before the Onion sends more.");
            EmergencyCount = Config.Bind("Items", "EmergencyCount", 10, "How many emergency Pikmin come. 0: off.");
            AutoCarryMetres = Config.Bind("Items", "AutoCarryMetres", 3f,
                "Pikmin in his squad (not swarming) or standing about pick up a pellet within this many " +
                "metres of its edge and carry it home. 0: off.");
            BackupSeedsPerStack = Config.Bind("Items", "BackupSeedsPerStack", 1,
                "Backup Magazine: extra seeds per pellet or corpse delivered to his Onion, per stack.");
            LarvaSquad = Config.Bind("Items", "LarvaSquad", 20,
                "Pluripotent Larva brings Olimar back with this many Pikmin if he had none left anywhere.");
            StompPikminDamage = Config.Bind("Items", "StompPikminDamage", 1f,
                "H3AD-5T v2: damage per Pikmin (times his damage) of the one blast his squad's stomp makes.");
            StompPikminCap = Config.Bind("Items", "StompPikminCap", 25, "Most Pikmin counted in that blast.");
            FallDamageEnabled = Config.Bind("Items", "FallDamage", true,
                "Olimar takes RoR2's fall damage for a drop (his speed on landing as if falling at RoR2's gravity). " +
                "Fall-damage items, armour and difficulty modifiers apply as for any survivor; stomps are safe.");
            GooboSquad = Config.Bind("Items", "GooboSquad", true,
                "Goobo Jr.'s Olimar gets a temporary copy of his squad; its deliveries go to the real Onion.");
            GummyClones = Config.Bind("Look", "GummyClones", true,
                "Goobo Jr.'s Olimar is drawn in RoR2's gummy clone material.");
            LogEvents = Config.Bind("Debug", "LogEvents", true,
                "Log notable sim events (latches, deaths, deliveries, plucks) to the BepInEx log.");

            Scheme = scheme.Value;
            if (Scheme == ControlScheme.Auto)
                Scheme = InVr() ? ControlScheme.Aim : ControlScheme.Pikmin;

            if (!Native.Load(Path.Combine(here, "pikmin.dll"), out string error))
            {
                Log.LogError(error);
                return;
            }
            AssetsDir = ResolveAssets(assets.Value, here);
            Log.LogInfo($"libpikmin ABI {Native.pik_abi_version()}, {Native.pik_tick_hz()} Hz, " +
                        $"assets {AssetsDir}, controls {Scheme}");
            PrepareAssets(disc.Value, here);
            PikminDamageNumbers.Install(gameObject, Config);
            ItemHooks.Install();
            // With the Pikmin controls the HUD's skill icons name the pad buttons the sim reads (Pad),
            // not RoR2's binds; the Aim scheme (VR) keeps RoR2's.
            GlyphOverride.Install();
            GlyphOverride.Set("OlimarTestBody", () => Scheme == ControlScheme.Pikmin, new Dictionary<SkillSlot, PadButton[]>
            {
                [SkillSlot.Primary] = new[] { PadButton.A },
                [SkillSlot.Secondary] = new[] { PadButton.LB, PadButton.B },
                [SkillSlot.Utility] = new[] { PadButton.X },
                // The dismiss strike.
                [SkillSlot.Special] = new[] { PadButton.RB },
            });

            ContentProvider.Register(Guid, Content.Fill);
            Language.onCurrentLanguageChanged += AddTokens;
            PikminWorld.Hook();
        }

        static bool InVr()
        {
            if (!Chainloader.PluginInfos.ContainsKey(VrModGuid)) return false;
            try { return UnityEngine.XR.XRSettings.enabled; }
            catch { return true; }
        }

        static string ResolveAssets(string configured, string here)
        {
            if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
            string file = Path.Combine(here, "assets_dir.txt");
            if (File.Exists(file)) return File.ReadAllText(file).Trim();
            string env = Environment.GetEnvironmentVariable("PIKMIN_ASSETS");
            if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
            return Path.Combine(here, "assets");
        }

        // First run: build the assets folder from the user's disc on a worker thread (the sim
        // waits for it). Nothing from the disc ships with the plugin.
        static void PrepareAssets(string configuredDisc, string here)
        {
            if (Native.pik_assets_complete(Native.CString(AssetsDir)) != 0)
                return;
            string disc = configuredDisc?.Trim();
            if (string.IsNullOrEmpty(disc))
            {
                string parent = Path.GetDirectoryName(Path.GetFullPath(AssetsDir)) ?? here;
                var found = new byte[1024];
                if (Native.pik_find_disc(Native.CString(here + ";" + parent), found, (uint)found.Length) != 0)
                    disc = Native.Utf8(found);
            }
            if (string.IsNullOrEmpty(disc) || !File.Exists(disc))
            {
                Log.LogError($"No Pikmin assets at '{AssetsDir}' and no disc image to build them from. " +
                             $"Set Paths.Disc in BepInEx/config/{Guid}.cfg to your own Pikmin disc. " +
                             "Until then the sim runs on code defaults, which differ from the game's tuning.");
                return;
            }
            assetsBusy = true;
            Log.LogInfo($"Extracting Pikmin assets from {disc} into {AssetsDir}...");
            var worker = new Thread(() =>
            {
                try
                {
                    var msg = new byte[2048];
                    byte ok = Native.pik_extract(Native.CString(disc), Native.CString(AssetsDir), msg, (uint)msg.Length);
                    if (ok != 0) Log.LogInfo($"Extracted: {Native.Utf8(msg)}");
                    else Log.LogError($"Extraction failed: {Native.Utf8(msg)}");
                }
                catch (Exception e)
                {
                    Log.LogError($"Extraction failed: {e}");
                }
                finally
                {
                    assetsBusy = false;
                }
            }) { IsBackground = true, Name = "pikmin extract" };
            worker.Start();
        }

        static void AddTokens()
        {
            var lang = Language.currentLanguage;
            if (lang == null) return;
            lang.SetStringByToken("OLIMARTEST_BODY_NAME", "Olimar");
            lang.SetStringByToken("OLIMARTEST_BODY_SUBTITLE", "Captain of the S.S. Dolphin");
            lang.SetStringByToken("OLIMARTEST_BODY_DESCRIPTION",
                "Commando's body leading a squad of Pikmin from libpikmin.\n\n" +
                "< ! > Throw Pikmin onto enemies; they latch on and fight.\n" +
                "< ! > Whistle to call Pikmin back; corpses are carried to the Onion for more.\n" +
                "< ! > Pad: A throw or pluck, B jump, LB + B whistle (LB blocks jumping), X raise or lower the Onion (or interact: under the Onion it opens its menu, as does d-pad up), RB strike (the dismiss attack), " +
                "d-pad left/right pick the colour to throw (Any is the nearest), hold RT to lock onto an enemy (RB then cycles), " +
                "right stick pans the camera (LB + right stick swarms; click it to zoom), LT recentres.\n" +
                "< ! > Keyboard (Pikmin controls): primary throw, secondary jump, hold jump + secondary whistle, utility Onion, F strike.\n" +
                "< ! > Hopoo Feather: jump again in mid-air, and the squad does too. H3AD-5T v2: jump in mid-air with no jumps left to stomp.\n" +
                "< ! > The utility's charges lift and land the Onion. Passive: sprouts left a minute are plucked by a Pikmin from the landed Onion.");
            lang.SetStringByToken("OLIMARTEST_ONION_NAME", "Onion");
            lang.SetStringByToken("OLIMARTEST_BODY_OUTRO", "..and so he left, with a ship full of seeds.");
            lang.SetStringByToken("OLIMARTEST_BODY_FAIL", "..and so he vanished, his Pikmin waiting by the Onion.");
            OlimarSkills.Tokens(lang);
        }
    }

    // The survivor: Commando's body and display, cloned under Olimar's own names, the display
    // drawing Olimar himself (OlimarDisplay) once the disc's files are there.
    internal static class Content
    {
        // Sets the sim's scale from Olimar's height against Commando's, and shrinks the body
        // (its collision capsule, and the hidden model that carries its hurtboxes, core and
        // camera pivot) to match.
        static void Size(GameObject body)
        {
            var cap = body.GetComponent<CapsuleCollider>();
            float survivor = cap ? cap.height * body.transform.lossyScale.y : 1.82f;
            UnityHost.Scale = Plugin.MetresPerUnit.Value > 0f
                ? Plugin.MetresPerUnit.Value
                : Mathf.Clamp(Plugin.OlimarHeight.Value, 0.2f, 2f) * survivor / UnityHost.NaviHeight;
            float tall = UnityHost.NaviHeight * UnityHost.Scale;
            float r = tall / survivor;
            Plugin.Log.LogInfo($"Olimar is {tall:F2} m tall, {r:P0} of Commando's {survivor:F2} m " +
                               $"({UnityHost.Scale:F4} m per unit)");
            if (cap)
            {
                var motor = body.GetComponent<KinematicCharacterController.KinematicCharacterMotor>();
                bool done = false;
                if (motor)
                {
                    try
                    {
                        motor.SetCapsuleDimensions(cap.radius * r, cap.height * r, cap.center.y * r);
                        done = true;
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning($"resizing Olimar's capsule through his motor: {e.Message}");
                    }
                }
                if (!done)
                {
                    cap.radius *= r;
                    cap.height *= r;
                    cap.center *= r;
                }
            }
            var ml = body.GetComponent<ModelLocator>();
            var model = ml ? (ml.modelBaseTransform ? ml.modelBaseTransform : ml.modelTransform) : null;
            if (model)
            {
                model.localPosition *= r;
                model.localScale *= r;
            }
        }

        internal static Minions.Kind OnionMinion;

        // An Onion doesn't fight: passive items only, at full strength. Nothing that attacks
        // (Tesla Coil, Mired Urn), nothing tagged as damage or on-kill (it never hits or kills).
        static bool OnionItem(ItemIndex i)
        {
            if (i == RoR2Content.Items.ShockNearby.itemIndex || i == RoR2Content.Items.SiphonOnLowHealth.itemIndex)
                return false;
            var def = ItemCatalog.GetItemDef(i);
            return !def || (!def.ContainsTag(ItemTag.Damage) && !def.ContainsTag(ItemTag.OnKillEffect));
        }

        public static void Fill(ContentPack pack)
        {
            var commando = LegacyResourcesAPI.Load<GameObject>("Prefabs/CharacterBodies/CommandoBody");
            var commandoDisplay = LegacyResourcesAPI.Load<GameObject>("Prefabs/CharacterDisplays/CommandoDisplay");
            if (!commando || !commandoDisplay)
            {
                Plugin.Log.LogError($"Commando not found (body {(bool)commando}, display {(bool)commandoDisplay})");
                return;
            }

            var body = Prefabs.Clone(commando, "OlimarTestBody", Plugin.Guid);
            Size(body);
            var cb = body.GetComponent<CharacterBody>();
            cb.baseNameToken = "OLIMARTEST_BODY_NAME";
            cb.subtitleNameToken = "OLIMARTEST_BODY_SUBTITLE";
            cb.bodyColor = new Color(0.95f, 0.75f, 0.2f);
            cb.preferredPodPrefab = null;
            cb.preferredInitialStateType = new SerializableEntityStateType(typeof(DolphinArrival));
            cb.baseMaxHealth = Plugin.OlimarHealth.Value;
            cb.levelMaxHealth = Plugin.OlimarHealthPerLevel.Value;
            foreach (var esm in body.GetComponents<EntityStateMachine>())
                if (esm.customName == "Body")
                {
                    esm.mainStateType = new SerializableEntityStateType(typeof(OlimarMain));
                    esm.initialStateType = new SerializableEntityStateType(typeof(DolphinArrival));
                }
            // Aim/VR retains RoR2's motor and its motor-driven item states.
            if (Plugin.Scheme == ControlScheme.Pikmin) body.AddComponent<NativeBody>();
            body.AddComponent<OlimarDriver>();
            // His actions in RoR2's skill slots, for the items that change skills.
            OlimarSkills.Add(body, pack);
            // An AI master, so Goobo Jr. (and the like) can make an Olimar.
            var ai = SurvivorAi.Register(pack, body, "Prefabs/CharacterMasters/CommandoMonsterMaster", "OlimarTestMonsterMaster", Plugin.Guid);
            // It only walks him; OlimarBrain works his controls.
            if (ai) SurvivorAi.Drivers(ai, OlimarBrain.HoldRange);

            // The Onion as Olimar's minion, for his items (ror2-common's Minions).
            OnionMinion = Minions.Define(pack, Plugin.Guid, new MinionSpec
            {
                name = "OlimarTestOnion",
                nameToken = "OLIMARTEST_ONION_NAME",
                targetable = false,
                invincible = true,
                itemFilter = OnionItem,
            });

            var display = Prefabs.Clone(commandoDisplay, "OlimarTestDisplay", Plugin.Guid);
            DisplayModel.Attach(display, Plugin.Guid, OlimarDisplay.Get);
            // His portrait, from the same lobby Olimar without the Pikmin beside him.
            Portrait.Request(body, OlimarDisplay.Get, d => d.kind != Native.KindCaptain);

            var survivor = ScriptableObject.CreateInstance<SurvivorDef>();
            survivor.cachedName = "OlimarTest";
            survivor.bodyPrefab = body;
            survivor.displayPrefab = display;
            survivor.displayNameToken = "OLIMARTEST_BODY_NAME";
            survivor.descriptionToken = "OLIMARTEST_BODY_DESCRIPTION";
            survivor.outroFlavorToken = "OLIMARTEST_BODY_OUTRO";
            survivor.mainEndingEscapeFailureFlavorToken = "OLIMARTEST_BODY_FAIL";
            survivor.primaryColor = cb.bodyColor;
            survivor.desiredSortPosition = 101f;

            pack.bodyPrefabs.Add(new[] { body });
            pack.survivorDefs.Add(new[] { survivor });
            pack.entityStateTypes.Add(new[] { typeof(OlimarMain), typeof(DolphinArrival) });
            Plugin.Log.LogInfo("Olimar registered");
        }
    }

    // The body's main state. Skills never fire: Olimar fights with Pikmin, and the skill
    // buttons are his controls in the Aim scheme. When the sim moves the body (Pikmin scheme)
    // RoR2's own walking and jumping are off too.
    public class OlimarMain : GenericCharacterMain
    {
        OlimarDriver driver;

        public override void OnEnter()
        {
            base.OnEnter();
            driver = GetComponent<OlimarDriver>();
        }

        protected override bool CanExecuteSkill(GenericSkill skillSlot) => false;

        public override void HandleMovements()
        {
            if (driver && driver.SimMoves) return;
            base.HandleMovements();
        }

        public override void ProcessJump()
        {
            if (driver && driver.SimMoves) return;
            base.ProcessJump();
        }
    }
}
