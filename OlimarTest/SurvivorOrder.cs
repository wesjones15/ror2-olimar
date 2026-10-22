using System;

namespace OlimarTest
{
    /// Where Olimar and Mario sit on the character-select bar. RoR2 sorts SurvivorCatalog.orderedSurvivorDefs once, by
    /// SurvivorDef.desiredSortPosition (SurvivorCatalog.SetSurvivorDefs), and CharacterSelectBarController.Build lays the
    /// icons out in that array's order. Outside Ship Repair Mario leads and Olimar follows; in Ship Repair Olimar leads
    /// and Mario (locked there until Olimar wins) follows. The Melee fighters start at 102 and are never touched.
    /// Plain types only, so the headless ShipRepairCheck can compile this file.
    internal static class SurvivorOrder
    {
        public const float Lead = 100f, Second = 101f;

        public static float OlimarPosition(bool shipRepair) => shipRepair ? Lead : Second;
        public static float MarioPosition(bool shipRepair) => shipRepair ? Second : Lead;

        /// Re-sorts the slice of `ordered` that spans the two survivors, stably, by `position`. Only that slice can
        /// change because only their positions did. `moved` is told each slot's new occupant. Returns false when
        /// either survivor is missing from the array or nothing moved.
        public static bool Reorder<T>(T[] ordered, T olimar, T mario, Func<T, float> position, Action<T, int> moved) where T : class
        {
            if (ordered == null || olimar == null || mario == null) return false;
            int a = Array.IndexOf(ordered, olimar), b = Array.IndexOf(ordered, mario);
            if (a < 0 || b < 0) return false;
            int lo = Math.Min(a, b), hi = Math.Max(a, b);
            bool changed = false;
            for (int i = lo + 1; i <= hi; i++)
            {
                var item = ordered[i];
                int j = i - 1;
                while (j >= lo && position(ordered[j]) > position(item)) { ordered[j + 1] = ordered[j]; j--; changed = true; }
                ordered[j + 1] = item;
            }
            if (changed && moved != null) for (int i = lo; i <= hi; i++) moved(ordered[i], i);
            return changed;
        }
    }
}
