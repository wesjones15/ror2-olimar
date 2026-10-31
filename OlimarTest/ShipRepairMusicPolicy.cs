namespace OlimarTest
{
    internal static class ShipRepairMusicPolicy
    {
        internal static readonly uint[] Tracks = { 4, 5, 10, 9, 17 };
        internal static uint Track(ulong seed, int stage)
        {
            uint previous = 4;
            // Reconstruct the seeded sequence: a late join does not need the
            // host's previous local playback state to select the same song.
            for (int number = 1; number <= stage; number++)
            {
                ulong value = seed ^ (unchecked((ulong)number) * 0x9E3779B97F4A7C15UL);
                value ^= value >> 30; value = unchecked(value * 0xBF58476D1CE4E5B9UL); value ^= value >> 27;
                int index = (int)(value % (ulong)Tracks.Length);
                if (Tracks[index] == previous) index = (index + 1) % Tracks.Length;
                previous = Tracks[index];
            }
            return previous;
        }
        internal static int Pikmin2Chance(int stage) => stage < 2 ? 20 : stage < 5 ? 35 : 50;
        internal static uint MixedTrack(ulong seed,int stage)
        {
            ulong value=seed ^ unchecked((ulong)(stage+1)*0xD6E8FEB86659FD93UL);
            value ^= value>>32; value=unchecked(value*0x9E3779B97F4A7C15UL); value ^= value>>29;
            return value%100 < (ulong)Pikmin2Chance(stage) ? 0x100u+(uint)((value>>8)%4) : Track(seed,stage);
        }
        internal static bool Explore(bool enabled, bool repair, bool eligible, bool terminal, bool boss) =>
            enabled && repair && eligible && !terminal && !boss;
    }
}
