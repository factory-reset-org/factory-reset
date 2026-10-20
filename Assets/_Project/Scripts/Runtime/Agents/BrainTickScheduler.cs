using System.Diagnostics;
using UnityEngine;

namespace ToyFactory.Runtime.Agents
{
    /// <summary>
    /// The path request scheduler: a per-frame budget for AI work, so heavy decisions are
    /// spread over frames instead of landing in the same one. Every agent asks before its
    /// brain ticks; once this frame's brain ticks have used the budget, the rest wait for the
    /// next frame (their bodies keep walking their routes), and an agent never waits more
    /// than <see cref="MaxWaitFrames"/> frames.
    /// </summary>
    /// <remarks>
    /// <para><b>Why per decision, not per path request.</b> Brains ask for routes synchronously
    /// inside their tick (<c>IPathfinder.FindPath</c>) and use the answer at once, so a queue
    /// that hands a path back some frames later would mean rewriting every brain. A brain's
    /// route searches happen inside its decision, so budgeting the decisions budgets the path
    /// requests: two agents that would each run a 5 ms search in the same frame now run them
    /// in consecutive frames.</para>
    /// <para><b>Why waiting is safe.</b> Brains time everything with <c>AgentContext.Time</c>,
    /// not frame counts, so a decision one or two frames late is the same decision. Noises
    /// heard meanwhile are kept (the loudest) until the brain's next tick, and the body keeps
    /// following the route it has.</para>
    /// <para>The first brain of a frame always ticks (the budget only says when to stop), so a
    /// single heavy agent is never starved. Times come from <see cref="Stopwatch"/> ticks;
    /// nothing allocates.</para>
    /// </remarks>
    public static class BrainTickScheduler
    {
        /// <summary>Brain time (ms) a frame may use before the remaining agents wait for the next frame.</summary>
        public static float FrameBudgetMs = 2f;

        /// <summary>Most frames an agent waits; after that it ticks whatever the budget says.</summary>
        public const int MaxWaitFrames = 2;

        static int _frame = -1;
        static long _spentTicks;
        static int _waitedThisFrame;

        /// <summary>Brain time used so far this frame, in milliseconds.</summary>
        public static double SpentThisFrameMs
        {
            get
            {
                Roll();
                return _spentTicks * 1000.0 / Stopwatch.Frequency;
            }
        }

        /// <summary>Brain ticks put off to the next frame this frame (for the debug overlay and the performance log).</summary>
        public static int WaitedThisFrame
        {
            get
            {
                Roll();
                return _waitedThisFrame;
            }
        }

        /// <summary>Brain ticks put off since the session started.</summary>
        public static int TotalWaits { get; private set; }

        /// <summary>
        /// True if this agent's brain may tick now: the budget has room, or it has already
        /// waited <see cref="MaxWaitFrames"/> frames. False means wait for the next frame.
        /// </summary>
        public static bool TryBegin(int framesWaited)
        {
            Roll();
            if (framesWaited >= MaxWaitFrames || _spentTicks * 1000.0 / Stopwatch.Frequency < FrameBudgetMs)
                return true;
            _waitedThisFrame++;
            TotalWaits++;
            return false;
        }

        /// <summary>Adds a brain tick's time (in <see cref="Stopwatch"/> ticks) to this frame's spend.</summary>
        public static void End(long stopwatchTicks)
        {
            Roll();
            _spentTicks += stopwatchTicks;
        }

        static void Roll()
        {
            int frame = Time.frameCount;
            if (frame == _frame)
                return;
            _frame = frame;
            _spentTicks = 0;
            _waitedThisFrame = 0;
        }

        // Domain reload is off: start each play session clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            _frame = -1;
            _spentTicks = 0;
            _waitedThisFrame = 0;
            TotalWaits = 0;
            FrameBudgetMs = 2f;
        }
    }
}
