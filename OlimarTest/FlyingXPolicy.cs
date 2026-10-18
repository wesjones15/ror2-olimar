namespace OlimarTest
{
    internal enum FlyingXAction { None, Land, Release }

    /// The first X reserves a short double-press window. Its landing eligibility is
    /// captured at that instant, so a cooldown completing during the window does not
    /// turn an unavailable first press into a delayed landing.
    internal struct FlyingXPolicy
    {
        const float Window = 0.75f;
        float deadline;
        bool pending, mayLand;

        public void Cancel() => pending = false;

        public FlyingXAction Step(float now, bool pressed, bool flying, bool interactClaimed, bool canLand)
        {
            if (!flying) { Cancel(); return FlyingXAction.None; }
            if (pressed && !interactClaimed)
            {
                if (pending && now <= deadline)
                {
                    Cancel();
                    return FlyingXAction.Release;
                }
                pending = true;
                mayLand = canLand;
                deadline = now + Window;
                return FlyingXAction.None;
            }
            if (pending && now >= deadline)
            {
                Cancel();
                return mayLand ? FlyingXAction.Land : FlyingXAction.None;
            }
            return FlyingXAction.None;
        }
    }
}
