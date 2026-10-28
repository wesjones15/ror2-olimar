using System;
using System.Collections.Generic;
using System.IO;
using RoR2;
using Ror2Common;
using UnityEngine;
using Path = System.IO.Path;

namespace OlimarTest
{
    /// One alt-skin of Olimar: a different body for the same captain. His sim, Pikmin, whistle and
    /// controls stay Olimar's; libpikmin draws another model in his place (pik_view_set_captain_skin).
    internal sealed class OlimarSkinSpec
    {
        /// The native skin id: 0 Olimar, 1 Louie, 2 the Pianta.
        public uint Id;
        public string Name, Token, Text;
        /// The SkinDef, set at registration.
        public SkinDef Def;
        /// Offered right now (its files are there and, if locked, it is registered with its key).
        public bool Registered;
    }

    // Olimar's skins. The Commando body Olimar is cloned from brings Commando's own skins along;
    // Register replaces them with these, the way ror2-mario's MarioSkin does for Mario.
    internal static class OlimarSkin
    {
        public const uint Olimar = 0, Louie = 1, Pianta = 2, Pikachu = 3;

        public static readonly OlimarSkinSpec[] Specs =
        {
            new OlimarSkinSpec { Id = Olimar, Name = "OlimarSkinDefault", Token = "OLIMARTEST_SKIN_DEFAULT", Text = "Olimar" },
            new OlimarSkinSpec { Id = Louie, Name = "OlimarSkinLouie", Token = "OLIMARTEST_SKIN_LOUIE", Text = "Louie" },
            new OlimarSkinSpec { Id = Pianta, Name = "OlimarSkinPianta", Token = "OLIMARTEST_SKIN_PIANTA", Text = "Pianta" },
            new OlimarSkinSpec { Id = Pikachu, Name = "OlimarSkinPikachu", Token = "OLIMARTEST_SKIN_PIKACHU", Text = "Captain Pikachu" },
        };

        static readonly List<OlimarSkinSpec> loadout = new List<OlimarSkinSpec>();
        static string sunshineDir, louieDir, meleeDir;
        static bool ready, louieReady, pikachuReady;

        /// Where the Sunshine cache the Pianta comes from is, when its files are all there.
        public static string SunshineDir => ready ? sunshineDir : null;
        /// Whether the Pianta can be drawn at all (Sunshine's extracted files are found).
        public static bool PiantaAvailable => ready;

        // The Pianta model and texture, Mario's rig for his locomotion, and a clip of each kind.
        static readonly string[] PiantaFiles =
        {
            "npc/montem/mom_model.bmd", "npc/montemcommon/i_mom_mino_rgba.bti", "npc/montemcommon/mom_wait.bck",
            "bmd/ma_mdl1.bmd", "bck/ma_wait.bck", "bck/ma_walk.bck", "bck/ma_run2.bck", "bck/ma_jump.bck",
        };

        static bool Complete(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return false;
            foreach (var f in PiantaFiles)
                if (!File.Exists(Path.Combine(dir, f))) return false;
            return true;
        }

        /// Resolves the Sunshine cache (the folder the Mario plugin extracted from the user's own
        /// Sunshine disc). Empty `configured`: $SMS_ASSETS, then beside this plugin or the Mario
        /// plugin's pointer file.
        public static void Discover(string configured, string here)
        {
            sunshineDir = Resolve(configured, here);
            ready = Complete(sunshineDir);
        }

        static string Resolve(string configured, string here)
        {
            if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
            string env = Environment.GetEnvironmentVariable("SMS_ASSETS");
            if (!string.IsNullOrWhiteSpace(env) && Complete(env.Trim())) return env.Trim();
            foreach (var rel in new[] { "../MarioTest/assets", "assets", "MarioTest/assets" })
            {
                var p = Path.GetFullPath(Path.Combine(here, rel));
                if (Complete(p)) return p;
            }
            var pointer = Path.GetFullPath(Path.Combine(here, "../MarioTest/assets_dir.txt"));
            if (File.Exists(pointer))
            {
                var target = File.ReadAllText(pointer).Trim();
                if (Complete(target)) return target;
            }
            return null;
        }

