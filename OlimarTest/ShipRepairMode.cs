using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MonoMod.RuntimeDetour;
using Ror2Common;
using RoR2;
using RoR2.ContentManagement;
using RoR2.UI;
using RoR2.UI.MainMenu;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace OlimarTest
{
    // A marker on a cloned vanilla Run prefab keeps its original network serialization
    // and scene/rule behavior. No custom NetworkBehaviour or unweaved SyncVars.
    public sealed class ShipRepairMarker : MonoBehaviour { }

    internal static class ShipRepairMode
    {
        public const string PrefabName = "OlimarShipRepairRun";
        public const string NameToken = "OLIMAR_SHIP_REPAIR_NAME";
        public static GameEndingDef Ending { get; private set; }
        static readonly List<Hook> hooks = new List<Hook>();
        static bool checkedAssets;
        static string assetError;
        static readonly BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        public static bool Active => IsRepair(Run.instance);
        public static bool IsRepair(Run run) => run && run.GetComponent<ShipRepairMarker>();
        public static bool LobbyActive => PreGameController.instance &&
            IsRepair(GameModeCatalog.GetGameModePrefabComponent(PreGameController.instance.gameModeIndex));

        public static void Fill(ContentPack pack)
        {
            var source = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/ClassicRun/ClassicRun.prefab").WaitForCompletion();
            if (!source) throw new InvalidOperationException("ClassicRun prefab is unavailable; Ship Repair cannot register.");
            var prefab = Prefabs.Clone(source, PrefabName, Plugin.Guid);
            prefab.AddComponent<ShipRepairMarker>();
            var run = prefab.GetComponent<Run>();
            run.nameToken = NameToken;
            run.userPickable = true;
            pack.gameModePrefabs.Add(new[] { prefab });
            pack.networkedObjectPrefabs.Add(new[] { prefab });
            // The normal report ending has no boss cinematic or credits transition.
            // Its address is verified against this build's GameAssetPaths.bin.
            var standard = Addressables.LoadAssetAsync<GameEndingDef>("RoR2/Base/ClassicRun/StandardLoss.asset").WaitForCompletion();
            if (!standard) throw new InvalidOperationException("Standard report ending is unavailable.");
            Ending = UnityEngine.Object.Instantiate(standard);
            ((ScriptableObject)Ending).name = "OlimarShipRepairWin";
            Ending.cachedName = "OlimarShipRepairWin";
            Ending.isWin = true;
            Ending.showCredits = false;
            Ending.lunarCoinReward = 0;
            Ending.endingTextToken = "OLIMAR_SHIP_REPAIR_WIN";
            pack.gameEndingDefs.Add(new[] { Ending });
        }

        public static void Install()
        {
            NetworkModCompatibilityHelper.networkModList = NetworkModCompatibilityHelper.networkModList
                .Concat(new[] { Plugin.Guid + ";ship-repair-abi30-v1" });
            hooks.Add(new Hook(typeof(Run).GetProperty("spawnWithPod").GetMethod,
                (Func<Func<Run, bool>, Run, bool>)((orig, run) => IsRepair(run) || orig(run))));
            hooks.Add(new Hook(typeof(PreGameController).GetMethod("StartLaunch", flags),
                (Action<Action<PreGameController>, PreGameController>)((orig, lobby) => { if (CanLaunch()) orig(lobby); })));
            hooks.Add(new Hook(typeof(PreGameController).GetMethod("StartRun", flags),
                (Action<Action<PreGameController>, PreGameController>)((orig, lobby) => { if (CanLaunch()) orig(lobby); })));
            hooks.Add(new Hook(typeof(CharacterSelectController).GetMethod("ClientSetReady", flags),
                (Action<Action<CharacterSelectController>, CharacterSelectController>)((orig, ui) => { if (CanLaunch()) orig(ui); })));
            hooks.Add(new Hook(typeof(CharacterSelectController).GetMethod("Update", flags),
                (Action<Action<CharacterSelectController>, CharacterSelectController>)((orig, ui) => {
                    orig(ui);
                    if (LobbyActive && LobbyError() != null && ui.readyButton) ui.readyButton.interactable = false;
                })));
            hooks.Add(new Hook(typeof(Run).GetMethod("AdvanceStage", flags),
                (Action<Action<Run, SceneDef>, Run, SceneDef>)((orig, run, scene) =>
                {
                    if (IsRepair(run))
                    {
                        if (ShipRepairSession.Instance && ShipRepairSession.Instance.Winning) return;
                        ShipRepairSession.Instance?.CloseStage();
                        if (!Allowed(scene))
                        {
                            var choices = new WeightedSelection<SceneDef>();
                            run.startingSceneGroup.AddToWeightedSelection(choices, s => run.CanPickStage(s));
                            run.PickNextStageScene(choices);
                            scene = run.nextStageScene;
                            Chat.AddMessage("Ship Repair: returning to the stage loop. Recover all 30 parts to escape.");
                        }
                    }
                    orig(run, scene);
                })));
            hooks.Add(new Hook(typeof(Run).GetMethod("BeginGameOver", flags),
                (Action<Action<Run, GameEndingDef>, Run, GameEndingDef>)((orig, run, ending) =>
                {
                    if (IsRepair(run))
                    {
                        if (ShipRepairSession.Instance && ShipRepairSession.Instance.Terminal && ending != Ending) return;
                        if (ending.isWin && ending != Ending) return;
                        if (!ending.isWin && ShipRepairSession.Instance)
                        {
                            ShipRepairSession.Instance.ScheduleFailure(() => orig(run, ending));
                            return;
                        }
                    }
                    orig(run, ending);
                })));
            HookPicker<HostGamePanelController>();
            HookPicker<LocalMultiplayerMenuController>();
            HookPicker<MultiplayerMenuController>();
            MainMenuController.OnMainMenuInitialised += AddSoloButton;
            Run.onRunStartGlobal += run => { if (IsRepair(run)) run.gameObject.AddComponent<ShipRepairSession>(); };
            Language.onCurrentLanguageChanged += Tokens;
            Tokens();
            ShipRepairSession.RegisterMessages();
        }

        static void HookPicker<T>() where T : Component
        {
            hooks.Add(new Hook(typeof(T).GetMethod("BuildGameModeChoices", flags),
                (Action<Action<T>, T>)((orig, panel) =>
                {
                    var picker = (CarouselController)typeof(T).GetField("gameModePicker", flags).GetValue(panel);
                    var choices = picker.choices.ToList();
                    if (!choices.Any(c => c.convarValue == PrefabName))
                        choices.Add(new CarouselController.Choice { convarValue = PrefabName, suboptionDisplayToken = NameToken });
                    picker.choices = choices.ToArray();
                    orig(panel);
                })));
        }

        static void AddSoloButton()
        {
            var menu = MainMenuController.instance;
            if (!menu || !menu.extraGameModeMenuScreen) return;
            var screen = menu.extraGameModeMenuScreen;
            if (screen.GetComponentsInChildren<HGButton>(true).Any(b => b.name == "ShipRepairButton")) return;
            var buttons = screen.GetComponentsInChildren<HGButton>(true);
            var source = buttons.FirstOrDefault(b => b.name.IndexOf("Eclipse", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? buttons.FirstOrDefault(b => b.interactable && b.name.IndexOf("Back", StringComparison.OrdinalIgnoreCase) < 0);
            if (!source) { Plugin.Log.LogWarning("No extra-mode button template found. Ship Repair remains in the host mode picker."); return; }
            var button = UnityEngine.Object.Instantiate(source, source.transform.parent);
            button.name = "ShipRepairButton";
            button.interactable = true;
            button.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => RoR2.Console.instance.SubmitCmd((NetworkUser)null, "gamemode " + PrefabName + "; host 0;", false));
            foreach (var text in button.GetComponentsInChildren<LanguageTextMeshController>(true)) text.token = NameToken;
            button.gameObject.SetActive(true);
        }

        static bool CanLaunch()
        {
            if (!LobbyActive) return true;
            string error = LobbyError();
            if (error == null && !checkedAssets)
            {
                checkedAssets = true;
                var sim = Native.pik_sim_new(Native.CString(Plugin.AssetsDir), 1);
                try
                {
                    if (sim == IntPtr.Zero) assetError = "Ship Repair could not load the disc tuning.";
                    else for (uint part = 0; part < 30; part++)
                        if (Native.pik_sim_ship_part_weight(sim, part) == 0 || Native.pik_sim_ship_part_weight(sim, part) > 100)
                        { assetError = "Ship Repair needs all 30 part models and carry configs. Rebuild the asset cache; missing part " + part; break; }
                }
                finally { if (sim != IntPtr.Zero) Native.pik_sim_free(sim); }
                error = assetError;
            }
            if (error == null) return true;
            Chat.AddMessage(error);
            Plugin.Log.LogWarning(error);
            return false;
        }

        internal static string LobbyError()
        {
            string error = assetError;
            if (!Plugin.AssetsReady) error = "Ship Repair needs your extracted Pikmin disc assets. Wait for extraction to finish.";
            else if (NetworkUser.readOnlyInstancesList.Any(u => u.isParticipating &&
                BodyCatalog.GetBodyPrefab(u.bodyIndexPreference)?.GetComponent<OlimarDriver>() == null))
                error = "Ship Repair requires every player to choose Olimar.";
            else if (Enumerable.Range(0, ArtifactCatalog.artifactCount).Any(i =>
                PreGameController.instance.readOnlyRuleBook.GenerateArtifactMask().HasArtifact((ArtifactIndex)i)))
                error = "Ship Repair's first version requires artifacts to be disabled.";
            return error;
        }

        // Normal stage scenes are sampled; Bazaar and Void Fields can return via vanilla
        // portals. Every other intermission/ending is redirected to expansion-eligible tier 1.
        public static bool Eligible(SceneDef scene) => scene && scene.sceneType == SceneType.Stage &&
            scene.stageOrder >= 1 && scene.stageOrder <= 5 &&
            scene.baseSceneName != "moon" && scene.baseSceneName != "moon2";
        public static bool Allowed(SceneDef scene) => Eligible(scene) || (scene &&
            (scene.baseSceneName == "bazaar" || scene.baseSceneName == "arena"));

        static void Tokens()
        {
            var language = Language.currentLanguage;
            if (language == null) return;
            language.SetStringByToken(NameToken, "Ship Repair");
            language.SetStringByToken("OLIMAR_SHIP_REPAIR_WIN", "The Dolphin is whole again. Home is finally within reach.");
        }
    }
}
