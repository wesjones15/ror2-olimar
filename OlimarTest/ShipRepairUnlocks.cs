using RoR2;

namespace OlimarTest
{
    /// Winning Ship Repair as Olimar unlocks Mario (his plugin registers the unlockable by this name; without his
    /// plugin there is nothing to grant). Each machine grants it to its own local players, so clients get it too.
    internal static class ShipRepairUnlocks
    {
        const string MarioUnlock = "Characters.MarioTest";
        static string grantedRun;

        internal static void OnWin(string runId)
        {
            if (grantedRun == runId) return;
            grantedRun = runId;
            var def = UnlockableCatalog.GetUnlockableDef(MarioUnlock);
            if (!def) return;
            foreach (var user in LocalUserManager.readOnlyLocalUsersList)
            {
                var body = user.cachedBody;
                if (!body || user.userProfile == null || !body.GetComponent<OlimarDriver>()) continue;
                if (user.userProfile.HasUnlockable(def)) continue;
                user.userProfile.GrantUnlockable(def);
                user.userProfile.RequestEventualSave();
                Chat.AddMessage("Mario unlocked.");
                Plugin.Log.LogInfo("Ship Repair won as Olimar: Mario unlocked");
            }
        }
    }
}
