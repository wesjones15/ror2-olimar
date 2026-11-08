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
        /// The Pikmin 2 cache Louie comes from, and the Melee cache Captain Pikachu does, when each is ready.
        public static string LouieDir => louieReady ? louieDir : null;
        public static string MeleeDir => pikachuReady ? meleeDir : null;
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

        /// SharedFiles' Sunshine folder (filled by the Mario plugin from the player's own Sunshine disc); the Pianta
        /// is offered when his files are there.
        public static void Discover()
        {
            sunshineDir = SharedFiles.Assets(SharedFiles.Sunshine);
            ready = Complete(sunshineDir);
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
            // The pad being drawn's player in a lobby, else the local player.
            return FromIndex(DisplayModel.SelectedSkin("OlimarTestBody"));
        }

        /// Loads the Pianta into a new view; false when Sunshine's files are missing. Call between
        /// pik_view_new and the ModelView's creation (the model table is fixed then).
        public static bool LoadInto(IntPtr view)
        {
            if (!ready) return false;
            int model = Native.pik_view_load_pianta(view, Native.CString(sunshineDir), 0, Plugin.PiantaColour.Value < 0 ? uint.MaxValue : (uint)Plugin.PiantaColour.Value);
            if (model < 0)
            {
                Plugin.Log.LogWarning($"The Pianta skin did not load from {sunshineDir}");
                return false;
            }
            return true;
        }

        /// Whether Captain Pikachu can be drawn (Melee's Pikachu and Ice Climbers models are in the Melee cache).
        public static bool PikachuAvailable => pikachuReady;

        /// SharedFiles' Melee folder; if it lacks Pikachu's and the Ice Climbers' models, extracts them once from a
        /// Melee disc in put_games_here (the Melee plugin isn't needed). Without either the skin is not offered.
        /// Versions of the extractors' output (SharedFiles extracted.txt): bump one when its extractor writes an
        /// existing file differently, so installed caches are extracted again.
        const int IceClimbersExtractorVersion = 1, P2ExtractorVersion = 1;

        public static void PreparePikachu()
        {
            try
            {
                meleeDir = SharedFiles.Assets(SharedFiles.Melee);
                bool Complete() => Native.pik_ssbm_ice_climbers_complete(Native.CString(meleeDir)) != 0;
                bool Current() => SharedFiles.Current(SharedFiles.Melee, "iceclimbers", IceClimbersExtractorVersion);
                if (!Complete() || !Current())
                {
                    using (SharedFiles.Lock(SharedFiles.Melee))
                    {
                        // Another plugin may have just extracted them.
                        bool complete = Complete();
                        if (!complete || !Current())
                        {
                            var found = new byte[1024];
                            string disc = Native.pik_ssbm_find_disc(Native.CString(SharedFiles.SearchList()), found, (uint)found.Length) != 0
                                ? Native.Utf8(found) : null;
                            if (string.IsNullOrEmpty(disc))
                            {
                                // Files from an older extractor are still used as they are.
                                if (!complete)
                                    Plugin.Log.LogInfo("Captain Pikachu: no Melee disc found, so he is not offered. " +
                                                       SharedFiles.MissingDisc("Super Smash Bros. Melee (USA, revision 1.02)"));
                            }
                            else
                            {
                                Plugin.Log.LogInfo($"Captain Pikachu: extracting the Ice Climbers and Pikachu from {disc} into {meleeDir}...");
                                if (Native.pik_ssbm_extract_ice_climbers(Native.CString(disc), Native.CString(meleeDir)) != 0)
                                    Plugin.Log.LogWarning("Captain Pikachu: extraction from the Melee disc failed (it must be GALE01 revision 2)");
                                else if (Complete())
                                    SharedFiles.MarkCurrent(SharedFiles.Melee, "iceclimbers", IceClimbersExtractorVersion);
                            }
                        }
                    }
                }
                pikachuReady = Complete();
                if (!pikachuReady) Plugin.Log.LogInfo("Captain Pikachu: his Melee files aren't complete, so he is not offered");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Captain Pikachu: {e.Message}");
            }
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
        public static bool LouieAvailable => true; // Gameplay and the blue stand-in need no Pikmin 2 files.
        public static bool UsesStandIn(uint skin) => !Plugin.AssetsReady || skin == Louie && !louieReady;
        public static Color StandInColor(uint skin) => skin == Louie ? Color.blue : new Color(0.95f, 0.75f, 0.2f);
        public static string OverviewNote()
        {
            var games = new List<string>();
            if (!Plugin.AssetsReady) games.Add("Pikmin");
            if (Selected() == Louie && !louieReady) games.Add("Pikmin 2");
            return AssetOverview.Missing(games.ToArray());
        }

        /// The Pikmin 2 disc for Louie, the P2 enemies and the treasures: the first one `pik_p2_find_disc` finds in
        /// `searchDirs` (SharedFiles' disc folders, `;`-separated). Null when there is none.
        internal static string FindPikmin2Disc(string searchDirs)
        {
            var found = new byte[1024];
            return Native.pik_p2_find_disc(Native.CString(searchDirs), found, (uint)found.Length) != 0
                ? Native.Utf8(found) : null;
        }

        /// Finds the Pikmin 2 disc and extracts Louie's files from it once, into `cacheDir` (a few small
        /// archives, so on the calling thread). Without a disc Louie uses a blue stand-in.
        public static void PrepareLouie(string cacheDir, string searchDirs)
        {
            louieDir = cacheDir;
            try
            {
                bool complete = Native.pik_p2_complete(Native.CString(cacheDir)) != 0;
                if (!complete || !SharedFiles.Current(SharedFiles.Pikmin, "p2", P2ExtractorVersion))
                {
                    string disc = FindPikmin2Disc(searchDirs);
                    if (string.IsNullOrEmpty(disc))
                    {
                        // Files from an older extractor are still used as they are.
                        if (!complete)
                        {
                            Plugin.Log.LogInfo("Louie: no Pikmin 2 disc found; using a blue stand-in. " + SharedFiles.MissingDisc("Pikmin 2"));
                            return;
                        }
                    }
                    else
                    {
                        Plugin.Log.LogInfo($"Louie: extracting Pikmin 2's player files from {disc} into {cacheDir}...");
                        if (Native.pik_p2_extract(Native.CString(disc), Native.CString(cacheDir)) != 0)
                            Plugin.Log.LogWarning("Louie: extraction from the Pikmin 2 disc failed");
                        else if (Native.pik_p2_complete(Native.CString(cacheDir)) != 0)
                            SharedFiles.MarkCurrent(SharedFiles.Pikmin, "p2", P2ExtractorVersion);
                    }
                }
                louieReady = Native.pik_p2_complete(Native.CString(cacheDir)) != 0;
                if (!louieReady) Plugin.Log.LogInfo("Louie: Pikmin 2's player files aren't complete; using a blue stand-in");
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

        static bool pikmin2Ready;

        /// Whether Purple and White Pikmin's Pikmin 2 bodies loaded (from the p2 asset folder even without Louie).
        public static bool Pikmin2Available => pikmin2Ready;

        /// Loads Purple and White Pikmin (Pikmin 2's bodies) into a new view, from Louie's cache or the
        /// shared p2 asset folder; without them they are drawn as tinted Pikmin 1 bodies.
        public static bool LoadPikmin2Into(IntPtr view)
        {
            string[] dirs =
            {
                louieReady ? louieDir : null,
                string.IsNullOrWhiteSpace(Plugin.AssetsDir) ? null : Path.Combine(Plugin.AssetsDir, "p2"),
            };
            foreach (var dir in dirs)
            {
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) continue;
                if (Native.pik_view_load_pikmin2(view, Native.CString(dir)) >= 0)
                {
                    pikmin2Ready = true;
                    return true;
                }
            }
            Plugin.Log.LogWarning("Purple and White Pikmin did not load (no piki_p2_black/white in the p2 assets or Louie cache)");
            return false;
        }

        /// The Pianta's height to the top of his head, in sim units.
        public static float PiantaUnits() => OlimarSimHeight * Mathf.Max(0.2f, Plugin.PiantaHeightRatio.Value);

        /// Olimar's height in sim units (his disc model), which the ratios are measured against.
        const float OlimarSimHeight = 25.1f;

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
                spec.Registered = spec.Id == Olimar || spec.Id == Louie || (spec.Id == Pianta && ready) || (spec.Id == Pikachu && pikachuReady);
                if (!spec.Registered) continue;
                spec.Def = Def(spec.Name, spec.Token, model, character,
                    Icon(spec.Id == Olimar || spec.Id == Louie ? StandInColor(spec.Id) : spec.Id == Pikachu ? new Color(1f, 0.85f, 0.1f) : new Color(0.3f, 0.7f, 0.3f)));
                spec.Def.unlockableDef = OlimarSkinUnlocks.For(spec.Id);
                loadout.Add(spec);
                defs.Add(spec.Def);
            }
            // The Commando placeholders are gone: only these remain.
            controller.skins = defs.ToArray();
            foreach (var spec in loadout)
            {
                var s = spec;
                Portrait.Request(body, () => OlimarDisplay.Get(s.Id), tex => s.Def.icon = Sprite(tex),
                    d => d.kind != Native.KindCaptain, framing: PortraitFraming.ThreeQuarter,
                    assetsDir: () => Plugin.AssetsDir, cacheKey: s.Name);
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
