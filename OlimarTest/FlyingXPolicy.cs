namespace OlimarTest
{
    internal enum FlyingXAction { None, Land }

    /// Hold X to raise or lower the Onion. There is no charge or cooldown: the hold is the guard against
    /// an accidental press. (The double tap that dumped a flying Onion is RT + X now.)
    internal struct FlyingXPolicy
    {
        const float HoldSeconds = 0.6f;
        float heldAt;
        bool wasHeld, fired;

        public void Cancel() { wasHeld = fired = false; }

        public FlyingXAction Step(float now, bool held, bool interactClaimed)
        {
            if (interactClaimed) { Cancel(); return FlyingXAction.None; }
            if (held && !wasHeld)
            {
                wasHeld = true;
                fired = false;
                heldAt = now;
            }
            if (held && !fired && now - heldAt >= HoldSeconds)
            {
                fired = true;
                return FlyingXAction.Land;
            }
            if (!held) { wasHeld = false; fired = false; }
            return FlyingXAction.None;
        }
    }
}
