using System.IO;
using Ror2Common;
using UnityEngine;
namespace OlimarTest
{
    internal static class Pikmin2Session
    {
        static OptionalContentSession session;
        static uint? cached;
        internal static uint LocalMask() => Local();
        static uint Local()
        {
            if(OptionalAssets.BuiltinOnly || !Plugin.AssetsSettled)return 0;
            if(cached.HasValue)return cached.Value;
            var dir=Native.CString(Path.Combine(Plugin.AssetsDir,"p2"));
            return (cached = Native.pik_p2_complete(dir)!=0 && Native.pik_p2_enemies_complete(dir)!=0 && Native.pik_p2_treasures_complete(dir)!=0 ? 1u : 0u).Value;
        }
        internal static bool Enabled => session != null ? session.Has(1) : Local()!=0;
        internal static void Install(GameObject owner) => session=OptionalContentSession.Attach(owner,0x4F30,"Pikmin 2",Local);
        internal static bool EnemyAllowed(byte kind) => kind < 27 || kind == 56 || Enabled;
    }
}
