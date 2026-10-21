using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using MonoMod.RuntimeDetour;
using Ror2Common;
using RoR2;
using RoR2.ContentManagement;
using RoR2.Navigation;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace OlimarTest
{
    // Pikmin's enemies as RoR2 monsters, for Ship Repair only (TekiRoster lists them). Each is a clone of a
    // vanilla monster with its AI, skills, hurt boxes and health, its model drawn by libpikmin's view
    // instead (TekiBody), sized to the Bulborb it carries, and its life and speed from the disc's tekipara. The combat director spawns them from cards added to the stage's monster
    // categories while a Ship Repair run is on. A dead one leaves the disc's corpse pellet (PikminWorld.OnDeath →
    // Died), and a big one also drops a ship part for its killer (ShipRepairSession.Teki).
    internal static class TekiBodies
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> BigMinStages;
        internal static ConfigEntry<float> HealthScale, SpeedScale, DamageScale, CarrierScale;
        internal static ConfigEntry<bool> FitSizes;
        static ConfigEntry<int>[] weights, costs;

        /// Seconds a bite's animation is squeezed into (RoR2's skills are shorter than the disc's).
        internal const float AttackSeconds = 1.3f;

        sealed class Entry
        {
            public TekiSpec spec;
            public CharacterSpawnCard card;
        }

        static readonly List<Entry> entries = new List<Entry>();
        static readonly List<Hook> hooks = new List<Hook>();
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly FieldInfo modifiable = typeof(ClassicStageInfo).GetField("modifiableMonsterCategories", Flags);

        public static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Enemies", "Enabled", true,
                "Ship Repair spawns Pikmin's enemies (drawn from your disc) among the monsters: small ones among the basic " +
                "monsters, big ones (which always drop a ship part) among the minibosses.");
            FitSizes = config.Bind("Enemies", "FitSizes", true,
                "Scale each borrowed monster's model, hurt boxes and hit boxes to the size of the Pikmin enemy drawn on it.");
            BigMinStages = config.Bind("Enemies", "BigMinStagesCleared", 0, "Stages cleared before big Pikmin enemies can spawn.");
            HealthScale = config.Bind("Enemies", "HealthScale", 1f,
                "RoR2 health per point of the disc's life (a Dwarf Bulborb has 130, a Spotty Bulborb 1100, a Smoky Progg 4000).");
            SpeedScale = config.Bind("Enemies", "SpeedScale", 1.5f,
                "Multiplies the disc's run speed (metres a second at the plugin's scale) for their RoR2 bodies.");
            DamageScale = config.Bind("Enemies", "DamageScale", 1f, "Multiplies the borrowed monster's damage.");
            CarrierScale = config.Bind("Enemies", "CorpseCarrierScale", 1f,
                "Multiplies the Pikmin a corpse needs to be lifted, from the disc's pellet config (3-6 Dwarf, 10-20 Spotty).");
            weights = new ConfigEntry<int>[TekiRoster.All.Length];
            costs = new ConfigEntry<int>[TekiRoster.All.Length];
            foreach (var spec in TekiRoster.All)
            {
                string stem = spec.Display.Replace(" ", "");
                weights[spec.Kind] = config.Bind("Enemies", stem + "Weight", spec.Weight,
                    $"The combat director's weight for the {spec.Display} among the {(spec.Big ? "minibosses" : "basic monsters")} (most vanilla ones are 1). 0 turns it off.");
                costs[spec.Kind] = config.Bind("Enemies", stem + "Cost", spec.Cost, $"Director credits a {spec.Display} costs.");
            }
        }

        /// The bodies, masters and spawn cards, at content load.
        public static void Fill(ContentPack pack)
        {
            foreach (var spec in TekiRoster.All) Define(pack, spec);
        }

        static void Define(ContentPack pack, TekiSpec spec)
        {
            try
            {
                var srcBody = Addressables.LoadAssetAsync<GameObject>(spec.Body).WaitForCompletion();
                var srcMaster = Addressables.LoadAssetAsync<GameObject>(spec.Master).WaitForCompletion();
                if (!srcBody || !srcMaster || !srcBody.GetComponent<CharacterBody>() || !srcMaster.GetComponent<CharacterMaster>())
                {
                    Plugin.Log.LogWarning($"Pikmin enemies: {spec.Body} or {spec.Master} isn't in this RoR2; no {spec.Display}.");
                    return;
                }
                string key = "OlimarTeki" + spec.Token;
                var body = Prefabs.Clone(srcBody, key + "Body", Plugin.Guid);
                var cb = body.GetComponent<CharacterBody>();
                cb.baseNameToken = $"OLIMARTEST_TEKI_{spec.Token}_NAME";
                cb.subtitleNameToken = "OLIMARTEST_TEKI_SUBTITLE";
                // The model is hidden (its renderers are switched off each frame), and its hurt boxes ride its
                // bones, so the animator has to keep running with nothing drawn.
                foreach (var animator in body.GetComponentsInChildren<Animator>(true))
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var teki = body.AddComponent<TekiBody>();
                teki.kind = spec.Kind;
                teki.big = spec.Big;

                var master = Prefabs.Clone(srcMaster, key + "Master", Plugin.Guid);
                master.GetComponent<CharacterMaster>().bodyPrefab = body;
                pack.bodyPrefabs.Add(new[] { body });
                pack.masterPrefabs.Add(new[] { master });

                var card = ScriptableObject.CreateInstance<CharacterSpawnCard>();
                card.name = "csc" + key;
                card.prefab = master;
                card.sendOverNetwork = true;
                card.hullSize = spec.Hull == TekiHull.Human ? HullClassification.Human : HullClassification.Golem;
                card.nodeGraphType = spec.Flying ? MapNodeGroup.GraphType.Air : MapNodeGroup.GraphType.Ground;
                card.requiredFlags = NodeFlags.None;
                card.forbiddenFlags = NodeFlags.NoCharacterSpawn;
                card.directorCreditCost = Mathf.Max(1, costs[spec.Kind].Value);
                card.occupyPosition = false;
                card.loadout = new SerializableLoadout();
                card.noElites = false;
                card.forbiddenAsBoss = false;
                entries.Add(new Entry { spec = spec, card = card });
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Pikmin enemies: {spec.Display} failed to register: {e}");
            }
        }

        public static void Install()
        {
            TekiCorpses.RegisterMessages();
            Language.onCurrentLanguageChanged += AddTokens;
            var rebuild = typeof(ClassicStageInfo).GetMethod("RebuildCards", Flags);
            if (rebuild == null)
            {
                Plugin.Log.LogWarning("Pikmin enemies: ClassicStageInfo.RebuildCards isn't in this RoR2; they won't spawn.");
                return;
            }
            var ps = rebuild.GetParameters();
            try
            {
                if (ps.Length == 2 && ps[0].ParameterType == typeof(DirectorCardCategorySelection) && ps[1].ParameterType == typeof(DirectorCardCategorySelection))
                    hooks.Add(new Hook(rebuild, (Action<Action<ClassicStageInfo, DirectorCardCategorySelection, DirectorCardCategorySelection>,
                        ClassicStageInfo, DirectorCardCategorySelection, DirectorCardCategorySelection>)((orig, self, monsters, interactables) =>
                    {
                        orig(self, monsters, interactables);
                        AddCards(self);
                    })));
                else if (ps.Length == 0)
                    hooks.Add(new Hook(rebuild, (Action<Action<ClassicStageInfo>, ClassicStageInfo>)((orig, self) =>
                    {
                        orig(self);
                        AddCards(self);
                    })));
                else Plugin.Log.LogWarning($"Pikmin enemies: RebuildCards has an unexpected signature ({ps.Length} parameters); they won't spawn.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Pikmin enemies: couldn't hook the stage's monster cards: {e}");
            }
        }

        static void AddTokens()
        {
            var lang = Language.currentLanguage;
            if (lang == null) return;
            foreach (var spec in TekiRoster.All) lang.SetStringByToken($"OLIMARTEST_TEKI_{spec.Token}_NAME", spec.Display);
            lang.SetStringByToken("OLIMARTEST_TEKI_SUBTITLE", "Creature of the distant planet");
        }

        // Ship Repair's monster list gets the Bulborbs: the small in the basic monsters, the big among the
        // minibosses. Only when the disc's files for the kind are there.
        static void AddCards(ClassicStageInfo stage)
        {
            if (!Enabled.Value || !stage || !ShipRepairMode.Active || !TekiCatalog.Ready) return;
            var monsters = modifiable?.GetValue(stage) as DirectorCardCategorySelection;
            if (monsters == null) return;
            bool added = false;
            foreach (var e in entries)
            {
                var spec = e.spec;
                int weight = weights[spec.Kind].Value;
                if (weight <= 0 || !TekiCatalog.Has(spec.Kind)) continue;
                if (monsters.categories.Any(c => c.cards != null && c.cards.Any(d => d != null && d.spawnCard == e.card))) continue;
                int at = FindCategory(monsters, spec.Big ? "mini" : "basic");
                if (at < 0) continue;
                var card = new DirectorCard
                {
                    spawnCard = e.card,
                    selectionWeight = weight,
                    spawnDistance = DirectorCore.MonsterSpawnDistance.Standard,
                    preventOverhead = false,
                    minimumStageCompletions = spec.Big ? Mathf.Max(0, BigMinStages.Value) : 0,
                };
                monsters.AddCard(at, card);
                added = true;
                Plugin.Log.LogInfo($"Ship Repair: {e.card.name} added to \"{monsters.categories[at].name}\" (weight {weight}).");
            }
            // The director draws from the selection built from the categories, so it is built again.
            if (added)
            {
                var setter = typeof(ClassicStageInfo).GetProperty("monsterSelection", Flags)?.GetSetMethod(true);
                if (setter != null) setter.Invoke(stage, new object[] { monsters.GenerateDirectorCardWeightedSelection() });
                else Plugin.Log.LogWarning("Pikmin enemies: the stage's monster selection can't be rebuilt; the director may not pick them.");
            }
        }

        static int FindCategory(DirectorCardCategorySelection selection, string part)
        {
            var categories = selection.categories;
            if (categories == null || categories.Length == 0) return -1;
            for (int i = 0; i < categories.Length; i++)
                if (categories[i].name != null && categories[i].name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0) return i;
            // No such category: the one with the most cards.
            int best = 0;
            for (int i = 1; i < categories.Length; i++)
                if ((categories[i].cards?.Length ?? 0) > (categories[best].cards?.Length ?? 0)) best = i;
            return best;
        }

        /// A Pikmin enemy died on the server: its corpse is the disc's pellet (carriers and seeds from
        /// pelMgr.bin), on the body. False if its kind isn't loaded, so the generic corpse is left to do.
        public static bool Died(IntPtr sim, CharacterBody victim, TekiBody teki)
        {
            if (!TekiCatalog.Has(teki.kind)) return false;
            var info = TekiCatalog.Get(teki.kind);
            float yaw = UnityHost.Yaw(victim.transform.forward);
            var foot = UnityHost.ToSim(UnityHost.Snap(victim.footPosition));
            float s = Mathf.Sin(yaw), c = Mathf.Cos(yaw);
            var o = info.corpseOffset;
            var (min, max) = TekiRules.Carriers(info.corpseMinCarriers, info.corpseMaxCarriers, CarrierScale.Value);
            var desc = new Native.PelletDesc
            {
                pos = new Native.Vec3(foot.x + o.x * c + o.z * s, foot.y, foot.z - o.x * s + o.z * c),
                yaw = yaw,
                radius = Mathf.Max(5f, info.corpseSize),
                height = info.corpseHeight,
                min_carriers = min,
                max_carriers = max,
                seeds_matching = info.corpseSeedsMatching,
                seeds_other = info.corpseSeedsOther,
                color = -1,
            };
            int id = Native.pik_sim_spawn_pellet(sim, ref desc);
            if (id < 0) return true;
            // The Pikmin that fought it go to carry it.
            Native.pik_sim_recruit_to_pellet(sim, (uint)id, unchecked((ulong)(uint)victim.GetInstanceID()), 8f / UnityHost.Scale);
            TekiCorpses.Announce((uint)id, teki.kind);
            if (Plugin.LogEvents.Value)
                Plugin.Log.LogInfo($"{victim.GetDisplayName()} left corpse {id} (needs {min}-{max} Pikmin)");
            return true;
        }
    }
}
