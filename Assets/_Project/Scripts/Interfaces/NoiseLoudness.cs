namespace ToyFactory.Interfaces
{
    /// <summary>
    /// Source levels (L0) for <see cref="NoiseEvent.Loudness"/>, from the plan. A noise loses
    /// 4 per metre and 35 per closed door, and is heard while above 10, so it carries
    /// <c>(L0 - 10) / 4</c> metres in the open. Use these instead of typing numbers, so every
    /// emitter agrees.
    /// </summary>
    public static class NoiseLoudness
    {
        /// <summary>Blaster shot: heard 22.5 m away.</summary>
        public const float BlasterShot = 100f;

        /// <summary>Chapter 3 relay alarm (wrong order): 20 m.</summary>
        public const float RelayAlarm = 90f;

        /// <summary>Chapter 4 power core explosion: 20 m.</summary>
        public const float CoreExplosion = 90f;

        /// <summary>Thrown wind-up toy landing, repeated every 1 s for 5 s: 15 m.</summary>
        public const float ToyLanding = 70f;

        /// <summary>Door slam: 12.5 m.</summary>
        public const float DoorSlam = 60f;

        /// <summary>Chapter 2 colour terminal beep, every 0.8 s while held: 12.5 m.</summary>
        public const float TerminalBeep = 60f;

        /// <summary>Box pushed or landing: 10 m.</summary>
        public const float BoxImpact = 50f;

        /// <summary>Chapter 1 pressure plate click: 7.5 m.</summary>
        public const float PlateClick = 40f;

        /// <summary>Player running footsteps: 3.75 m.</summary>
        public const float Footsteps = 25f;
    }
}
