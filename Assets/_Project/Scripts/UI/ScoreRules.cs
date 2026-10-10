using System;
using System.Collections.Generic;
using ToyFactory.Interfaces;

namespace ToyFactory.UI
{
    /// <summary>What an award of points was for. The Results breakdown groups by this.</summary>
    public enum ScoreReason
    {
        /// <summary>A control switch was restored.</summary>
        Switch,

        /// <summary>A checklist task was completed, or a power core destroyed.</summary>
        Task,

        /// <summary>An agent was knocked out or scrapped.</summary>
        Takedown,

        /// <summary>A battery was picked up.</summary>
        Battery,

        /// <summary>All four Saboteurs were destroyed.</summary>
        AllSaboteurs,

        /// <summary>The win bonus: the flat part.</summary>
        WinBase,

        /// <summary>The win bonus: the time part.</summary>
        WinTime,

        /// <summary>The win bonus: the part for integrity left.</summary>
        WinIntegrity,

        /// <summary>The win bonus: the part for accuracy.</summary>
        WinAccuracy
    }

    /// <summary>One award of points, for the points feed and the breakdown.</summary>
    public readonly struct ScoreAward
    {
        /// <summary>Points added to the score.</summary>
        public int Points { get; }

        /// <summary>What it was for.</summary>
        public ScoreReason Reason { get; }

        /// <summary>A short line for the points feed, such as "Guard takedown x2".</summary>
        public string Label { get; }

        /// <summary>Game time of the award, in seconds.</summary>
        public float Time { get; }

        /// <summary>Creates an award.</summary>
        public ScoreAward(int points, ScoreReason reason, string label, float time)
        {
            Points = points;
            Reason = reason;
            Label = label;
            Time = time;
        }
    }

    /// <summary>The grade letters, best first.</summary>
    public enum ScoreGrade
    {
        S,
        A,
        B,
        C,
        D
    }

    /// <summary>
    /// The scoring table of the game and nothing else: how many points each event is worth, the
    /// takedown combo, the decay for repeat takedowns, the win bonus and the grade. Plain C#, with no
    /// clock and no scene: every call is told the game time, so it can be tested directly and a
    /// pause or cutscene (which stop game time) never costs or gives anything.
    /// </summary>
    /// <remarks>
    /// <para><b>Takedown points</b> are <c>base x repeat factor x combo</c>, rounded once at the end
    /// (halves round away from zero). Both factors multiply, so their order does not matter.</para>
    /// <para><b>Repeat decay is linear.</b> The first takedown of an agent counts in full, its second
    /// 75%, its third 50%, and every later one 25%. The plan's wording ("25% less per repeat, floor 25%")
    /// needs a floor only if the steps are linear: a compounding 25% would reach 25% on its own.
    /// Repeats are counted per agent id, so only agents that reboot (Tracker, Guard, Captain) can
    /// decay; a Saboteur is scrapped once.</para>
    /// <para><b>Combo.</b> The first takedown is x1. A takedown within <see cref="ComboWindowSeconds"/>
    /// of the previous one raises the multiplier by one, up to <see cref="ComboCap"/>; a longer gap
    /// starts again at x1. The window runs from the previous takedown, not from the first of the chain.</para>
    /// </remarks>
    public sealed class ScoreRules
    {
        /// <summary>Points for restoring a switch.</summary>
        public const int SwitchPoints = 1000;

        /// <summary>Points for a checklist task or a power core.</summary>
        public const int TaskPoints = 300;

        /// <summary>Points for a battery pickup.</summary>
        public const int BatteryPoints = 50;

        /// <summary>One-off bonus for destroying all four Saboteurs.</summary>
        public const int AllSaboteursBonus = 1000;

        /// <summary>How many Saboteurs there are.</summary>
        public const int SaboteurCount = 4;

        /// <summary>Seconds within which the next takedown raises the combo.</summary>
        public const float ComboWindowSeconds = 6f;

        /// <summary>The highest combo multiplier.</summary>
        public const int ComboCap = 4;

        /// <summary>Share of its base points a takedown loses for each earlier takedown of the same agent.</summary>
        public const float RepeatDecayStep = 0.25f;

        /// <summary>The least a repeat takedown is worth, as a share of its base points.</summary>
        public const float RepeatFloor = 0.25f;

        /// <summary>The flat part of the win bonus.</summary>
        public const int WinBasePoints = 2000;

        /// <summary>The time bonus at zero seconds; it falls by <see cref="TimeBonusPerSecond"/> a second to a floor of 0.</summary>
        public const int TimeBonusStart = 3000;

        /// <summary>Time bonus lost per second of run time.</summary>
        public const int TimeBonusPerSecond = 3;

