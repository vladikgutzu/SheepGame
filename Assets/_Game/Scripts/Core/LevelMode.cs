namespace SheepGame.Core
{
    /// <summary>What is running out on this level.</summary>
    // One or the other, never both. The tap limit exists to make the player read the board
    // before touching it; a clock exists to stop them reading it. Running the two together
    // would ask for opposite things at once and the level would just feel unfair -- so a
    // Time Rush level has no tap limit at all, and every ordinary level has no clock.
    public enum LevelMode
    {
        Taps,
        TimeRush
    }

    /// <summary>Per-level say over the mode. Auto lets the progression decide.</summary>
    public enum LevelModeOverride
    {
        Auto = 0,
        AlwaysTaps = 1,
        AlwaysTimeRush = 2
    }
}
