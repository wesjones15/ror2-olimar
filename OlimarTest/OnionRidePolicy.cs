namespace OlimarTest
{
    // Onion Ride's clock and speed, kept free of Unity so tests/OnionRideCheck can run it.
    //
    // Boarding starts a grace period of up to GraceSeconds. It ends at five seconds or once the Onion is
    // more than GraceRadius metres (horizontally) from where he boarded, whichever comes first. Then
    // the ride proper runs ActiveSeconds: it never pauses or resets, however the Onion moves.
    internal struct OnionRidePolicy
    {
        public const float GraceSeconds = 5f, GraceRadius = 10f, ActiveSeconds = 10f, BoostSeconds = 2f;
        /// The Onion's speed against Olimar's walking speed, and while the left stick click boosts it.
        public const float SpeedScale = 2f, BoostScale = 3f;

        public float graceElapsed, activeLeft, boostLeft;
        public bool graceOver;

        public static OnionRidePolicy Begin() => new OnionRidePolicy { activeLeft = ActiveSeconds };

        /// The ride's active ten seconds are up.
        public bool Expired => graceOver && activeLeft <= 0f;

        /// Boosting right now.
        public bool Boosting => boostLeft > 0f;

        /// Advances the clocks by `dt`; `distance` is how far the Onion is from the boarding spot on the
        /// ground plane, and `boost` that the stick click was pressed this step. Returns the speed
        /// multiplier on Olimar's walking speed to fly at.
        public float Step(float dt, float distance, bool boost)
        {
            if (boost && boostLeft <= 0f) boostLeft = BoostSeconds;
            if (!graceOver)
            {
                graceElapsed += dt;
                if (graceElapsed >= GraceSeconds || distance > GraceRadius) graceOver = true;
            }
            else activeLeft -= dt;
            float scale = boostLeft > 0f ? BoostScale : SpeedScale;
            if (boostLeft > 0f) boostLeft -= dt;
            return scale;
        }

        /// How a ride's Pikmin leave: a third down each of the three legs.
        public static int PerLeg(int pikmin, int leg) => pikmin / 3 + (leg < pikmin % 3 ? 1 : 0);
    }
}
