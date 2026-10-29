using System;
using RoR2;
using RoR2.ContentManagement;
using Ror2Common;

namespace OlimarTest
{
    internal static class OlimarSkinUnlocks
    {
        static UnlockableDef louie, pianta, pikachu;

        internal static void Register(ContentPack pack)
        {
            // Register keys even when an optional disc cache is absent, so earned
            // progress survives installing that skin's assets later.
            louie = SkinUnlocks.Create(pack, "Skins.Olimar.Louie", "OLIMARTEST_SKIN_LOUIE",
                "Collect all 30 ship parts in Ship Repair as Olimar.", "Louie unlocked.");
            pianta = SkinUnlocks.Create(pack, "Skins.Olimar.Pianta", "OLIMARTEST_SKIN_PIANTA",
                "Complete stage 1's teleporter event as Olimar.", "Pianta unlocked.");
            pikachu = SkinUnlocks.Create(pack, "Skins.Olimar.Pikachu", "OLIMARTEST_SKIN_PIKACHU",
                "Complete stage 2's teleporter event as Olimar.", "Captain Pikachu unlocked.");
        }

        internal static UnlockableDef For(uint skin) => skin == OlimarSkin.Louie ? louie
            : skin == OlimarSkin.Pianta ? pianta : skin == OlimarSkin.Pikachu ? pikachu : null;

        internal static void Install() => TeleporterInteraction.onTeleporterChargedGlobal += Charged;

        static bool IsOlimar(LocalUser user) => SkinUnlocks.IsPlaying(user, "OLIMARTEST_BODY_NAME");

        internal static void Charged(TeleporterInteraction teleporter)
        {
            if (!Run.instance) return;
            int stage = Run.instance.stageClearCount + 1;
            foreach (var user in LocalUserManager.readOnlyLocalUsersList)
            {
                if (!IsOlimar(user)) continue;
                if (stage >= 1) Grant(user, pianta);
                if (stage >= 2) Grant(user, pikachu);
            }
        }

        // Each machine awards its own profiles from authoritative Ship Repair
        // progress: shared crew parts in co-op, each racer's own parts otherwise.
        internal static void CheckParts(Func<uint, int> partsFor)
        {
            foreach (var user in LocalUserManager.readOnlyLocalUsersList)
            {
                if (!IsOlimar(user)) continue;
                uint id = user.currentNetworkUser ? user.currentNetworkUser.netId.Value : 0;
                if (partsFor(id) >= 30) Grant(user, louie);
            }
        }

        static void Grant(LocalUser user, UnlockableDef def)
        {
            if (!SkinUnlocks.Grant(user, def)) return;
            Chat.AddMessage(def.getUnlockedString());
            Plugin.Log.LogInfo(def.getUnlockedString());
        }
    }
}