        /// <summary>Win bonus points per point of integrity (HP) left.</summary>
        public const int PointsPerHp = 10;

        /// <summary>Win bonus points per percent of accuracy.</summary>
        public const int PointsPerAccuracyPercent = 15;

        /// <summary>Shots needed before accuracy counts towards the win bonus.</summary>
        public const int MinShotsForAccuracy = 10;

        /// <summary>The lowest score of each grade: S 14000, A 11000, B 8000, C 5000, otherwise D.</summary>
        public static readonly int[] GradeThresholds = { 14000, 11000, 8000, 5000 };

        readonly List<ScoreAward> _history = new List<ScoreAward>();
        readonly Dictionary<int, int> _takedownsPerAgent = new Dictionary<int, int>();
        readonly HashSet<int> _destroyedSaboteurs = new HashSet<int>();
        readonly int[] _byReason = new int[Enum.GetValues(typeof(ScoreReason)).Length];

        float _lastTakedownTime;
        bool _hasTakedown;
        bool _allSaboteursAwarded;
        bool _won;

        /// <summary>Raised for every award, right after the score changes.</summary>
        public event Action<ScoreAward> Awarded;

        /// <summary>The score so far.</summary>
        public int Score { get; private set; }

        /// <summary>The combo multiplier the next takedown would get if it came now, 1 to <see cref="ComboCap"/>.</summary>
        public int Combo { get; private set; } = 1;

        /// <summary>Takedowns scored so far.</summary>
        public int Takedowns { get; private set; }

        /// <summary>Every award so far, in order.</summary>
        public IReadOnlyList<ScoreAward> History => _history;

        /// <summary>True once the win bonus has been awarded.</summary>
        public bool HasWon => _won;

        /// <summary>The grade for the current score.</summary>
        public ScoreGrade Grade => GradeFor(Score);

        /// <summary>The points awarded for one reason so far.</summary>
        public int PointsFor(ScoreReason reason) => _byReason[(int)reason];

        /// <summary>The combo multiplier at <paramref name="time"/>: 1 once the window has run out.</summary>
        public int ComboAt(float time) =>
            _hasTakedown && time >= _lastTakedownTime && time - _lastTakedownTime <= ComboWindowSeconds ? Combo : 1;

        /// <summary>Awards the points for a restored switch.</summary>
        public ScoreAward SwitchRestored(float time) => Award(SwitchPoints, ScoreReason.Switch, "Switch restored", time);

        /// <summary>Awards the points for a completed task or a destroyed power core.</summary>
        public ScoreAward TaskCompleted(float time) => Award(TaskPoints, ScoreReason.Task, "Task complete", time);

        /// <summary>Awards the points for a battery pickup.</summary>
        public ScoreAward BatteryPickedUp(float time) => Award(BatteryPoints, ScoreReason.Battery, "Battery", time);

