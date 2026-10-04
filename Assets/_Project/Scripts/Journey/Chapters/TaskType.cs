namespace ToyFactory.Journey.Chapters
{
    /// <summary>What kind of prop a task is; the prop (S2) implements the behaviour.</summary>
    public enum TaskType
    {
        /// <summary>Use it once (the conveyor lever).</summary>
        Interact,
        /// <summary>Get a crate onto a pressure plate.</summary>
        BoxOnPlate,
        /// <summary>Pick up an item (a fuse, the keycard).</summary>
        Collect,
        /// <summary>Hit every target within a time window (4 targets in 12 s).</summary>
        TimedHits,
        /// <summary>Stay in a zone until a meter fills; it decays when you leave (the colour terminal).</summary>
        Hold,
        /// <summary>Use several items in a given order (the relays).</summary>
        Sequence,
        /// <summary>Destroy it (a power core).</summary>
        Destroy,
        /// <summary>Hold the interact button at it (the console).</summary>
        HoldAt
    }
}
