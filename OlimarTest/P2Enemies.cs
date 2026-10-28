using System;

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

        /// Finds the Pikmin 2 disc (the config's image path, else beside this plugin and the Pikmin assets) and extracts
        /// the enemies' files from it once into `cacheDir` (a few hundred small files, so on the calling thread).
        public static void Prepare(string configuredDisc, string cacheDir, string here, string assetsParent)
        {
            try
            {
                if (Native.pik_p2_enemies_complete(Native.CString(cacheDir)) == 0)
                {
                    string disc = configuredDisc?.Trim();
                    if (string.IsNullOrEmpty(disc) || !System.IO.File.Exists(disc))
                    {
                        var found = new byte[1024];
                        disc = Native.pik_p2_find_disc(Native.CString(here + ";" + assetsParent), found, (uint)found.Length) != 0
                            ? Native.Utf8(found) : null;
                    }
                    if (string.IsNullOrEmpty(disc))
                    {
                        Plugin.Log.LogInfo("Pikmin 2 enemies: no Pikmin 2 disc found (set Skins.Pikmin2Disc), so they are not offered");
                        return;
                    }
                    Plugin.Log.LogInfo($"Pikmin 2 enemies: extracting their files from {disc} into {cacheDir}...");
                    if (Native.pik_p2_enemies_extract(Native.CString(disc), Native.CString(cacheDir)) != 0)
                    {
                        Plugin.Log.LogWarning("Pikmin 2 enemies: extraction from the Pikmin 2 disc failed, so they are not offered");
                        return;
                    }
                }
                Ready = Native.pik_p2_enemies_complete(Native.CString(cacheDir)) != 0;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Pikmin 2 enemies: {e.Message}");
            }
        }
    }
}