        /// <summary>The base points of a takedown of this kind of agent.</summary>
        public static int TakedownBase(AgentType type)
        {
            switch (type)
            {
                case AgentType.Tracker: return 150;
                case AgentType.Guard: return 250;
                case AgentType.Saboteur: return 400;
                case AgentType.Captain: return 600;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        /// <summary>
        /// The share of its base points the next takedown of the agent <paramref name="agentId"/> is
        /// worth: 1, 0.75, 0.5, then 0.25 for every later one.
        /// </summary>
        public float RepeatFactor(int agentId)
        {
            _takedownsPerAgent.TryGetValue(agentId, out int earlier);
            return Math.Max(RepeatFloor, 1f - RepeatDecayStep * earlier);
        }

        /// <summary>
        /// Awards a takedown: <c>base x repeat factor x combo</c>. A Saboteur also counts towards the
        /// all-four bonus, which is awarded once, right after the fourth.
        /// </summary>
        /// <param name="type">The kind of agent.</param>
        /// <param name="agentId">The agent's unique id, to count repeats.</param>
        /// <param name="time">Game time in seconds.</param>
        /// <returns>The takedown's award; the all-four bonus, if it came with it, is in <see cref="History"/>.</returns>
        public ScoreAward Takedown(AgentType type, int agentId, float time)
        {
            int combo = AdvanceCombo(time);
            float repeat = RepeatFactor(agentId);
            _takedownsPerAgent.TryGetValue(agentId, out int earlier);
            _takedownsPerAgent[agentId] = earlier + 1;
            Takedowns++;

            int points = (int)Math.Round(TakedownBase(type) * repeat * combo, MidpointRounding.AwayFromZero);
            string label = combo > 1 ? $"{type} takedown x{combo}" : $"{type} takedown";
            ScoreAward award = Award(points, ScoreReason.Takedown, label, time);

            if (type == AgentType.Saboteur)
            {
                _destroyedSaboteurs.Add(agentId);
                if (!_allSaboteursAwarded && _destroyedSaboteurs.Count >= SaboteurCount)
                {
                    _allSaboteursAwarded = true;
                    Award(AllSaboteursBonus, ScoreReason.AllSaboteurs, "All Saboteurs destroyed", time);
                }
            }

            return award;
        }

        /// <summary>
        /// Awards the win bonus as four lines: the flat 2000, the time bonus (3000 minus 3 a second,
        /// not below 0), 10 a point of integrity, and 15 a percent of accuracy once at least 10 shots
        /// were fired. Only the first call counts; later calls return an empty list.
        /// </summary>
        /// <param name="runSeconds">Game time the run took, stopped during cutscenes and pause.</param>
        /// <param name="hp">Integrity left, in HP.</param>
        /// <param name="shotsFired">Blaster shots fired in the run.</param>
        /// <param name="shotsHit">Of those, the shots that hit.</param>
        /// <param name="time">Game time in seconds.</param>
        public IReadOnlyList<ScoreAward> Win(float runSeconds, float hp, int shotsFired, int shotsHit, float time)
        {
            var awards = new List<ScoreAward>(4);
            if (_won)
                return awards;
            _won = true;

            awards.Add(Award(WinBasePoints, ScoreReason.WinBase, "Win bonus", time));
            awards.Add(Award(TimeBonus(runSeconds), ScoreReason.WinTime, "Time bonus", time));
            awards.Add(Award(IntegrityBonus(hp), ScoreReason.WinIntegrity, "Integrity bonus", time));
            awards.Add(Award(AccuracyBonus(shotsFired, shotsHit), ScoreReason.WinAccuracy, "Accuracy bonus", time));
            return awards;
        }

        /// <summary><c>max(0, 3000 - 3 x seconds)</c>, rounded to a whole point.</summary>
        public static int TimeBonus(float runSeconds)
        {
            if (float.IsNaN(runSeconds))
                return 0;
            return (int)Math.Round(Math.Max(0f, TimeBonusStart - TimeBonusPerSecond * Math.Max(0f, runSeconds)), MidpointRounding.AwayFromZero);
        }

        /// <summary>10 a point of integrity left, rounded to a whole point; never negative.</summary>
        public static int IntegrityBonus(float hp)
        {
            if (float.IsNaN(hp))
                return 0;
            return (int)Math.Round(Math.Max(0f, hp) * PointsPerHp, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// 15 a percent of accuracy, but only from <see cref="MinShotsForAccuracy"/> shots, so a lucky
        /// single shot is not 100%. Hits above the shots fired are capped.
        /// </summary>
        public static int AccuracyBonus(int shotsFired, int shotsHit)
        {
            if (shotsFired < MinShotsForAccuracy)
                return 0;

            float accuracy = Math.Min(shotsHit, shotsFired) / (float)shotsFired * 100f;
            return (int)Math.Round(Math.Max(0f, accuracy) * PointsPerAccuracyPercent, MidpointRounding.AwayFromZero);
        }

        /// <summary>The grade of a score: S from 14000, A from 11000, B from 8000, C from 5000, otherwise D.</summary>
        public static ScoreGrade GradeFor(int score)
        {
            if (score >= GradeThresholds[0]) return ScoreGrade.S;
            if (score >= GradeThresholds[1]) return ScoreGrade.A;
            if (score >= GradeThresholds[2]) return ScoreGrade.B;
            if (score >= GradeThresholds[3]) return ScoreGrade.C;
            return ScoreGrade.D;
        }

        /// <summary>Starts a new run: score, combo, repeat counts and the one-off bonuses are cleared.</summary>
        public void Reset()
        {
            _history.Clear();
            _takedownsPerAgent.Clear();
            _destroyedSaboteurs.Clear();
            Array.Clear(_byReason, 0, _byReason.Length);
            Score = 0;
            Combo = 1;
            Takedowns = 0;
            _hasTakedown = false;
            _lastTakedownTime = 0f;
            _allSaboteursAwarded = false;
            _won = false;
        }

        // The multiplier for a takedown at this time, and the chain it continues.
        int AdvanceCombo(float time)
        {
            bool inWindow = _hasTakedown && time >= _lastTakedownTime && time - _lastTakedownTime <= ComboWindowSeconds;
            Combo = inWindow ? Math.Min(ComboCap, Combo + 1) : 1;
            _hasTakedown = true;
            _lastTakedownTime = time;
            return Combo;
        }

        ScoreAward Award(int points, ScoreReason reason, string label, float time)
        {
            var award = new ScoreAward(points, reason, label, time);
            Score += points;
            _byReason[(int)reason] += points;
            _history.Add(award);
            Awarded?.Invoke(award);
            return award;
        }
    }
}
