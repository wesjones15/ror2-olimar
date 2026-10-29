using System;
using OlimarTest;
using RoR2;
using RoR2.ContentManagement;

static class Program
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static LocalUser Player(string token, uint id) => new LocalUser {
        cachedBody = new CharacterBody { baseNameToken=token }, userProfile = new UserProfile(),
        currentNetworkUser = new NetworkUser { netId = new NetId { Value=id } }
    };
    static void Main()
    {
        var pack = new ContentPack(); OlimarSkinUnlocks.Register(pack); OlimarSkinUnlocks.Install();
        Check(pack.unlockableDefs.Count == 3 && OlimarSkinUnlocks.For(0) == null, "Register all three optional keys, leaving default Olimar open");
        var pianta=OlimarSkinUnlocks.For(OlimarSkin.Pianta); var pikachu=OlimarSkinUnlocks.For(OlimarSkin.Pikachu); var louie=OlimarSkinUnlocks.For(OlimarSkin.Louie);
        Check(pianta.cachedName == "Skins.Olimar.Pianta", "Preserve existing Pianta profile key");
        var olimar=Player("OLIMARTEST_BODY_NAME", 1); var rival=Player("OLIMARTEST_BODY_NAME", 2); var mario=Player("MARIOTEST_BODY_NAME", 3);
        LocalUserManager.readOnlyLocalUsersList.AddRange(new[] { olimar, rival, mario });
        TeleporterInteraction.Charge();
        Check(olimar.userProfile.Saves == 0, "No run must mean no reward");
        Run.instance = new Run(); TeleporterInteraction.Charge(); TeleporterInteraction.Charge();
        Check(olimar.userProfile.HasUnlockable(pianta) && !olimar.userProfile.HasUnlockable(pikachu), "Stage 1 grants Pianta only");
        Run.instance.stageClearCount=1; TeleporterInteraction.Charge();
        Check(olimar.userProfile.HasUnlockable(pikachu) && olimar.userProfile.Saves == 2, "Stage 2 grants Captain Pikachu once");
        Check(mario.userProfile.Saves == 0, "Wrong survivor earned Olimar skins");
        OlimarSkinUnlocks.CheckParts(id => 29);
        Check(!olimar.userProfile.HasUnlockable(louie), "29 parts unlocked Louie");
        OlimarSkinUnlocks.CheckParts(id => id == 1 ? 30 : 29);
        Check(olimar.userProfile.HasUnlockable(louie) && !rival.userProfile.HasUnlockable(louie), "Competitive parts leaked to a rival");
        OlimarSkinUnlocks.CheckParts(id => 30); OlimarSkinUnlocks.CheckParts(id => 30);
        Check(rival.userProfile.HasUnlockable(louie) && olimar.userProfile.Saves == 3 && rival.userProfile.Saves == 3, "Co-op crew reward must persist exactly once");
        Check(mario.userProfile.Saves == 0, "Ship Repair key leaked to another survivor");
        olimar.userProfile = new UserProfile();
        olimar.cachedMaster = new CharacterMaster { bodyPrefab = new UnityEngine.GameObject { Component=olimar.cachedBody } }; olimar.cachedBody=null;
        OlimarSkinUnlocks.CheckParts(id => 30);
        Check(olimar.userProfile.HasUnlockable(louie), "Dead crew member lost the 30-part reward");
        Console.WriteLine("PASS: Olimar stage 1/2 costumes, 30-part Louie, shared and competitive ownership, wrong survivors, dead players and persistent saves.");
    }
}
