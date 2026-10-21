namespace OlimarTest
{
    // Every Pikmin enemy the plugin can spawn: the disc's kind (libpikmin-present teki::KIND_TABLE order), its
    // tier and the vanilla RoR2 monster whose AI, skills and hurt boxes it borrows. Pikmin 1's own strategies
    // aren't ported; each enemy fights the way its template does, in the disc's body, with the disc's life,
    // speed, animations, sounds and death effects. Small ones are common, big ones rare and always drop a part.
    internal enum TekiHull { Human, Golem }

    internal sealed class TekiSpec
    {
        public byte Kind;
        /// Language token fragment, and the config key stem.
        public string Token, Display;
        public bool Big, Flying;
        public string Body, Master;
        public int Weight, Cost;
        public TekiHull Hull;

        public TekiSpec(byte kind, string token, string display, bool big, bool flying, string body, string master, int weight, int cost, TekiHull hull)
        {
            Kind = kind; Token = token; Display = display; Big = big; Flying = flying;
            Body = body; Master = master; Weight = weight; Cost = cost; Hull = hull;
        }
    }

    internal static class TekiRoster
    {
        const string Base = "RoR2/Base/", Dlc1 = "RoR2/DLC1/";

        // Beetle: a headbutting bug. Beetle Guard: slams. Bison: charges. Clay Grenadier: lobs tar. Elder Lemurian:
        // breathes fire. Blind Pest/Vermin: small biters, one on the wing. Jellyfish: floats and novas.
        public static readonly TekiSpec[] All =
        {
            new TekiSpec(0, "DWARF", "Dwarf Bulborb", false, false, Base + "Beetle/BeetleBody.prefab", Base + "Beetle/BeetleMaster.prefab", 6, 25, TekiHull.Human),
            new TekiSpec(1, "SPOTTY", "Spotty Bulborb", true, false, Base + "BeetleGuard/BeetleGuardBody.prefab", Base + "BeetleGuard/BeetleGuardMaster.prefab", 2, 250, TekiHull.Golem),
            new TekiSpec(2, "DWARFORANGE", "Dwarf Bulbear", false, false, Base + "Beetle/BeetleBody.prefab", Base + "Beetle/BeetleMaster.prefab", 5, 25, TekiHull.Human),
            new TekiSpec(3, "SPOTTYORANGE", "Spotty Bulbear", true, false, Base + "BeetleGuard/BeetleGuardBody.prefab", Base + "BeetleGuard/BeetleGuardMaster.prefab", 1, 300, TekiHull.Golem),
            new TekiSpec(4, "WOLLYWOG", "Wollywog", true, false, Base + "Bison/BisonBody.prefab", Base + "Bison/BisonMaster.prefab", 2, 200, TekiHull.Golem),
            new TekiSpec(5, "WOLLYWOGYELLOW", "Yellow Wollywog", true, false, Base + "Bison/BisonBody.prefab", Base + "Bison/BisonMaster.prefab", 1, 250, TekiHull.Golem),
            new TekiSpec(6, "CANNONBEETLE", "Armored Cannon Beetle", true, false, Dlc1 + "ClayGrenadier/ClayGrenadierBody.prefab", Dlc1 + "ClayGrenadier/ClayGrenadierMaster.prefab", 1, 300, TekiHull.Golem),
            new TekiSpec(7, "BLOWHOG", "Fiery Blowhog", true, false, Base + "LemurianBruiser/LemurianBruiserBody.prefab", Base + "LemurianBruiser/LemurianBruiserMaster.prefab", 2, 250, TekiHull.Golem),
            new TekiSpec(8, "SNITCHBUG", "Swooping Snitchbug", false, true, Dlc1 + "FlyingVermin/FlyingVerminBody.prefab", Dlc1 + "FlyingVermin/FlyingVerminMaster.prefab", 3, 30, TekiHull.Human),
            new TekiSpec(9, "SHEARGRUBF", "Female Sheargrub", false, false, Dlc1 + "Vermin/VerminBody.prefab", Dlc1 + "Vermin/VerminMaster.prefab", 4, 20, TekiHull.Human),
            new TekiSpec(10, "SHEARGRUBM", "Male Sheargrub", false, false, Dlc1 + "Vermin/VerminBody.prefab", Dlc1 + "Vermin/VerminMaster.prefab", 4, 20, TekiHull.Human),
            new TekiSpec(11, "SHEARWIG", "Shearwig", false, false, Dlc1 + "Vermin/VerminBody.prefab", Dlc1 + "Vermin/VerminMaster.prefab", 2, 25, TekiHull.Human),
            new TekiSpec(12, "WOGPOLE", "Wogpole", false, false, Dlc1 + "AcidLarva/AcidLarvaBody.prefab", Dlc1 + "AcidLarva/AcidLarvaMaster.prefab", 3, 25, TekiHull.Human),
            new TekiSpec(13, "PUFFSTOOL", "Puffstool", true, false, Base + "HermitCrab/HermitCrabBody.prefab", Base + "HermitCrab/HermitCrabMaster.prefab", 1, 250, TekiHull.Golem),
            new TekiSpec(14, "PUFFYBLOWHOG", "Puffy Blowhog", true, true, Base + "Jellyfish/JellyfishBody.prefab", Base + "Jellyfish/JellyfishMaster.prefab", 1, 250, TekiHull.Golem),
            new TekiSpec(15, "MAMUTA", "Mamuta", true, false, Base + "Golem/GolemBody.prefab", Base + "Golem/GolemMaster.prefab", 1, 300, TekiHull.Golem),
            new TekiSpec(16, "PROGG", "Smoky Progg", true, false, Base + "Parent/ParentBody.prefab", Base + "Parent/ParentMaster.prefab", 1, 400, TekiHull.Golem),
            new TekiSpec(17, "DUMPLE", "Water Dumple", false, false, Base + "Lemurian/LemurianBody.prefab", Base + "Lemurian/LemurianMaster.prefab", 3, 25, TekiHull.Human),
        };

        public static TekiSpec Of(byte kind) => kind < All.Length ? All[kind] : null;
    }
}
