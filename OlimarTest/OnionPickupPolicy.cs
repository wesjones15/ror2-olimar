namespace OlimarTest
{
    internal static class OnionPickupPolicy
    {
        // Ship parts are handed over exclusively by native carry AI to its selected
        // receiver. Following Onions must not target, descend to, or vacuum them,
        // even when they are duplicates eligible for a deliberate salvage haul.
        internal static bool CanTrack(in Native.Pellet pellet, int onion) => pellet.has_ship_part == 0 &&
            pellet.state != 3 && (pellet.state == 0 || pellet.onion == onion);
    }
}
