namespace ToyFactory.AI.Agents.Tracker
{
    /// <summary>
    /// The Tracker's wind-up spring. Energy drains while it moves (fast while chasing,
    /// slowly otherwise); at zero the toy must stop and rewind its key until the spring is
    /// full again. This is the counter-play hook: kite the Tracker until it winds down, then
    /// strike while it rewinds.
    /// </summary>
    /// <remarks>
    /// Pure C# and driven only by game time passed in from <c>ctx.Time</c>, so it freezes
    /// with the game during cutscenes and pause. Allocates nothing after construction.
    /// </remarks>
    public sealed class WindUpEnergy
    {
        /// <summary>Energy of a fully wound spring.</summary>
        public const float MaxEnergy = 100f;

        /// <summary>Drain per second while chasing: a full spring lasts 10 s of pursuit.</summary>
        public const float ChaseDrain = 10f;

        /// <summary>Drain per second in every other state: a full spring lasts 50 s.</summary>
        public const float NormalDrain = 2f;

        /// <summary>Seconds to wind from empty back to full.</summary>
        public const float RewindDuration = 3f;

        float _lastTime;
        bool _hasTime;

        // Rewind progress is kept as elapsed time, not as added energy, so whole-second
        // ticks reach exactly RewindDuration and the spring is full at exactly 3 s
        // (summing 100/3 three times in floats gives 99.99999 and would miss it).
        float _rewindElapsed;

        /// <summary>Current energy, from 0 to <see cref="MaxEnergy"/>. Starts full.</summary>
        public float Energy { get; private set; } = MaxEnergy;

        /// <summary>Energy as a fraction from 0 to 1 (drives the key-spin animation).</summary>
        public float Energy01 => Energy / MaxEnergy;

        /// <summary>True from the moment energy hits zero until the spring is full again.</summary>
        public bool IsRewinding { get; private set; }

        /// <summary>
        /// Advances the spring to game time <paramref name="now"/>. The first call only
        /// records the time. While rewinding, <paramref name="chasing"/> is ignored: the toy
        /// is standing still winding its key.
        /// </summary>
        public void Tick(float now, bool chasing)
        {
            if (!_hasTime)
            {
                Resume(now);
                return;
            }

            float dt = now - _lastTime;
            _lastTime = now;
            if (dt <= 0f) return;   // time stood still or went backwards: nothing to do

            if (IsRewinding)
            {
                _rewindElapsed += dt;
                if (_rewindElapsed >= RewindDuration)
                {
                    Energy = MaxEnergy;
                    IsRewinding = false;
                }
                else
                {
                    Energy = MaxEnergy * (_rewindElapsed / RewindDuration);
                }

                return;
            }

            Energy -= (chasing ? ChaseDrain : NormalDrain) * dt;
            if (Energy <= 0f)
            {
                Energy = 0f;
                IsRewinding = true;
                _rewindElapsed = 0f;
            }
        }

        /// <summary>
        /// Records <paramref name="now"/> as the last tick time without changing energy.
        /// Call it on the first tick after a stun: game time kept running while the brain
        /// was not ticked, and that gap must not count as drain (a 7 s stun would otherwise
        /// cost 14 energy, or 70 if the toy was chasing).
        /// </summary>
        public void Resume(float now)
        {
            _lastTime = now;
            _hasTime = true;
        }
    }
}
