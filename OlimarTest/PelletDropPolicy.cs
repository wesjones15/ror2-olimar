namespace OlimarTest
{
    /// Which numbered pellet a dead enemy leaves: 1 (an ordinary enemy), 5 (a heavy body the size
    /// of a Beetle Guard or a Stone Golem), 10 (anything bigger, or a named boss that isn't the
    /// stage's champion) and 20 (the stage boss).
    internal static class PelletDropPolicy
    {
        /// The body's size class, from the game's own navigation hulls: `Human` is an ordinary
        /// enemy, `Golem` a heavy one, `BeetleQueen` anything larger.
        public enum Size { Small, Large, Huge }

        public static int Number(bool champion, bool boss, Size size) =>
            champion ? 20 : boss || size == Size.Huge ? 10 : size == Size.Large ? 5 : 1;
    }
}
