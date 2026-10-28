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
    [BepInPlugin(Guid, "Olimar", "0.4.2")]
    // Use the pack's fixes first when installed; no pack APIs are required by this plugin.
    [BepInDependency("___riskofthunder.RoR2BepInExPack", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.libpikmin.olimartest";
        const string VrModGuid = "com.DrBibop.VRMod";

        internal static ManualLogSource Log;
        internal static string AssetsDir;
        static volatile bool assetsBusy;
        bool musicInitialized;

        internal static ControlScheme Scheme;
        internal static ConfigEntry<CameraMode> Camera;
        internal static ConfigEntry<int> StartRed, StartYellow, StartBlue, StartPurple, StartWhite;
        internal static ConfigEntry<float> PoundGroundSeconds, WhitePoisonDamage;
        internal static ConfigEntry<bool> OnionItems, AutoRejoin;
        internal static ConfigEntry<float> OnionHealth, OnionReviveSeconds, OnionPelletHeal;
        internal static ConfigEntry<float> DamageCoefficient, ProcCoefficient, PikminHealth, PikminHurtRadius;
        internal static ConfigEntry<float> OlimarHealth, OlimarHealthPerLevel, StickDeadzone;
        internal static ConfigEntry<float> LookUpDegrees;
        internal static ConfigEntry<float> OlimarHeight, MetresPerUnit, ThrowReach, ShakeProtection, LockRange;
        internal static ConfigEntry<int> MaxPellets;
        internal static ConfigEntry<bool> OnlyOwnKills;
        internal static ConfigEntry<float> CarriersPerMetre;
        internal static ConfigEntry<bool> CarryBodies;
        internal static ConfigEntry<bool> LogEvents, LogLatches;
        internal static ConfigEntry<float> SnapshotHz, LatchLead;
        internal static ConfigEntry<bool> ReliableSnapshots, LogNet, PerformanceLog;
        // Items (OlimarItems): the shared minion scaling policy and the timers items shorten.
        internal static ConfigEntry<float> ProcInterval, MinionMoveScale, MinionHealScale, ItemEffectScale,
            CooldownExponent, RelocationRecharge, AutoPluckDelay, EmergencySeconds, AutoCarryMetres,
            StompPikminDamage, RazorwireInterval;
        // Growth: planted sprouts ripen on a timer, Pikmin grow on kills they fought.
        internal static ConfigEntry<float> SecondsPerStage, FlowerSeconds;
        // The dismiss strike and Pikmin pace (see PikminWorld.StartSim).
        internal static ConfigEntry<float> StrikeDamage, StrikeReachMetres, StrikeCooldown, PaceLeaf, PaceBud, PaceFlower;
        internal static ConfigEntry<bool> CursorHoming, PikminJump, ShipParts, CompetitiveMode;
        internal static ConfigEntry<float> EnemyPartIntervalSeconds, TreasureScoreWeight;
        internal static ConfigEntry<int> TreasuresPerStage, TreasureMaxCarriers;
        internal static ConfigEntry<bool> StageMusic, AdaptiveStageMusic;
        internal static ConfigEntry<float> StageMusicVolume;
        internal static ConfigEntry<float> PunchDamage, StrikeChargeSeconds;
        internal static ConfigEntry<bool> StrikeStun, StrikeKillReset;
        internal static ConfigEntry<int> KillsPerStage;
        internal static ConfigEntry<int> EmergencyCount, BackupSeedsPerStack, LarvaSquad, StompPikminCap;
        internal static ConfigEntry<bool> AttackSpeedFromItems, GooboSquad, GummyClones, FallDamageEnabled;
        internal static ConfigEntry<bool> Sound;
        internal static ConfigEntry<bool> PiantaUnlocked;
        internal static ConfigEntry<float> PiantaHeight, LouieHeight, PikachuHeight, IceClimberHeight;
        internal static ConfigEntry<float> SoundVolume, TrumpetGain;

        /// Extraction has finished (or wasn't needed). The sim waits for this so it starts on
        /// the disc's tuning.
        internal static bool AssetsSettled => !assetsBusy;

        static int assetsState;

        /// The assets folder is complete (so Pikmin's models, sounds and tuning can load), checked once
        /// extraction has settled.
        internal static bool AssetsReady
        {
            get
            {
                if (!AssetsSettled) return false;
                if (assetsState == 0)
                    assetsState = Native.pik_assets_complete(Native.CString(AssetsDir)) != 0 ? 1 : 2;
                return assetsState == 1;
            }
        }

        void Awake()
        {
            Log = Logger;
            CommonLog.Log = Logger;
            OnionMenuEquipment.Install();
            string here = Path.GetDirectoryName(Info.Location);
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
            StartPurple = Config.Bind("Pikmin", "StartPurple", 3,
                "Purple Pikmin in Louie's squad at the start of a run (Pikmin 2: heavy hitters that count as ten carriers). " +
                "Only Louie starts with them, and only when Pikmin 2's files are there.");
            StartWhite = Config.Bind("Pikmin", "StartWhite", 5,
                "White Pikmin in Louie's squad at the start of a run (Pikmin 2: fast, poison their eaters).");
            PoundGroundSeconds = Config.Bind("Pikmin", "PoundGroundSeconds", 4f,
                "How long a flying enemy stays down after a Purple Pikmin lands next to it (seconds). 0 turns the effect off.");
            WhitePoisonDamage = Config.Bind("Pikmin", "WhitePoisonDamage", 300f,
                "Pikmin attack points an enemy takes (times Combat/DamageCoefficient) when it eats a White Pikmin; " +
                "Pikmin 2 enemies take 300. 0 turns the poison off.");
            AutoRejoin = Config.Bind("Pikmin", "AutoRejoin", true,
                "Pikmin that get separated (done carrying or fighting, thrown with nothing to " +
                "do, left behind) run back to Olimar by themselves. Dismissed Pikmin still wait. " +
                "Off: Pikmin 1, where they wait to be whistled.");
            LatchLead = Config.Bind("Combat", "LatchLead", 1f,
                new ConfigDescription("The sim's Pikmin are drawn one 30 Hz tick behind real time, while an enemy's model is drawn live, " +
                    "so a fast enemy (a wisp) moves away from the Pikmin latched to it. Enemies' parts go to the sim this many ticks " +
                    "ahead (by their speed) to cancel that. 0 turns it off; more over-corrects.",
                    new AcceptableValueRange<float>(0f, 3f)));
            StrikeDamage = Config.Bind("Combat", "StrikeDamage", 4f,
                "The dismiss button (RB, F on keyboard) is a melee strike that no longer dismisses the squad: this many " +
                "times Olimar's punch damage to every enemy around him. 4 is about 4.5 times his damage stat: two strikes kill a Beetle at the start " +
                "(80 health, 20 armor). 0: it dismisses the squad as in Pikmin 1.");
            PunchDamage = Config.Bind("Combat", "PunchDamage", 22.5f,
                "Olimar's punch, in Pikmin attack points (times Combat.DamageCoefficient and his damage stat). Pikmin 1's is 10, half " +
                "his damage stat; 22.5 is a quarter of the dismiss strike's 4x, about 1.1 times his damage stat. 0: the disc's. " +
                "The strike is a multiple of this.");
            StrikeChargeSeconds = Config.Bind("Combat", "StrikeChargeSeconds", 3f,
                "After a strike, Pikmin charge enemies in the attack direction, jump, and latch on. With no target they swarm forward for this many seconds. " +
                "Moving the right stick overrides the forward swarm. 0: no charge.");
            StrikeStun = Config.Bind("Combat", "StrikeStun", true, "The strike staggers what it hits (RoR2's one-second stun; bosses resist).");
            StrikeKillReset = Config.Bind("Combat", "StrikeKillReset", true,
                "A kill with the strike gives its charge straight back. Off: the cooldown always runs in full.");
            StrikeReachMetres = Config.Bind("Combat", "StrikeReachMetres", 4f,
                "How far from an enemy's edge the strike reaches, all around Olimar.");
            StrikeCooldown = Config.Bind("Combat", "StrikeCooldown", 4f, "Seconds between strikes.");
            ShipParts = Config.Bind("Dolphin", "ShipPartsMode", false,
                "Retired setting, ignored. Select Ship Repair in the game-mode menu instead.");
            if (ShipParts.Value) Log.LogInfo("Dolphin.ShipPartsMode is retired. Select Ship Repair from the mode menu.");
            CompetitiveMode = Config.Bind("ShipRepair", "CompetitiveMode", true,
                "Host rule, locked at run start. True: each player repairs their own Dolphin; first to 30 unique parts wins. False: shared cooperative repair.");
            EnemyPartIntervalSeconds = Config.Bind("ShipRepair", "EnemyPartIntervalSeconds", 30f,
                "Host rule, locked at run start. Minimum seconds between each player's successful ordinary enemy part drops. Independent per player; stage-boss reward bypasses this gate. Range 0-600.");
            TreasuresPerStage = Config.Bind("ShipRepair", "TreasuresPerStage", 5,
                "Host rule, locked at run start, shared repair only. Pikmin 2 treasures lying in each stage as bonus objectives (needs the Pikmin 2 files); Pikmin carry them to the Dolphin and each scores in a separate treasure category. 0-16, 0 turns them off.");
            TreasureScoreWeight = Config.Bind("ShipRepair", "TreasureScoreWeight", ShipRepairTreasures.DefaultWeight,
                "Host rule. What a plain treasure adds to the final score as a fraction of one Dolphin part (1.0): 0.1 by default, richer treasures 1.5x, 2.5x and 4x that. Capped at 0.2, so no treasure is ever worth a whole part. 0 turns the category off.");
            TreasureMaxCarriers = Config.Bind("ShipRepair", "TreasureMaxCarriers", 25,
                "Host rule. Treasures that need more Pikmin than this to lift never lie in a stage (the Pikmin 2 table goes up to 101). 1-100.");
            StageMusic = Config.Bind("ShipRepair", "StageMusic", true, "Local audio: Pikmin jungle music in the lobby, Impact Site on stage one, seeded Pikmin music on later stages, and space-themed world-map music on results. Retains RoR2 boss music.");
            AdaptiveStageMusic = Config.Bind("ShipRepair", "AdaptiveStageMusic", true, "Local audio: nearby RoR2 enemies activate the original Pikmin enemy-proximity musical layers.");
            StageMusicVolume = Config.Bind("ShipRepair", "StageMusicVolume", 1f, "Local Pikmin stage music gain, multiplied by RoR2 music/master volume (0-2).");
            PikminJump = Config.Bind("Pikmin", "OlimarUsesPikminJump", true,
                "Olimar jumps with the Pikmin's own jump animation (they share the game's motion table). Off: his geyser-ride pose, tipped upright.");
            CursorHoming = Config.Bind("Combat", "CursorHoming", true,
                "A thrown Pikmin is steered in flight onto the cursor, the way a lock-on steers it onto its enemy, so it lands where " +
                "the cursor was whatever Olimar's speed or the cursor's height. Off: Pikmin 1's plain arc, whose height follows how long the throw was held.");
            PaceFlower = Config.Bind("Pikmin", "PaceFlower", 1.2f,
                "A flower Pikmin's top speed as a multiple of Olimar's full-tilt speed (his items and sprint included), so " +
                "it keeps up and stays in reach for a throw. 0 for any of the three: the disc's speeds.");
            PaceBud = Config.Bind("Pikmin", "PaceBud", 1.04f,
                "The same for a bud Pikmin: just faster than Olimar, so it's clearly quicker than a leaf.");
            // 0.97 was the old default, too close to a leaf's 0.93: a file still on it moves up.
            if (Mathf.Approximately(PaceBud.Value, 0.97f)) PaceBud.Value = 1.04f;
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
                "stand still (Bustling Fungus) work around it.");
            OnionHealth = Config.Bind("Pikmin", "OnionHealth", 400f, "Onion starting health; grows by 30% per level. Restart to apply.");
            OnionReviveSeconds = Config.Bind("Pikmin", "OnionReviveSeconds", 30f, "Dormant time before the Onion plays its three-second revival. Stock survives but is inaccessible.");
            OnionPelletHeal = Config.Bind("Pikmin", "OnionPelletHeal", 0.25f, "Fraction of maximum health healed per delivered pellet, plus 2% per seed (capped at full health).");
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
            TekiBodies.Bind(Config);
            MaxPellets = Config.Bind("Corpses", "MaxPellets", 30, "Most enemy corpses on the ground at once.");
            OnlyOwnKills = Config.Bind("Corpses", "OnlyOwnKills", true,
                "Only enemies that Olimar, his Pikmin or his Onions kill leave a corpse to carry. Off: every enemy that dies " +
                "while Olimar is in the game does, whoever killed it.");
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
            var sunshine = Config.Bind("Skins", "SunshineAssets", "",
                "The Super Mario Sunshine cache the Pianta skin is drawn from (the Mario plugin's extracted assets folder). " +
                "Empty: $SMS_ASSETS, then beside this plugin or the Mario plugin's assets_dir.txt. Without it the skin is not offered.");
            PiantaUnlocked = Config.Bind("Skins", "PiantaUnlocked", true,
                "Offer the Pianta skin without earning it. Open while it is being tested; it will become a reward for " +
                "defeating Mithrix and escaping the planet, and turning this off locks it until then.");
            PiantaHeight = Config.Bind("Skins", "PiantaHeight", 1.75f,
                new ConfigDescription("The Pianta's height to the top of his head, in metres. Olimar himself is OlimarHeight tall; " +
                    "Commando, and Mario by default, are 1.82.", new AcceptableValueRange<float>(0.5f, 3f)));
            LouieHeight = Config.Bind("Skins", "LouieHeight", 1f,
                new ConfigDescription("Louie's height as a multiple of Olimar's.", new AcceptableValueRange<float>(0.5f, 2f)));
            var p2disc = Config.Bind("Skins", "Pikmin2Disc", "",
                "Your Pikmin 2 disc image (.iso/.gcm/.ciso), used once to extract Louie's models and motions. Empty: look in " +
                "the plugin folder and next to AssetsDir. Without it Louie is not offered.");
            PikachuHeight = Config.Bind("Skins", "PikachuHeight", 1f,
                new ConfigDescription("Captain Pikachu's height as a multiple of Olimar's.", new AcceptableValueRange<float>(0.5f, 2f)));
            IceClimberHeight = Config.Bind("Skins", "IceClimberHeight", 0.42f,
                new ConfigDescription("An Ice Climber's height as a multiple of Olimar's (Captain Pikachu's squad).", new AcceptableValueRange<float>(0.2f, 1f)));
            var meleeCache = Config.Bind("Skins", "MeleeAssets", "",
                "The Melee plugin's extracted cache, where Captain Pikachu's models live. Empty: $SSBM_ASSETS, then beside this plugin or the Melee plugin's assets_dir.txt.");
            var meleeDisc = Config.Bind("Skins", "MeleeDisc", "",
                "Your Melee disc image (GALE01 revision 2, .iso/.gcm/.ciso), used once to add the Ice Climbers models to the Melee cache. Empty: look in the plugin folder and next to AssetsDir.");
            OlimarSkin.Discover(sunshine.Value, here);
            Log.LogInfo(OlimarSkin.PiantaAvailable ? $"Pianta skin: Sunshine assets at {OlimarSkin.SunshineDir}"
                : "Pianta skin: no Sunshine assets found, so it is not offered");
            SnapshotHz = Config.Bind("Net", "SnapshotHz", 10f,
                new ConfigDescription("How often, with other players in the game, the host sends them its Pikmin world " +
                    "(positions are blended between snapshots, so 10 looks smooth). More is smoother and uses more bandwidth.",
                    new AcceptableValueRange<float>(2f, 30f)));
            ReliableSnapshots = Config.Bind("Net", "ReliableSnapshots", false,
                "Send the host's snapshots reliably. Off: a lost one is skipped (its sounds and effects too); on: " +
                "none are lost but a bad connection backs up.");
            StickDeadzone = Config.Bind("Input", "StickDeadzone", 0f,
                new ConfigDescription("Gamepad sticks read below this (of full deflection) count as centred, and the rest is " +
                    "rescaled. 0 (default) leaves the game's own reading alone. Raise it (0.2-0.3) if a worn or badly centred stick drifts Olimar or his aim; a controller that is " +
                    "reconnected is recalibrated by the game, which is why it can fix drift.",
                    new AcceptableValueRange<float>(0f, 0.6f)));
            PerformanceLog = Config.Bind("Debug", "PerformanceLog", false,
                "Every ten seconds during a run, log frame times and Olimar's CPU timings (the sim's step, " +
                "gathering enemies, drawing) and how many sim ticks land in one physics step. For finding " +
                "what is slow on a weak machine; does not measure GPU time.");
            LogNet = Config.Bind("Debug", "LogNet", false,
                "Every ten seconds, log how much the host's snapshots send.");
            LogLatches = Config.Bind("Debug", "LogLatches", false,
                "Each time a Pikmin latches onto an enemy, log how far it is from the enemy's nearest real hurtbox " +
                "(0 is on the surface; a big number is a Pikmin latched onto air) and what kind of hurtbox that is.");
            LogEvents = Config.Bind("Debug", "LogEvents", false,
                "Log every sim event (latches, deaths, growth, deliveries, plucks, enemy attacks) to the " +
                "BepInEx log. Off by default: it adds a line per event and floods the log in a fight.");

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
            OlimarSkin.PrepareLouie(p2disc.Value, Path.Combine(AssetsDir, "p2"), here, Path.GetDirectoryName(Path.GetFullPath(AssetsDir)) ?? here);
            OlimarSkin.PreparePikachu(meleeCache.Value, meleeDisc.Value, here, Path.GetDirectoryName(Path.GetFullPath(AssetsDir)) ?? here);
            PikminDamageNumbers.Install(gameObject, Config);
            PikminHealthGauges.Install(gameObject, Config);
            ItemHooks.Install();
            // With the Pikmin controls the HUD's skill icons name the pad buttons the sim reads (Pad),
            // not RoR2's binds; the Aim scheme (VR) keeps RoR2's.
            PauseControls.Install(gameObject);
            PauseControls.Register("OlimarTestBody", _ => OlimarControls.Sheet());
            GlyphOverride.Install();
            GlyphOverride.Set("OlimarTestBody", () => Scheme == ControlScheme.Pikmin, new Dictionary<SkillSlot, PadButton[]>
            {
                [SkillSlot.Primary] = new[] { PadButton.A },
                [SkillSlot.Secondary] = new[] { PadButton.LB, PadButton.B },
                [SkillSlot.Utility] = new[] { PadButton.X },
                // The dismiss strike.
                [SkillSlot.Special] = new[] { PadButton.RB },
            });

            ContentProvider.Register(Guid, pack => { Content.Fill(pack); ShipRepairMode.Fill(pack); TekiBodies.Fill(pack); });
            Language.onCurrentLanguageChanged += AddTokens;
            PikminWorld.Hook();
            PikminNet.Register();
            ShipRepairMode.Install(gameObject);
            TekiBodies.Install();
            TekiSpawner.Install(gameObject);
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
            bool ready = Native.pik_assets_complete(Native.CString(AssetsDir)) != 0;
            string audio = Path.Combine(AssetsDir, "ror2_audio");
            bool needIntro = !File.Exists(Path.Combine(audio, "spawn.wav"))
                || !File.Exists(Path.Combine(audio, "stings.version"))
                || File.ReadAllText(Path.Combine(audio, "stings.version")).Trim() != "1";
            // An older extraction has only the first three enemies' files; the rest come from the disc (extraction tops it up).
            bool needTekis = ready && !File.Exists(Path.Combine(AssetsDir, "tekis", ".all"));
            if (ready && !needIntro && !needTekis) return;
            string disc = configuredDisc?.Trim();
            if ((!ready || needTekis) && string.IsNullOrEmpty(disc))
            {
                string parent = Path.GetDirectoryName(Path.GetFullPath(AssetsDir)) ?? here;
                var found = new byte[1024];
                if (Native.pik_find_disc(Native.CString(here + ";" + parent), found, (uint)found.Length) != 0)
                    disc = Native.Utf8(found);
            }
            if (needTekis && (string.IsNullOrEmpty(disc) || !File.Exists(disc)))
            {
                Log.LogWarning("Pikmin's other enemies need their files from your disc, and none was found; only the Bulborbs will spawn. " +
                               $"Set Paths.Disc in BepInEx/config/{Guid}.cfg to your Pikmin disc.");
                needTekis = false;
                if (!needIntro) return;
            }
            if (!ready && (string.IsNullOrEmpty(disc) || !File.Exists(disc)))
            {
                Log.LogError($"No Pikmin assets at '{AssetsDir}' and no disc image to build them from. " +
                             $"Set Paths.Disc in BepInEx/config/{Guid}.cfg to your own Pikmin disc. " +
                             "Until then the sim runs on code defaults, which differ from the game's tuning.");
                return;
            }
            assetsBusy = true;
            Log.LogInfo(ready ? "Preparing Pikmin arrival music..." : $"Extracting Pikmin assets from {disc} into {AssetsDir}...");
            var worker = new Thread(() =>
            {
                try
                {
                    var msg = new byte[2048];
                    if (needTekis)
                    {
                        byte ok = Native.pik_extract(Native.CString(disc), Native.CString(AssetsDir), msg, (uint)msg.Length);
                        if (ok != 0) Log.LogInfo($"Pikmin enemies' files added: {Native.Utf8(msg)}");
                        else Log.LogWarning($"Couldn't add Pikmin's other enemies: {Native.Utf8(msg)}");
                    }
                    if (!ready)
                    {
                        byte ok = Native.pik_extract(Native.CString(disc), Native.CString(AssetsDir), msg, (uint)msg.Length);
                        ready = ok != 0 && Native.pik_assets_complete(Native.CString(AssetsDir)) != 0;
                        if (ready) Log.LogInfo($"Extracted: {Native.Utf8(msg)}");
                        else Log.LogError($"Extraction failed: {Native.Utf8(msg)}");
                    }
                    if (ready)
                    {
                        if (Native.pik_render_stings(Native.CString(AssetsDir), msg, (uint)msg.Length) != 0)
                            Log.LogInfo($"Pikmin audio: {Native.Utf8(msg)}");
                        else Log.LogWarning($"Pikmin arrival music unavailable: {Native.Utf8(msg)}");
                    }
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

        void Update()
        {
            OlimarPerformance.Report();
            if (musicInitialized || !AssetsSettled) return;
            musicInitialized = true;
            // Like Mario, bind config and load the worker's completed cache on the Unity thread.
            if (AssetsReady) SpawnMusic.Initialize(Config, Path.Combine(AssetsDir, "ror2_audio"));
        }

        static void AddTokens()
        {
            var lang = Language.currentLanguage;
            if (lang == null) return;
            lang.SetStringByToken("OLIMARTEST_BODY_NAME", "Olimar");
            OlimarSkin.Tokens(lang);
            lang.SetStringByToken("OLIMARTEST_BODY_SUBTITLE", "Captain of the S.S. Dolphin");
            lang.SetStringByToken("OLIMARTEST_BODY_DESCRIPTION",
                "Captain Olimar's ship has crashed on a strange planet, and his only help is a swarm of Pikmin: loyal little " +
                "creatures that follow his whistle, fight at his side, and carry whatever he defeats back to the Onion to " +
                "grow into more of themselves.\n\n" +
                "< ! > Lead a squad of Red, Yellow and Blue Pikmin.\n" +
                "< ! > Throw Pikmin onto enemies to latch on and fight, or whistle them back to safety.\n" +
                "< ! > Defeated enemies are carried to the Onion, which turns them into new Pikmin.\n" +
                "< ! > Sprouts grow from leaf to bud to flower, ready to be plucked and join the squad.");
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

        // A round onion-coloured portrait for the ally card: a shaded sphere with a highlight and a dark rim.
        static Texture2D OnionPortrait()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Onion portrait", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - 31.5f) / 30f, dy = (y - 31.5f) / 30f, r = Mathf.Sqrt(dx * dx + dy * dy);
                    if (r > 1f) { pixels[y * size + x] = Color.clear; continue; }
                    float light = Mathf.Clamp01(0.55f + 0.45f * (-dx * 0.5f + dy * 0.7f + 0.3f));
                    var body = Color.Lerp(new Color(0.42f, 0.13f, 0.05f), new Color(1f, 0.55f, 0.15f), light);
                    float rim = Mathf.Clamp01((r - 0.82f) / 0.18f);
                    var c = Color.Lerp(body, new Color(0.12f, 0.05f, 0.03f), rim);
                    c.a = Mathf.Clamp01((1f - r) * 20f);
                    pixels[y * size + x] = c;
                }
            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return tex;
        }

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
            // The clone inherits Commando's RagdollController, so vanilla's death state would drop
            // a Commando ragdoll corpse. Olimar's knockdown (ODead) is drawn by the native view in
            // its place, so remove the controller and leave no vanilla corpse.
            foreach (var ragdoll in body.GetComponentsInChildren<RagdollController>(true))
                UnityEngine.Object.DestroyImmediate(ragdoll);
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
                targetable = true,
                invincible = false,
                baseMaxHealth = Mathf.Max(1f, Plugin.OnionHealth.Value),
                levelMaxHealth = Mathf.Max(1f, Plugin.OnionHealth.Value) * 0.3f,
                baseRegen = 0f, levelRegen = 0f,
                itemFilter = item => Plugin.OnionItems.Value && OnionItem(item),
            });

            // The Onion's card in RoR2's ally list: its health, with a portrait of its own.
            var onionBody = OnionMinion != null && OnionMinion.body ? OnionMinion.body.GetComponent<CharacterBody>() : null;
            if (onionBody) onionBody.portraitIcon = OnionPortrait();

            var display = Prefabs.Clone(commandoDisplay, "OlimarTestDisplay", Plugin.Guid);
            DisplayModel.Attach(display, Plugin.Guid, OlimarDisplay.Get);
            // His portrait, from the same lobby Olimar without the Pikmin beside him.
            Portrait.Request(body, OlimarDisplay.Get, d => d.kind != Native.KindCaptain, framing: PortraitFraming.ThreeQuarter);

            var survivor = ScriptableObject.CreateInstance<SurvivorDef>();
            survivor.cachedName = "OlimarTest";
            survivor.bodyPrefab = body;
            survivor.displayPrefab = display;
            survivor.displayNameToken = "OLIMARTEST_BODY_NAME";
            survivor.descriptionToken = "OLIMARTEST_BODY_DESCRIPTION";
            survivor.outroFlavorToken = "OLIMARTEST_BODY_OUTRO";
            survivor.mainEndingEscapeFailureFlavorToken = "OLIMARTEST_BODY_FAIL";
            survivor.primaryColor = cb.bodyColor;
            // Second behind Mario everywhere except Ship Repair, where ShipRepairMode.ApplySelectOrder puts him first.
            survivor.desiredSortPosition = SurvivorOrder.OlimarPosition(false);

            // Olimar's own skins in place of the Commando body's.
            OlimarSkin.Register(body);

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
