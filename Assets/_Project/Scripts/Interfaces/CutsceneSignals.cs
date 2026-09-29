namespace ToyFactory.Interfaces
{
    /// <summary>
    /// Ids of the Critical signals passed by <see cref="CutsceneEvents.OnCriticalSignal"/>.
    /// Use these constants instead of typing the strings, so a typo is a compile error.
    /// </summary>
    public static class CutsceneSignals
    {
        /// <summary>Chapter 3 cutscene: the Captain leaves its Dormant state.</summary>
        public const string CaptainWake = "CaptainWake";

        /// <summary>Chapter 3 cutscene: the Control Room doors unlock.</summary>
        public const string ControlRoomUnlock = "ControlRoomUnlock";

        /// <summary>The core shields drop so the cores can be reached.</summary>
        public const string CoreShieldsDown = "CoreShieldsDown";
    }
}
