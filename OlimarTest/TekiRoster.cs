namespace OlimarTest
{
    // Every Pikmin enemy the plugin can spawn: the disc's kind (libpikmin-present teki::KIND_TABLE order), its
    // tier and the vanilla RoR2 monster whose body, hurt boxes and health it borrows. Pikmin 1's own strategies
    // (libpikmin-teki, ported from the decomp's Tai*/TAI* files) drive each of them: the borrowed monster's AI
    // is removed and its body follows the strategy, in the disc's model with the disc's life, speed, animations,
    // sounds and effects. Small ones are common, big ones rare and always drop a part.
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
        /// The decomp's strategy is ported (libpikmin-teki) and drives the body; otherwise the borrowed monster's
        /// own AI fights.
        public bool Ported;
        /// Not a fighter the director spawns: Ship Repair places it itself (TekiSpawner) or another enemy makes it.
        public bool NoCard;
        /// Gone when its strategy says so, with no corpse or reward (the Honeywisp, a Rolling Boulder).
        public bool Ephemeral;
        /// Lives in water: Ship Repair spawns it in the stage's water, if it has any.
        public bool Water;
        /// Cannot be killed (a Fire Geyser, a Clamclamp): its body is given endless health and it stays.
        public bool Hazard;
        /// Leaves no corpse pellet when it dies (a plant, an egg, a pearl): what it gives comes from its strategy.
        public bool NoCorpse;

        public TekiSpec(byte kind, string token, string display, bool big, bool flying, string body, string master, int weight, int cost, TekiHull hull)
        {
            Kind = kind; Token = token; Display = display; Big = big; Flying = flying;
            Body = body; Master = master; Weight = weight; Cost = cost; Hull = hull;
        }
    }

    internal static class TekiRoster
    {
        const string Base = "RoR2/Base/", Dlc1 = "RoR2/DLC1/";

        // The bodies are borrowed for their hurt boxes and health: Beetle (a headbutting bug), Beetle Guard, Bison,
        // Clay Grenadier, Elder Lemurian, Blind Pest/Vermin (small, one on the wing), Acid Larva, Hermit Crab, Jellyfish,
        // Golem, Parent, Lemurian, Wisp.
        public static readonly TekiSpec[] All =
        {
            new TekiSpec(0, "DWARF", "Dwarf Bulborb", false, false, Base + "Beetle/BeetleBody.prefab", Base + "Beetle/BeetleMaster.prefab", 6, 25, TekiHull.Human) { Ported = true },
            new TekiSpec(1, "SPOTTY", "Spotty Bulborb", true, false, Base + "BeetleGuard/BeetleGuardBody.prefab", Base + "BeetleGuard/BeetleGuardMaster.prefab", 2, 250, TekiHull.Golem) { Ported = true },
            new TekiSpec(2, "DWARFORANGE", "Dwarf Bulbear", false, false, Base + "Beetle/BeetleBody.prefab", Base + "Beetle/BeetleMaster.prefab", 5, 25, TekiHull.Human) { Ported = true },
            new TekiSpec(3, "SPOTTYORANGE", "Spotty Bulbear", true, false, Base + "BeetleGuard/BeetleGuardBody.prefab", Base + "BeetleGuard/BeetleGuardMaster.prefab", 1, 300, TekiHull.Golem) { Ported = true },
            new TekiSpec(4, "WOLLYWOG", "Wollywog", true, false, Base + "Bison/BisonBody.prefab", Base + "Bison/BisonMaster.prefab", 2, 200, TekiHull.Golem) { Ported = true },
            new TekiSpec(5, "WOLLYWOGYELLOW", "Yellow Wollywog", true, false, Base + "Bison/BisonBody.prefab", Base + "Bison/BisonMaster.prefab", 1, 250, TekiHull.Golem) { Ported = true },
            new TekiSpec(6, "CANNONBEETLE", "Armored Cannon Beetle", true, false, Dlc1 + "ClayGrenadier/ClayGrenadierBody.prefab", Dlc1 + "ClayGrenadier/ClayGrenadierMaster.prefab", 1, 300, TekiHull.Golem) { Ported = true },
            new TekiSpec(7, "BLOWHOG", "Fiery Blowhog", true, false, Base + "LemurianBruiser/LemurianBruiserBody.prefab", Base + "LemurianBruiser/LemurianBruiserMaster.prefab", 2, 250, TekiHull.Golem) { Ported = true },
            new TekiSpec(8, "SNITCHBUG", "Swooping Snitchbug", false, true, Dlc1 + "FlyingVermin/FlyingVerminBody.prefab", Dlc1 + "FlyingVermin/FlyingVerminMaster.prefab", 3, 30, TekiHull.Human) { Ported = true },
            new TekiSpec(9, "SHEARGRUBF", "Female Sheargrub", false, false, Dlc1 + "Vermin/VerminBody.prefab", Dlc1 + "Vermin/VerminMaster.prefab", 4, 20, TekiHull.Human) { Ported = true },
            new TekiSpec(10, "SHEARGRUBM", "Male Sheargrub", false, false, Dlc1 + "Vermin/VerminBody.prefab", Dlc1 + "Vermin/VerminMaster.prefab", 4, 20, TekiHull.Human) { Ported = true },
            new TekiSpec(11, "SHEARWIG", "Shearwig", false, false, Dlc1 + "Vermin/VerminBody.prefab", Dlc1 + "Vermin/VerminMaster.prefab", 2, 25, TekiHull.Human) { Ported = true },
            new TekiSpec(12, "WOGPOLE", "Wogpole", false, false, Dlc1 + "AcidLarva/AcidLarvaBody.prefab", Dlc1 + "AcidLarva/AcidLarvaMaster.prefab", 3, 25, TekiHull.Human) { Ported = true, NoCard = true, Water = true },
            new TekiSpec(13, "PUFFSTOOL", "Puffstool", true, false, Base + "HermitCrab/HermitCrabBody.prefab", Base + "HermitCrab/HermitCrabMaster.prefab", 1, 250, TekiHull.Golem) { Ported = true },
            new TekiSpec(14, "PUFFYBLOWHOG", "Puffy Blowhog", true, true, Base + "Jellyfish/JellyfishBody.prefab", Base + "Jellyfish/JellyfishMaster.prefab", 1, 250, TekiHull.Golem) { Ported = true },
            new TekiSpec(15, "MAMUTA", "Mamuta", true, false, Base + "Golem/GolemBody.prefab", Base + "Golem/GolemMaster.prefab", 1, 300, TekiHull.Golem) { Ported = true },
            new TekiSpec(16, "PROGG", "Smoky Progg", true, false, Base + "Parent/ParentBody.prefab", Base + "Parent/ParentMaster.prefab", 1, 400, TekiHull.Golem) { Ported = true },
            new TekiSpec(17, "DUMPLE", "Water Dumple", false, false, Base + "Lemurian/LemurianBody.prefab", Base + "Lemurian/LemurianMaster.prefab", 3, 25, TekiHull.Human) { Ported = true, NoCard = true, Water = true },
            new TekiSpec(18, "HONEYWISP", "Honeywisp", false, true, Base + "Wisp/WispBody.prefab", Base + "Wisp/WispMaster.prefab", 0, 1, TekiHull.Human) { Ported = true, NoCard = true, Ephemeral = true },
            new TekiSpec(19, "BREADBUG", "Breadbug", false, false, Dlc1 + "Vermin/VerminBody.prefab", Dlc1 + "Vermin/VerminMaster.prefab", 0, 20, TekiHull.Human) { Ported = true, NoCard = true },
            // What the others make or Ship Repair places: the Cannon Beetle's boulder, the egg's Progg (above), a
            // Clamclamp's pearls, and the stage's plants, geysers and shells.
            new TekiSpec(20, "BOULDER", "Rolling Boulder", false, false, Base + "Beetle/BeetleBody.prefab", Base + "Beetle/BeetleMaster.prefab", 0, 1, TekiHull.Golem) { Ported = true, NoCard = true, Ephemeral = true },
            new TekiSpec(21, "EGG", "Giant Egg", false, false, Base + "Beetle/BeetleBody.prefab", Base + "Beetle/BeetleMaster.prefab", 1, 150, TekiHull.Golem) { Ported = true, NoCorpse = true },
            new TekiSpec(22, "GEYSER", "Fire Geyser", false, false, Base + "Beetle/BeetleBody.prefab", Base + "Beetle/BeetleMaster.prefab", 0, 1, TekiHull.Golem) { Ported = true, NoCard = true, Hazard = true, NoCorpse = true },
            new TekiSpec(23, "POSY", "Pellet Posy", false, false, Base + "Beetle/BeetleBody.prefab", Base + "Beetle/BeetleMaster.prefab", 0, 1, TekiHull.Human) { Ported = true, NoCard = true, NoCorpse = true },
            new TekiSpec(24, "CLAMCLAMP", "Pearly Clamclamp", false, false, Base + "Beetle/BeetleBody.prefab", Base + "Beetle/BeetleMaster.prefab", 0, 1, TekiHull.Golem) { Ported = true, NoCard = true, Hazard = true, NoCorpse = true },
            new TekiSpec(25, "PEARL", "Clamclamp Pearl", false, false, Base + "Beetle/BeetleBody.prefab", Base + "Beetle/BeetleMaster.prefab", 0, 1, TekiHull.Human) { Ported = true, NoCard = true, NoCorpse = true },
            new TekiSpec(26, "ROCPE", "Clamclamp Treasure", false, false, Base + "Beetle/BeetleBody.prefab", Base + "Beetle/BeetleMaster.prefab", 0, 1, TekiHull.Human) { Ported = true, NoCard = true, NoCorpse = true },
            // Pikmin 2's enemies (libpikmin-teki p2, from the user's Pikmin 2 disc): kinds 27 on, in Ship Repair like the others.
            // The Kogane family are loot bugs: harmless, they pop up near a player, scuttle about, drop something each time
            // they are hit (up to three) and dive away. They cannot be killed (a hit flips them), so they keep endless health,
            // and what they give comes from the strategy, so they leave no corpse.
            new TekiSpec(27, "FLINT", "Iridescent Flint Beetle", false, false, Dlc1 + "Vermin/VerminBody.prefab", Dlc1 + "Vermin/VerminMaster.prefab", 2, 10, TekiHull.Human) { Ported = true, Hazard = true, NoCorpse = true },
            new TekiSpec(28, "GLINT", "Iridescent Glint Beetle", false, false, Dlc1 + "Vermin/VerminBody.prefab", Dlc1 + "Vermin/VerminMaster.prefab", 1, 20, TekiHull.Human) { Ported = true, Hazard = true, NoCorpse = true },
            new TekiSpec(29, "DOODLEBUG", "Doodlebug", false, false, Dlc1 + "Vermin/VerminBody.prefab", Dlc1 + "Vermin/VerminMaster.prefab", 2, 15, TekiHull.Human) { Ported = true, Hazard = true, NoCorpse = true },
            // The Bulborb Larva: a chasing biter with almost no health that leaves no corpse.
            new TekiSpec(30, "LARVA", "Bulborb Larva", false, false, Dlc1 + "Vermin/VerminBody.prefab", Dlc1 + "Vermin/VerminMaster.prefab", 4, 12, TekiHull.Human) { Ported = true, NoCorpse = true },
            // The Skitter Leaf: a bug under a leaf that skitters about and never attacks; it leaves the disc's corpse.
            new TekiSpec(31, "SKITTER", "Skitter Leaf", false, false, Dlc1 + "Vermin/VerminBody.prefab", Dlc1 + "Vermin/VerminMaster.prefab", 3, 15, TekiHull.Human) { Ported = true },
            // The Mitite: a swarm bug that wanders about its home, harmless; its leader calls the others up, and it leaves the disc's corpse.
            new TekiSpec(32, "MITITE", "Mitite", false, false, Dlc1 + "Vermin/VerminBody.prefab", Dlc1 + "Vermin/VerminMaster.prefab", 2, 10, TekiHull.Human) { Ported = true },
            // The gas pipe: a fixture Ship Repair places, like the Fire Geyser. It vents gas that kills the Pikmin in it and cannot be killed.
            new TekiSpec(33, "GASPIPE", "Gas Pipe", false, false, Base + "Beetle/BeetleBody.prefab", Base + "Beetle/BeetleMaster.prefab", 0, 1, TekiHull.Golem) { Ported = true, NoCard = true, Hazard = true, NoCorpse = true },
            // The Bomb-rock: sits until it is hit, then burns its fuse and blows up whoever is near. It is not killed, it goes off.
            new TekiSpec(34, "BOMBROCK", "Bomb-rock", false, false, Base + "Beetle/BeetleBody.prefab", Base + "Beetle/BeetleMaster.prefab", 1, 12, TekiHull.Human) { Ported = true, Hazard = true, NoCorpse = true },
            // The Watery Blowhog: patrols, turns on what it sees and blows a long stream of water (the Fiery Blowhog's flame cylinder here); leaves the disc's corpse.
            new TekiSpec(35, "WATERYBLOWHOG", "Watery Blowhog", true, false, Base + "LemurianBruiser/LemurianBruiserBody.prefab", Base + "LemurianBruiser/LemurianBruiserMaster.prefab", 2, 250, TekiHull.Golem) { Ported = true },
            // The Armored Cannon Beetle Larva: wanders, turns on what it sees and spits a rolling boulder along a line; leaves the disc's corpse.
            new TekiSpec(36, "CANNONLARVA", "Armored Cannon Beetle Larva", true, false, Dlc1 + "ClayGrenadier/ClayGrenadierBody.prefab", Dlc1 + "ClayGrenadier/ClayGrenadierMaster.prefab", 1, 260, TekiHull.Golem) { Ported = true },
            // Pikmin 2's other Bulborbs, on the Red Bulborbs' model with their own textures and numbers; they sleep at home, chase and bite, and leave the disc's corpse.
            new TekiSpec(37, "ORANGEBULBORB", "Orange Bulborb", true, false, Base + "BeetleGuard/BeetleGuardBody.prefab", Base + "BeetleGuard/BeetleGuardMaster.prefab", 2, 280, TekiHull.Golem) { Ported = true },
            new TekiSpec(38, "HAIRYBULBORB", "Hairy Bulborb", true, false, Base + "BeetleGuard/BeetleGuardBody.prefab", Base + "BeetleGuard/BeetleGuardMaster.prefab", 1, 240, TekiHull.Golem) { Ported = true },
            new TekiSpec(39, "DWARFORANGEBULBORB", "Dwarf Orange Bulborb", false, false, Base + "Beetle/BeetleBody.prefab", Base + "Beetle/BeetleMaster.prefab", 4, 25, TekiHull.Human) { Ported = true },
            new TekiSpec(40, "SNOWBULBORB", "Snow Bulborb", false, false, Base + "Beetle/BeetleBody.prefab", Base + "Beetle/BeetleMaster.prefab", 3, 20, TekiHull.Human) { Ported = true },
            // The Fiery Bulblax: a Bulborb whose body is alight (it burns what touches it) until water puts it out.
            new TekiSpec(41, "FIERYBULBLAX", "Fiery Bulblax", true, false, Base + "BeetleGuard/BeetleGuardBody.prefab", Base + "BeetleGuard/BeetleGuardMaster.prefab", 1, 350, TekiHull.Golem) { Ported = true },
            // The Empress Bulblax, a boss: sleeps in her place laying Bulborb Larvae, wakes when hurt and rolls across her territory crushing everything.
            // Always drops a ship part (big). She cannot walk off, so she is a fixture of the stage she is placed on.
            new TekiSpec(42, "EMPRESS", "Empress Bulblax", true, false, Base + "Golem/GolemBody.prefab", Base + "Golem/GolemMaster.prefab", 1, 900, TekiHull.Golem) { Ported = true },
            // The Emperor Bulblax, a boss: buried until someone comes near, bursts out, walks after the nearest and bites with a long tongue, stamps and shakes off
            // what clings to it, goes home and buries itself again when it loses its prey. Always drops a ship part.
            new TekiSpec(43, "EMPEROR", "Emperor Bulblax", true, false, Base + "Golem/GolemBody.prefab", Base + "Golem/GolemMaster.prefab", 1, 1000, TekiHull.Golem) { Ported = true },
            // The Cloaking Burrow-nit: buried until someone is in its sight, bursts out, hunts and bites, dives when it loses its prey; leaves the disc's corpse.
            new TekiSpec(44, "BURROWNIT", "Cloaking Burrow-nit", false, false, Base + "Beetle/BeetleBody.prefab", Base + "Beetle/BeetleMaster.prefab", 2, 60, TekiHull.Human) { Ported = true },
            // The Hermit Crawmad: lives in a hole, lunges at whoever comes near, carries what it caught home tail first (open to blows only then); leaves the disc's corpse.
            new TekiSpec(45, "HERMITCRAWMAD", "Hermit Crawmad", false, false, Base + "Beetle/BeetleBody.prefab", Base + "Beetle/BeetleMaster.prefab", 2, 90, TekiHull.Human) { Ported = true },
            // The Unmarked Spectralid: a butterfly that hovers over its place and cannot be hurt (endless health here); a Pikmin landing on it brings it down for nectar and a small carcass.
            new TekiSpec(46, "SPECTRALID", "Unmarked Spectralid", false, true, Base + "Wisp/WispBody.prefab", Base + "Wisp/WispMaster.prefab", 2, 8, TekiHull.Human) { Ported = true, Hazard = true },
            // The Long Legs, bosses on four stepping legs (libpikmin-teki legs/longlegs; the view folds the legs onto the feet it names): unseen above their place until
            // someone comes near, they drop, then stand and walk after the nearest, their feet crushing what they come down on, and shake off what clings. Always drop a
            // ship part (big). They leave no corpse (the disc gives them none): the body goes when it dies.
            new TekiSpec(47, "BEADYLONGLEGS", "Beady Long Legs", true, false, Base + "Golem/GolemBody.prefab", Base + "Golem/GolemMaster.prefab", 1, 1000, TekiHull.Golem) { Ported = true, NoCorpse = true },
            // Raging Long Legs: bigger and far tougher; once shaken it rages, stomping about wildly for a while.
            new TekiSpec(48, "RAGINGLONGLEGS", "Raging Long Legs", true, false, Base + "Golem/GolemBody.prefab", Base + "Golem/GolemMaster.prefab", 1, 1500, TekiHull.Golem) { Ported = true, NoCorpse = true },
            // Man-at-Legs: walks without stamping; shaken, or left alone a long while, it opens a cannon on its back and shells whoever is near.
            new TekiSpec(49, "MANATLEGS", "Man-at-Legs", true, false, Base + "Golem/GolemBody.prefab", Base + "Golem/GolemMaster.prefab", 1, 1200, TekiHull.Golem) { Ported = true, NoCorpse = true },
            // The Giant Breadbug: comes up from its mound, ambles, takes a pellet (never a ship part) home and eats it underground; a blow makes it let go and hurts it.
            new TekiSpec(50, "GIANTBREADBUG", "Giant Breadbug", true, false, Base + "BeetleGuard/BeetleGuardBody.prefab", Base + "BeetleGuard/BeetleGuardMaster.prefab", 1, 300, TekiHull.Golem) { Ported = true },
            // The Segmented Crawbster, a boss: waits out of sight until someone is near, crashes down, walks after them and rolls into a ball at them; a wall at speed
            // throws it on its back, then it rights itself and swings its arms. Always drops a ship part (big). Leaves the disc's corpse.
            new TekiSpec(51, "CRAWBSTER", "Segmented Crawbster", true, false, Base + "Golem/GolemBody.prefab", Base + "Golem/GolemMaster.prefab", 1, 1400, TekiHull.Golem) { Ported = true },
            // The Burrowing Snagret: lives underground, comes up beside whoever is in its territory and strikes with its head, swallowing Pikmin; dives when nobody is in reach.
            // Always drops a ship part (big). Leaves the disc's corpse.
            new TekiSpec(52, "SNAGRET", "Burrowing Snagret", true, false, Base + "Golem/GolemBody.prefab", Base + "Golem/GolemMaster.prefab", 1, 700, TekiHull.Golem) { Ported = true },
            // The Pileated Snagret, a boss: the same bird with its body out of the ground; it hops after what it sees and back home, and strikes with its head. Always drops a ship part (big).
            new TekiSpec(53, "PILEATEDSNAGRET", "Pileated Snagret", true, false, Base + "Golem/GolemBody.prefab", Base + "Golem/GolemMaster.prefab", 1, 1500, TekiHull.Golem) { Ported = true },
            // The Ranging Bloyster, a boss: wanders its territory until a player is within its search range, marks him, turns to him and creeps after him; in reach it lashes
            // out with its tongue (catching Pikmin to eat), and it loses him when he goes far. Always drops a ship part (big). The Toady Bloyster is the same slug without sight:
            // it ambles and sits and only strikes at what is within reach.
            new TekiSpec(54, "RANGINGBLOYSTER", "Ranging Bloyster", true, false, Base + "Golem/GolemBody.prefab", Base + "Golem/GolemMaster.prefab", 1, 1000, TekiHull.Golem) { Ported = true },
            new TekiSpec(55, "TOADYBLOYSTER", "Toady Bloyster", true, false, Base + "BeetleGuard/BeetleGuardBody.prefab", Base + "BeetleGuard/BeetleGuardMaster.prefab", 2, 350, TekiHull.Golem) { Ported = true },
        };

        public static TekiSpec Of(byte kind) => kind < All.Length ? All[kind] : null;
    }
}
