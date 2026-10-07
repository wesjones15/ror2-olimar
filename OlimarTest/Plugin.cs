using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using EntityStates;
using RoR2;
using RoR2.ContentManagement;
using UnityEngine;
using UnityEngine.Networking;
using Path = System.IO.Path;

namespace OlimarTest
{
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
    // own content-pack hook.
    [BepInPlugin(Guid, "Olimar (test)", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.libpikmin.olimartest";
        const string VrModGuid = "com.DrBibop.VRMod";

        internal static ManualLogSource Log;
        internal static string AssetsDir;
        static volatile bool assetsBusy;

        internal static ControlScheme Scheme;
        internal static ConfigEntry<int> StartRed, StartYellow, StartBlue;
        internal static ConfigEntry<bool> OnionPerColour;
        internal static ConfigEntry<float> DamageCoefficient, ProcCoefficient, PikminHealth, PikminHurtRadius;
        internal static ConfigEntry<float> OlimarHealth, OlimarHealthPerLevel;
        internal static ConfigEntry<int> MaxPellets;
        internal static ConfigEntry<float> CarriersPerMetre;
        internal static ConfigEntry<bool> LogEvents;

        /// Extraction has finished (or wasn't needed). The sim waits for this so it starts on
        /// the disc's tuning.
        internal static bool AssetsSettled => !assetsBusy;

        void Awake()
        {
            Log = Logger;
            string here = Path.GetDirectoryName(Info.Location);

            var assets = Config.Bind("Paths", "AssetsDir", "",
                "libpikmin's assets/ folder (extracted from your own Pikmin disc). Empty: read " +
                "assets_dir.txt next to the plugin, then $PIKMIN_ASSETS, then assets/ next to the plugin.");
            var disc = Config.Bind("Paths", "Disc", "",
                "Your Pikmin (GameCube) disc image (.iso/.gcm/.ciso), used once to build AssetsDir " +
                "if it's missing. Empty: look in the plugin folder and next to AssetsDir.");
            UnityHost.Scale = Config.Bind("Sim", "MetresPerUnit", 0.04f,
                "Metres per Pikmin unit. 0.04 makes Olimar run at 6.4 m/s and a whistle 3.6 m wide.").Value;
            var scheme = Config.Bind("Controls", "Scheme", ControlScheme.Auto,
                "Pikmin: the sim moves Olimar, pad buttons by position (A throw/pluck, B whistle, " +
                "X dismiss, right stick swarm, L recentre camera, R zoom). Aim: RoR2 moves the body " +
                "and the cursor is where you aim (primary throw, secondary whistle, utility " +
                "dismiss, special swarm). Auto: Aim in VR (VRMod), Pikmin otherwise.");
            StartRed = Config.Bind("Pikmin", "StartRed", 10, "Red Pikmin in the squad at the start of a run.");
            StartYellow = Config.Bind("Pikmin", "StartYellow", 5, "Yellow Pikmin at the start of a run.");
            StartBlue = Config.Bind("Pikmin", "StartBlue", 5, "Blue Pikmin at the start of a run.");
            OnionPerColour = Config.Bind("Pikmin", "OnionPerColour", false,
                "One Onion per colour (Pikmin 1) instead of a single Onion that takes all three.");
            DamageCoefficient = Config.Bind("Combat", "DamageCoefficient", 0.05f,
                "RoR2 damage per point of Pikmin attack power, times Olimar's damage stat. A Red " +
                "hits for 15 points: 0.75x Olimar's damage per swing at the default.");
            ProcCoefficient = Config.Bind("Combat", "ProcCoefficient", 0.1f,
                "Proc coefficient of a Pikmin hit (items like Ukulele). Pikmin hit often; keep it low.");
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
            CarriersPerMetre = Config.Bind("Corpses", "CarriersPerMetre", 2f,
                "Pikmin needed to lift a corpse per metre of the enemy's radius (1 to 20).");
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

            ContentManager.collectContentPackProviders += add => add(new Content());
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
            lang.SetStringByToken("OLIMARTEST_BODY_NAME", "Olimar (test)");
            lang.SetStringByToken("OLIMARTEST_BODY_SUBTITLE", "Captain of the S.S. Dolphin");
            lang.SetStringByToken("OLIMARTEST_BODY_DESCRIPTION",
                "Commando's body leading a squad of Pikmin from libpikmin.\n\n" +
                "< ! > Throw Pikmin onto enemies; they latch on and fight.\n" +
                "< ! > Whistle to call Pikmin back; corpses are carried to the Onion for more.\n" +
                "< ! > Pad: A throw/pluck, B whistle, X dismiss, right stick swarm, L camera, R zoom.\n" +
                "< ! > Keyboard: primary throw, secondary whistle, utility dismiss, special swarm.");
            lang.SetStringByToken("OLIMARTEST_BODY_OUTRO", "..and so he left, with a ship full of seeds.");
            lang.SetStringByToken("OLIMARTEST_BODY_FAIL", "..and so he vanished, his Pikmin waiting by the Onion.");
        }
    }

    internal class Content : IContentPackProvider
    {
        readonly ContentPack pack = new ContentPack();

        public string identifier => Plugin.Guid;

        public IEnumerator LoadStaticContentAsync(LoadStaticContentAsyncArgs args)
        {
            var commando = LegacyResourcesAPI.Load<GameObject>("Prefabs/CharacterBodies/CommandoBody");
            var commandoDisplay = LegacyResourcesAPI.Load<GameObject>("Prefabs/CharacterDisplays/CommandoDisplay");
            if (!commando || !commandoDisplay)
            {
                Plugin.Log.LogError($"Commando not found (body {(bool)commando}, display {(bool)commandoDisplay})");
                yield break;
            }

            var body = Prefabs.Clone(commando, "OlimarTestBody", networked: true);
            var cb = body.GetComponent<CharacterBody>();
            cb.baseNameToken = "OLIMARTEST_BODY_NAME";
            cb.subtitleNameToken = "OLIMARTEST_BODY_SUBTITLE";
            cb.bodyColor = new Color(0.95f, 0.75f, 0.2f);
            cb.baseMaxHealth = Plugin.OlimarHealth.Value;
            cb.levelMaxHealth = Plugin.OlimarHealthPerLevel.Value;
            foreach (var esm in body.GetComponents<EntityStateMachine>())
                if (esm.customName == "Body")
                    esm.mainStateType = new SerializableEntityStateType(typeof(OlimarMain));
            body.AddComponent<OlimarDriver>();

            var display = Prefabs.Clone(commandoDisplay, "OlimarTestDisplay", networked: false);

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
            pack.entityStateTypes.Add(new[] { typeof(OlimarMain) });
            Plugin.Log.LogInfo("Olimar (test) registered");
        }

        public IEnumerator GenerateContentPackAsync(GetContentPackAsyncArgs args)
        {
            ContentPack.Copy(pack, args.output);
            yield break;
        }

        public IEnumerator FinalizeAsync(FinalizeAsyncArgs args)
        {
            yield break;
        }
    }

    internal static class Prefabs
    {
        static GameObject holder;

        /// A prefab copy, kept under an inactive object so it never wakes up in the scene. A
        /// networked copy gets its own asset id, so clients spawn it rather than Commando
        /// (what R2API's PrefabAPI.InstantiateClone does).
        public static GameObject Clone(GameObject src, string name, bool networked)
        {
            if (!holder)
            {
                holder = new GameObject("OlimarTestPrefabs");
                holder.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(holder);
            }
            var copy = UnityEngine.Object.Instantiate(src, holder.transform);
            copy.name = name;
            var id = copy.GetComponent<NetworkIdentity>();
            if (id)
            {
                if (!networked)
                    Plugin.Log.LogWarning($"{name} has a NetworkIdentity but is not registered as networked");
                var hash = NetworkHash128.Parse(Md5Hex(Plugin.Guid + "/" + name));
                typeof(NetworkIdentity)
                    .GetField("m_AssetId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(id, hash);
            }
            return copy;
        }

        static string Md5Hex(string s)
        {
            using (var md5 = MD5.Create())
            {
                var sb = new StringBuilder();
                foreach (byte b in md5.ComputeHash(Encoding.UTF8.GetBytes(s))) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
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