        public static OlimarSkinSpec Spec(uint id) => id < Specs.Length && Specs[id] != null ? Specs[id] : Specs[Olimar];

        /// The native skin id for a loadout skin index (Olimar for anything unregistered).
        static uint FromIndex(uint index) => index < loadout.Count ? loadout[(int)index].Id : Olimar;

        /// The skin `body`'s loadout selected (a native skin id; Olimar when none).
        public static uint Selected(CharacterBody body)
        {
            if (loadout.Count < 2 || !body) return Olimar;
            uint skin = body.skinIndex;
            if (body.master && body.master.loadout != null && body.bodyIndex != BodyIndex.None)
                skin = Math.Max(skin, body.master.loadout.bodyLoadoutManager.GetSkinIndex(body.bodyIndex));
            return FromIndex(skin);
        }

        /// The skin the loadout has selected for Olimar's body, for scenes with no live body yet
        /// (the character-select display).
        public static uint Selected()
        {
            if (loadout.Count < 2) return Olimar;
            var index = BodyCatalog.FindBodyIndex("OlimarTestBody");
            if (index == BodyIndex.None) return Olimar;
            foreach (var user in LocalUserManager.readOnlyLocalUsersList)
                if (user.userProfile != null)
                    return FromIndex(user.userProfile.loadout.bodyLoadoutManager.GetSkinIndex(index));
            return Olimar;
        }

        /// Loads the Pianta into a new view; false when Sunshine's files are missing. Call between
        /// pik_view_new and the ModelView's creation (the model table is fixed then).
        public static bool LoadInto(IntPtr view)
        {
            if (!ready) return false;
            int model = Native.pik_view_load_pianta(view, Native.CString(sunshineDir), 0, uint.MaxValue);
            if (model < 0)
            {
                Plugin.Log.LogWarning($"The Pianta skin did not load from {sunshineDir}");
                return false;
            }
            return true;
        }

        /// Whether Captain Pikachu can be drawn (Melee's Pikachu and Ice Climbers models are in the Melee cache).
        public static bool PikachuAvailable => pikachuReady;

        /// Finds the Melee cache the Melee plugin keeps (`configured`, $SSBM_ASSETS, `../Melee/assets`, its
        /// `assets_dir.txt`) and, if it lacks Pikachu's and the Ice Climbers' models, extracts them once from the
        /// user's Melee disc (`configuredDisc`, or one beside the plugin or the Pikmin assets). Without either
        /// the skin is not offered.
        public static void PreparePikachu(string configured, string configuredDisc, string here, string assetsParent)
        {
            try
            {
                meleeDir = ResolveMelee(configured, here);
                if (meleeDir == null)
                {
                    Plugin.Log.LogInfo("Captain Pikachu: no Melee cache found (set Skins.MeleeAssets), so he is not offered");
                    return;
                }
                if (Native.pik_ssbm_ice_climbers_complete(Native.CString(meleeDir)) == 0)
                {
                    string disc = configuredDisc?.Trim();
                    if (string.IsNullOrEmpty(disc) || !File.Exists(disc))
                    {
                        var found = new byte[1024];
                        disc = Native.pik_ssbm_find_disc(Native.CString(here + ";" + assetsParent), found, (uint)found.Length) != 0
                            ? Native.Utf8(found) : null;
                    }
                    if (string.IsNullOrEmpty(disc))
                    {
                        Plugin.Log.LogInfo("Captain Pikachu: the Ice Climbers models are not extracted and no Melee disc was found (set Skins.MeleeDisc), so he is not offered");
                        return;
                    }
                    Plugin.Log.LogInfo($"Captain Pikachu: extracting the Ice Climbers models from {disc} into {meleeDir}...");
                    if (Native.pik_ssbm_extract_ice_climbers(Native.CString(disc), Native.CString(meleeDir)) != 0)
                    {
                        Plugin.Log.LogWarning("Captain Pikachu: extraction from the Melee disc failed (it must be GALE01 revision 2), so he is not offered");
                        return;
                    }
                }
                pikachuReady = Native.pik_ssbm_ice_climbers_complete(Native.CString(meleeDir)) != 0;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Captain Pikachu: {e.Message}");
            }
        }

