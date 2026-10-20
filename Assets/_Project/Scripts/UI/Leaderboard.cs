using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ToyFactory.UI
{
    /// <summary>One row of the local leaderboard. Public fields because <see cref="JsonUtility"/> saves it.</summary>
    [Serializable]
    public sealed class LeaderboardEntry
    {
        /// <summary>The name the player typed, cleaned by <see cref="Leaderboard.CleanName"/>.</summary>
        public string name;

        /// <summary>The run's final score.</summary>
        public int score;

        /// <summary>The grade letter.</summary>
        public string grade;

        /// <summary>Game seconds the run took; the tie-break between equal scores.</summary>
        public float seconds;

        /// <summary>"Won" or "Recalled".</summary>
        public string outcome;

        /// <summary>The day the run was set, as yyyy-MM-dd.</summary>
        public string date;
    }

    /// <summary>
    /// The local top-10 leaderboard, saved as JSON (by default in <c>Application.persistentDataPath</c>).
    /// Plain C# over a path so EditMode tests can point it at a temporary folder. It never throws for a
    /// missing, empty, corrupt or unwritable file: <see cref="Load"/> then yields an empty board and
    /// <see cref="Save"/> reports failure.
    /// </summary>
    /// <remarks>
    /// <para><b>Order.</b> Higher score first; equal scores go to the shorter run; if both match, the
    /// older entry stays ahead.</para>
    /// <para><b>The file</b> is written to a temporary file first and then moved over the old one, so a
    /// crash while saving cannot leave half a board behind.</para>
    /// </remarks>
    public sealed class Leaderboard
    {
        /// <summary>Rows kept.</summary>
        public const int Capacity = 10;

        /// <summary>The longest name, in characters.</summary>
        public const int MaxNameLength = 12;

        /// <summary>The name used when the player leaves the field empty.</summary>
        public const string DefaultName = "PLAYER";

        /// <summary>The file name inside the persistent data folder.</summary>
        public const string FileName = "leaderboard.json";

        [Serializable]
        sealed class FileData
        {
            public int version = 1;
            public List<LeaderboardEntry> entries = new List<LeaderboardEntry>();
        }

        readonly string _path;
        readonly List<LeaderboardEntry> _entries = new List<LeaderboardEntry>(Capacity + 1);

        /// <summary>Creates a board saved at <paramref name="path"/>. Nothing is read until <see cref="Load"/>.</summary>
        public Leaderboard(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        /// <summary>The default place of the file.</summary>
        public static string DefaultPath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>The rows, best first.</summary>
        public IReadOnlyList<LeaderboardEntry> Entries => _entries;

        /// <summary>
        /// A name the board can show: control characters removed, trimmed, cut to
        /// <see cref="MaxNameLength"/>, and <see cref="DefaultName"/> if nothing is left.
        /// </summary>
        public static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return DefaultName;

            var builder = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (!char.IsControl(c))
                    builder.Append(c);
            }

            string cleaned = builder.ToString().Trim();
            if (cleaned.Length > MaxNameLength)
                cleaned = cleaned.Substring(0, MaxNameLength).TrimEnd();
            return cleaned.Length == 0 ? DefaultName : cleaned;
        }

        /// <summary>
        /// The 1-based place a run would take, or 0 if it would not make the top
        /// <see cref="Capacity"/>. A score of 0 or less never places.
        /// </summary>
        public int RankFor(int score, float seconds)
        {
            if (score <= 0)
                return 0;

            int index = InsertIndex(score, seconds);
            return index < Capacity ? index + 1 : 0;
        }

        /// <summary>True if a run with this score and time would make the board.</summary>
        public bool Qualifies(int score, float seconds) => RankFor(score, seconds) > 0;

        /// <summary>
        /// Adds a run and keeps the best <see cref="Capacity"/>. The name is cleaned.
        /// </summary>
        /// <returns>The 1-based place the entry took, or 0 if it did not place (nothing is added then).</returns>
        public int Add(string name, int score, ScoreGrade grade, float seconds, RunOutcome outcome, DateTime when)
        {
            int rank = RankFor(score, seconds);
            if (rank == 0)
                return 0;

            var entry = new LeaderboardEntry
            {
                name = CleanName(name),
                score = score,
                grade = grade.ToString(),
                seconds = float.IsNaN(seconds) ? 0f : Mathf.Max(0f, seconds),
                outcome = outcome == RunOutcome.Won ? "Won" : "Recalled",
                date = when.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            };

            _entries.Insert(rank - 1, entry);
            while (_entries.Count > Capacity)
                _entries.RemoveAt(_entries.Count - 1);
            return rank;
        }

        /// <summary>Removes every row (the file is untouched until <see cref="Save"/>).</summary>
        public void Clear() => _entries.Clear();

        /// <summary>
        /// Replaces the rows with the file's. A missing, empty or unreadable file gives an empty board.
        /// Rows from the file are cleaned, sorted and cut to <see cref="Capacity"/>, so a hand-edited
        /// file cannot break the screen.
        /// </summary>
        /// <returns>True if a file was read; false if the board starts empty.</returns>
        public bool Load()
        {
            _entries.Clear();
            try
            {
                if (!File.Exists(_path))
                    return false;

                string json = File.ReadAllText(_path);
                if (string.IsNullOrWhiteSpace(json))
                    return false;

                FileData data = JsonUtility.FromJson<FileData>(json);
                if (data == null || data.entries == null)
                    return false;

                foreach (LeaderboardEntry loaded in data.entries)
                {
                    if (loaded != null)
                        _entries.Add(Sanitise(loaded));
                }

                // OrderBy is stable, so rows that tie completely keep their order from the file.
                List<LeaderboardEntry> ordered = _entries.OrderByDescending(e => e.score).ThenBy(e => e.seconds).ToList();
                _entries.Clear();
                _entries.AddRange(ordered);
                if (_entries.Count > Capacity)
                    _entries.RemoveRange(Capacity, _entries.Count - Capacity);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException
                                      || e is NotSupportedException || e is System.Security.SecurityException)
            {
                _entries.Clear();
                return false;
            }
        }

        /// <summary>Writes the board to its file. False if the file could not be written.</summary>
        public bool Save()
        {
            string temporary = _path + ".tmp";
            try
            {
                string directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var data = new FileData();
                data.entries.AddRange(_entries);
                File.WriteAllText(temporary, JsonUtility.ToJson(data, true));

                if (File.Exists(_path))
                    File.Delete(_path);
                File.Move(temporary, _path);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException
                                      || e is NotSupportedException || e is System.Security.SecurityException)
            {
                return false;
            }
        }

        // Where a new run goes: after every row that beats it or ties it.
        int InsertIndex(int score, float seconds)
        {
            float time = float.IsNaN(seconds) ? 0f : Mathf.Max(0f, seconds);
            int index = _entries.Count;
            for (int i = 0; i < _entries.Count; i++)
            {
                LeaderboardEntry row = _entries[i];
                if (score > row.score || (score == row.score && time < row.seconds))
                {
                    index = i;
                    break;
                }
            }

            return index;
        }

        static LeaderboardEntry Sanitise(LeaderboardEntry entry)
        {
            return new LeaderboardEntry
            {
                name = CleanName(entry.name),
                score = Math.Max(0, entry.score),
                grade = string.IsNullOrEmpty(entry.grade) ? ScoreGrade.D.ToString() : entry.grade,
                seconds = float.IsNaN(entry.seconds) ? 0f : Mathf.Max(0f, entry.seconds),
                outcome = entry.outcome == "Won" ? "Won" : "Recalled",
                date = entry.date ?? string.Empty
            };
        }
    }
}
