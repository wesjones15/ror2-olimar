using System;
using Ror2Common;

namespace OlimarTest
{
    // Pikmin 2's enemies (kinds 27 on in TekiRoster) come from the user's own Pikmin 2 disc: their models, clips,
    // textures and parameter text are extracted once into the Pikmin 2 cache folder (beside Louie's files and the
    // treasures'), with a marker of their own so the others are never redone. Without a disc they are simply not
    // spawned (TekiCatalog skips a kind whose files are missing).
    internal static class P2Enemies
    {
        /// Whether the enemies' files are in the cache.
        public static bool Ready { get; private set; }

        /// Finds the Pikmin 2 disc (`OlimarSkin.FindPikmin2Disc`) and extracts
        /// the enemies' files from it once into `cacheDir` (a few hundred small files, so on the calling thread).
        /// Version of `pik_p2_enemies_extract`'s output (SharedFiles extracted.txt, set "p2enemies"): bump it when
        /// the extractor writes an existing file differently.
        const int ExtractorVersion = 1;

        public static void Prepare(string cacheDir, string searchDirs)
        {
            try
            {
                bool complete = Native.pik_p2_enemies_complete(Native.CString(cacheDir)) != 0;
                if (!complete || !SharedFiles.Current(SharedFiles.Pikmin, "p2enemies", ExtractorVersion))
                {
                    string disc = OlimarSkin.FindPikmin2Disc(searchDirs);
                    if (string.IsNullOrEmpty(disc))
                    {
                        // Files from an older extractor are still used as they are.
                        if (!complete)
                        {
                            Plugin.Log.LogInfo("Pikmin 2 enemies: no Pikmin 2 disc found, so they are not offered. " + SharedFiles.MissingDisc("Pikmin 2"));
                            return;
                        }
                    }
                    else
                    {
                        Plugin.Log.LogInfo($"Pikmin 2 enemies: extracting their files from {disc} into {cacheDir}...");
                        if (Native.pik_p2_enemies_extract(Native.CString(disc), Native.CString(cacheDir)) != 0)
                            Plugin.Log.LogWarning("Pikmin 2 enemies: extraction from the Pikmin 2 disc failed");
                        else if (Native.pik_p2_enemies_complete(Native.CString(cacheDir)) != 0)
                            SharedFiles.MarkCurrent(SharedFiles.Pikmin, "p2enemies", ExtractorVersion);
                    }
                }
                Ready = Native.pik_p2_enemies_complete(Native.CString(cacheDir)) != 0;
                if (!Ready) Plugin.Log.LogWarning("Pikmin 2 enemies: their files aren't complete, so they are not offered");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Pikmin 2 enemies: {e.Message}");
            }
        }
    }
}