        static string ResolveMelee(string configured, string here)
        {
            if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
            string env = Environment.GetEnvironmentVariable("SSBM_ASSETS");
            if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env.Trim())) return env.Trim();
            foreach (var rel in new[] { "../Melee/assets", "assets", "Melee/assets" })
            {
                var p = Path.GetFullPath(Path.Combine(here, rel));
                if (File.Exists(Path.Combine(p, "PlCo.dat"))) return p;
            }
            var pointer = Path.GetFullPath(Path.Combine(here, "../Melee/assets_dir.txt"));
            if (File.Exists(pointer))
            {
                var target = File.ReadAllText(pointer).Trim();
                if (!string.IsNullOrWhiteSpace(target) && Directory.Exists(target)) return target;
            }
            return null;
        }

        /// Loads Pikachu and the Ice Climbers into a new view (before the model table is read); false when unavailable.
        public static bool LoadPikachuInto(IntPtr view)
        {
            if (!pikachuReady) return false;
            if (Native.pik_view_load_pikachu(view, Native.CString(meleeDir)) < 0)
            {
                Plugin.Log.LogWarning($"Captain Pikachu did not load from {meleeDir}");
                return false;
            }
            return true;
        }

        /// Whether Louie can be drawn (Pikmin 2's player files are extracted).
        public static bool LouieAvailable => louieReady;

        /// Finds the Pikmin 2 disc and extracts Louie's files from it once, into `cacheDir` (a few small
        /// archives, so on the calling thread). Without a disc Louie is simply not offered. `configuredDisc`
        /// is the config's image path; empty looks beside this plugin and the Pikmin assets.
        public static void PrepareLouie(string configuredDisc, string cacheDir, string here, string assetsParent)
        {
            louieDir = cacheDir;
            try
            {
                if (Native.pik_p2_complete(Native.CString(cacheDir)) == 0)
                {
                    string disc = configuredDisc?.Trim();
                    if (string.IsNullOrEmpty(disc) || !File.Exists(disc))
                    {
                        var found = new byte[1024];
                        disc = Native.pik_p2_find_disc(Native.CString(here + ";" + assetsParent), found, (uint)found.Length) != 0
                            ? Native.Utf8(found) : null;
                    }
                    if (string.IsNullOrEmpty(disc))
                    {
                        Plugin.Log.LogInfo("Louie: no Pikmin 2 disc found (set Skins.Pikmin2Disc), so he is not offered");
                        return;
                    }
                    Plugin.Log.LogInfo($"Louie: extracting Pikmin 2's player files from {disc} into {cacheDir}...");
                    if (Native.pik_p2_extract(Native.CString(disc), Native.CString(cacheDir)) != 0)
                    {
                        Plugin.Log.LogWarning("Louie: extraction from the Pikmin 2 disc failed, so he is not offered");
                        return;
                    }
                }
                louieReady = Native.pik_p2_complete(Native.CString(cacheDir)) != 0;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Louie: {e.Message}");
            }
        }

        /// Loads Louie into a new view (before the model table is read); false when he is not available.
        public static bool LoadLouieInto(IntPtr view)
        {
            if (!louieReady) return false;
            if (Native.pik_view_load_louie(view, Native.CString(louieDir)) < 0)
            {
                Plugin.Log.LogWarning($"Louie did not load from {louieDir}");
                return false;
            }
            return true;
        }

        /// The Pianta's height to the top of his head, in sim units.
        public static float PiantaUnits() => Mathf.Max(0.2f, Plugin.PiantaHeight.Value) / Mathf.Max(1e-4f, UnityHost.Scale);

        /// Registers Olimar's skins on the body in place of Commando's (Olimar default, then the Pianta
        /// when his files are found) and starts the first-run Portrait jobs that render their icons.
        public static void Register(GameObject body)
        {
            var model = body.GetComponent<ModelLocator>().modelTransform;
            var controller = model.GetComponent<ModelSkinController>();
            if (!controller) controller = model.gameObject.AddComponent<ModelSkinController>();
            var character = model.GetComponent<CharacterModel>();
            loadout.Clear();
            var defs = new List<SkinDef>();
            foreach (var spec in Specs)
            {
                if (spec == null) continue;
                spec.Registered = spec.Id == Olimar || (spec.Id == Pianta && ready) || (spec.Id == Louie && louieReady) || (spec.Id == Pikachu && pikachuReady);
                if (!spec.Registered) continue;
                spec.Def = Def(spec.Name, spec.Token, model, character,
                    Icon(spec.Id == Olimar ? new Color(0.95f, 0.75f, 0.2f) : spec.Id == Louie ? new Color(0.2f, 0.7f, 0.3f) : spec.Id == Pikachu ? new Color(1f, 0.85f, 0.1f) : new Color(0.3f, 0.7f, 0.3f)));
                if (spec.Id == Pianta && !Plugin.PiantaUnlocked.Value)
                {
                    // Held back until earned (the plan: Mithrix's defeat, escaping the planet). Nothing grants it yet.
                    var unlock = ScriptableObject.CreateInstance<UnlockableDef>();
                    unlock.cachedName = "Skins.Olimar.Pianta";
                    unlock.nameToken = spec.Def.nameToken;
                    unlock.getHowToUnlockString = () => "Defeat Mithrix and escape the planet.";
                    unlock.getUnlockedString = () => "Olimar's Pianta skin";
                    spec.Def.unlockableDef = unlock;
                }
                loadout.Add(spec);
                defs.Add(spec.Def);
            }
            // The Commando placeholders are gone: only these remain.
            controller.skins = defs.ToArray();
            foreach (var spec in loadout)
            {
                var s = spec;
                Portrait.Request(body, () => OlimarDisplay.Get(s.Id), tex => s.Def.icon = Sprite(tex),
                    d => d.kind != Native.KindCaptain, framing: PortraitFraming.ThreeQuarter);
            }
        }

        static SkinDef Def(string name, string nameToken, Transform root, CharacterModel character, Sprite icon)
        {
            var skin = ScriptableObject.CreateInstance<SkinDef>();
            skin.name = name;
            skin.nameToken = nameToken;
            skin.rootObject = root.gameObject;
            skin.baseSkins = Array.Empty<SkinDef>();
            skin.icon = icon;
            var p = ScriptableObject.CreateInstance<SkinDefParams>();
            p.name = name + "Params";
            p.rendererInfos = character.baseRendererInfos;
            p.gameObjectActivations = Array.Empty<SkinDefParams.GameObjectActivation>();
            p.meshReplacements = Array.Empty<SkinDefParams.MeshReplacement>();
            p.projectileGhostReplacements = Array.Empty<SkinDefParams.ProjectileGhostReplacement>();
            p.minionSkinReplacements = Array.Empty<SkinDefParams.MinionSkinReplacement>();
            p.lightReplacements = Array.Empty<CharacterModel.LightInfo>();
            skin.skinDefParams = p;
            skin.optimizedSkinDefParams = p;
            return skin;
        }

        static Sprite Sprite(Texture2D texture) =>
            UnityEngine.Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));

        static Sprite Icon(Color color)
        {
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var pixels = new Color32[64];
            var suit = (Color32)color;
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) pixels[y * 8 + x] = suit;
            texture.SetPixelData(pixels, 0);
            texture.Apply(false, true);
            return Sprite(texture);
        }

        public static void Tokens(Language language)
        {
            foreach (var spec in Specs)
                if (spec != null) language.SetStringByToken(spec.Token, spec.Text);
        }
    }
}
