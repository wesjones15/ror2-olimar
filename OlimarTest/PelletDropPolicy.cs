namespace OlimarTest
{
    internal static class PelletDropPolicy
    {
        // Host balance tiers, using unlevelled health so difficulty never inflates the drop.
        public static int Number(bool boss, bool elite, float baseHealth, float radius) =>
            boss ? 20 : elite || baseHealth >= 600f ? 10 : baseHealth >= 200f || radius >= 1f ? 5 : 1;
    }
}
