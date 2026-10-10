namespace ToyFactory.AI.Agents.Captain
{
    /// <summary>What the Captain's body does this frame about its prediction: flash the goal, say a line, both or neither.</summary>
    public readonly struct Callout
    {
        /// <summary>Flash the red marker on the committed goal.</summary>
        public readonly bool Mark;

        /// <summary>The line to say above its head, or null for none.</summary>
        public readonly string Line;

        public Callout(bool mark, string line)
        {
            Mark = mark;
            Line = line;
        }

        public bool IsNone => !Mark && Line == null;
    }

    /// <summary>
    /// When the Captain lets the player know it has read them, and what it says. The
    /// prediction is the Captain's whole idea, and without this it is invisible: the player
    /// only sees a robot that is somehow ahead of them. On every new commitment (a goal it
    /// starts cutting off or guarding, or a switch to another goal) it flashes a red marker on
    /// that goal, and, if the player can see it, says one line.
    /// </summary>
    /// <remarks>
    /// <para>Rate limits keep it a taunt, not a commentary: a line at most every 8 s, the same
    /// goal called out again only after 20 s, and the marker at most every 2 s. A line it
    /// cannot be seen saying is skipped, not saved for later.</para>
    /// <para>Switching goals within 6 s of the last commitment is the player's feint working,
    /// so it gets its own lines: the Captain admits it saw the change. Pure C#, so the rules
    /// are tested without a scene; the body only shows the result.</para>
    /// </remarks>
    public sealed class CaptainCallouts
    {
        public const float LineCooldown = 8f;
        public const float RepeatAfter = 20f;
        public const float MarkCooldown = 2f;
        public const float FeintWindow = 6f;

        const int NoGoal = int.MinValue;

        // The Captain's voice (see its wake line in Story.md): formal, no contractions, cold.
        // Short enough for one line above its head.
        static readonly string[] TaskLines =
        {
            "That task, 047? I am already there.",
            "I know where you are going.",
            "Predictable. I will be waiting.",
        };
        static readonly string[] CoreLines =
        {
            "The core? Not while I stand.",
            "You want the core. I will be there.",
            "I have computed your path to the core.",
        };
        static readonly string[] SwitchLines =
        {
            "The switch, 047? I will be there first.",
            "You will not reach that switch before me.",
        };
        static readonly string[] ConsoleLines =
        {
            "The console. Of course.",
            "You must reach the console. So I wait.",
        };
        static readonly string[] BatteryLines =
        {
            "Low on power? I know where you are going.",
        };
        static readonly string[] GuardLines =
        {
            "Hide if you like. You must come here.",
            "I do not need to find you. I only wait.",
        };
        static readonly string[] FeintLines =
        {
            "Changing your mind will not help.",
            "A feint? I saw it.",
        };

        static readonly string[][] Pools = { TaskLines, CoreLines, SwitchLines, ConsoleLines, BatteryLines, GuardLines, FeintLines };
        readonly int[] _next = new int[Pools.Length];

        int _committedId = NoGoal;
        int _lastGoalId = NoGoal;
        float _lastCommitEndAt = float.NegativeInfinity;
        float _lastLineAt = float.NegativeInfinity;
        int _lastLineGoalId = NoGoal;
        float _lastMarkAt = float.NegativeInfinity;

        /// <summary>
        /// Call once a frame with the Captain's commitment.
        /// </summary>
        /// <param name="time">Game time in seconds.</param>
        /// <param name="committed">True while it is committed to a goal.</param>
        /// <param name="goal">The committed goal (ignored when not committed).</param>
        /// <param name="guarding">True if it is guarding the goal rather than cutting the player off.</param>
        /// <param name="finalChapter">Chapter 4, where the tasks are the cores.</param>
        /// <param name="visible">True if the player can see the Captain now (on screen, not behind a wall).</param>
        public Callout Update(float time, bool committed, in CandidateGoal goal, bool guarding, bool finalChapter, bool visible)
        {
            if (!committed)
            {
                if (_committedId != NoGoal)
                    _lastCommitEndAt = time;
                _committedId = NoGoal;
                return default;
            }
            if (goal.Id == _committedId)
                return default;

            // A new commitment: from nothing, or a switch to another goal.
            bool switching = _committedId != NoGoal || time - _lastCommitEndAt <= FeintWindow;
            bool feint = switching && _lastGoalId != NoGoal && _lastGoalId != goal.Id;
            _committedId = goal.Id;
            _lastGoalId = goal.Id;

            bool mark = time - _lastMarkAt >= MarkCooldown;
            if (mark)
                _lastMarkAt = time;

            string line = null;
            bool repeat = goal.Id == _lastLineGoalId && time - _lastLineAt < RepeatAfter;
            if (visible && time - _lastLineAt >= LineCooldown && !repeat)
            {
                line = NextLine(PoolFor(goal.Category, guarding, finalChapter, feint));
                _lastLineAt = time;
                _lastLineGoalId = goal.Id;
            }
            return new Callout(mark, line);
        }

        static int PoolFor(GoalCategory category, bool guarding, bool finalChapter, bool feint)
        {
            if (feint) return 6;
            if (guarding) return 5;
            switch (category)
            {
                case GoalCategory.Switch: return 2;
                case GoalCategory.Console: return 3;
                case GoalCategory.Battery: return 4;
                default: return finalChapter ? 1 : 0;
            }
        }

        // Round the pool in order, so a line never follows itself.
        string NextLine(int pool)
        {
            string[] lines = Pools[pool];
            string line = lines[_next[pool] % lines.Length];
            _next[pool]++;
            return line;
        }

        /// <summary>
        /// The name of the line's comic speech bubble (Textures/FX/CaptainSays): "Say_" and the
        /// letters and digits of each word, capitalised. "The console. Of course." is
        /// "Say_TheConsoleOfCourse".
        /// </summary>
        public static string BubbleName(string line)
        {
            var name = new System.Text.StringBuilder("Say_", 48);
            bool wordStart = true;
            foreach (char c in line)
            {
                if (!char.IsLetterOrDigit(c))
                {
                    wordStart = true;
                    continue;
                }
                name.Append(wordStart ? char.ToUpperInvariant(c) : c);
                wordStart = false;
            }
            return name.ToString();
        }

        /// <summary>Every line it can say, for tests and the dialogue list in Story.md.</summary>
        public static System.Collections.Generic.IEnumerable<string> AllLines()
        {
            foreach (string[] pool in Pools)
                foreach (string line in pool)
                    yield return line;
        }
    }
}
