namespace OlimarTest
{
    // One physical button, with a held whistle modifier. Kept independent of Rewired so
    // the frame/tick boundary and menu handling can be checked outside the game.
    internal sealed class JumpWhistleButton
    {
        bool wasDown, jumpTap, swallowed;
        public bool Blocked { get; private set; }
        public bool Whistle { get; private set; }
        public bool Jump => jumpTap && !Blocked;

        public void Read(bool down, bool modifier)
        {
            Blocked = modifier;
            if (!down) swallowed = false;
            Whistle = down && modifier && !swallowed;
            if (down && !wasDown && !modifier && !swallowed) jumpTap = true;
            if (modifier) jumpTap = false;
            wasDown = down;
        }

        public void Ticked() { jumpTap = false; }
        public void Swallow() { swallowed = true; jumpTap = Whistle = false; }
    }
}
