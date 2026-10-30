namespace OlimarTest
{
    internal enum FlyingXAction { None, Land, Release }

    /// Hold X for relocation; two short presses dump a flying Onion.
    internal struct FlyingXPolicy
    {
        const float Window = 0.75f;
        const float HoldSeconds = 0.6f;
        float deadline, heldAt;
        bool pending, wasHeld, fired, mayLand;

        public void Cancel() { pending = wasHeld = fired = false; }

        public FlyingXAction Step(float now, bool held, bool flying, bool interactClaimed, bool canLand)
        {
            if (interactClaimed) { Cancel(); return FlyingXAction.None; }
            if (held && !wasHeld)
            {
                wasHeld = true;
                fired = false;
                heldAt = now;
                mayLand = canLand;
                if (flying && pending && now <= deadline)
                {
                    pending = false;
                    fired = true;
                    return FlyingXAction.Release;
                }
            }
            if (held && !fired && now - heldAt >= HoldSeconds)
            {
                fired = true;
                pending = false;
                return mayLand ? FlyingXAction.Land : FlyingXAction.None;
            }
            if (!held && wasHeld)
            {
                if (!fired) { pending = flying; deadline = now + Window; }
                wasHeld = false;
                fired = false;
            }
            if (pending && now > deadline) pending = false;
            return FlyingXAction.None;
        }
    }
}
