using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace ToyFactory.UI
{
    /// <summary>
    /// The Results screen: "Factory shut down" or "Recalled", the run's time, chapters cleared,
    /// toys disabled and accuracy, the grade and score, the breakdown of the points, the local top 10
    /// and, when the run made the board, a name field. It draws a <see cref="RunSummary"/> and writes
    /// to a <see cref="Leaderboard"/>; it computes no points itself.
    /// </summary>
    public sealed class ResultsView : MonoBehaviour
    {
        const int BreakdownRows = 9;

        Canvas _canvas;
        Text _headline;
        Text _time, _chapters, _toys, _accuracy;
        Text _grade, _score, _rankLine;
        Image _gradeBox;
        Text[] _breakdownLabels;
        Text[] _breakdownPoints;
        Text[] _boardRank, _boardName, _boardScore, _boardGrade;
        GameObject _nameRow;
        InputField _nameField;
        Button _saveButton;
        Text _status;

        RunSummary _summary;
        Leaderboard _board;
        bool _saved;
        bool _focusPending;

        /// <summary>True while the screen is up.</summary>
        public bool IsShowing => _canvas != null && _canvas.gameObject.activeSelf;

        /// <summary>The run on show, or null.</summary>
        public RunSummary Summary => _summary;

        /// <summary>The place the run took after it was saved, or 0 if it was not saved or did not place.</summary>
        public int SavedRank { get; private set; }

        /// <summary>True while the name field is offered: the run would make the board and is not saved yet.</summary>
        public bool CanEnterName => _nameRow != null && _nameRow.activeSelf;

        /// <summary>The headline text, for tests.</summary>
        public string HeadlineText => _headline != null ? _headline.text : string.Empty;

        /// <summary>The grade letter shown, for tests.</summary>
        public string GradeText => _grade != null ? _grade.text : string.Empty;

        /// <summary>The score shown, for tests.</summary>
        public string ScoreText => _score != null ? _score.text : string.Empty;

        /// <summary>The line under the score, for tests.</summary>
        public string StatusText => _status != null ? _status.text : string.Empty;

        /// <summary>The text of one stat line, for tests.</summary>
        public string StatText(ResultsStat stat)
        {
            switch (stat)
            {
                case ResultsStat.Time: return _time != null ? _time.text : string.Empty;
                case ResultsStat.Chapters: return _chapters != null ? _chapters.text : string.Empty;
                case ResultsStat.Toys: return _toys != null ? _toys.text : string.Empty;
                case ResultsStat.Accuracy: return _accuracy != null ? _accuracy.text : string.Empty;
                default: return string.Empty;
            }
        }

        /// <summary>The name in leaderboard row <paramref name="index"/> (0 is first), or empty if the row is empty.</summary>
        public string BoardName(int index) =>
            _boardName != null && index >= 0 && index < Leaderboard.Capacity ? _boardName[index].text : string.Empty;

        /// <summary>The label of breakdown row <paramref name="index"/>, or null if the row is not shown.</summary>
        public string BreakdownLabel(int index) =>
            _breakdownLabels != null && index >= 0 && index < BreakdownRows && _breakdownLabels[index].gameObject.activeSelf
                ? _breakdownLabels[index].text
                : null;

        /// <summary>Raised by the Play again button.</summary>
        public event Action PlayAgainRequested;

        /// <summary>Raised by the Quit button.</summary>
        public event Action QuitRequested;

        /// <summary>Builds the screen, hidden, under this view's object.</summary>
        internal void Build(int sortingOrder)
        {
            _canvas = HudWidgets.ScreenCanvas("Results Canvas", transform, sortingOrder);
            HudWidgets.AllowClicks(_canvas);
            Transform root = _canvas.transform;

            Image backdrop = HudWidgets.Panel("Backdrop", root, new Color(HudWidgets.Plum.r, HudWidgets.Plum.g, HudWidgets.Plum.b, 0.94f));
            backdrop.raycastTarget = true;

            Image panel = HudWidgets.Panel("Panel", root, HudWidgets.PlumPanel);
            HudWidgets.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1760f, 960f));
            Transform p = panel.transform;

            _headline = HudWidgets.Label("Headline", p, string.Empty, 84, HudWidgets.Mint, TextAnchor.MiddleLeft);
            Corner(_headline.rectTransform, 60f, -36f, 1200f, 110f);

            BuildStats(p);
            BuildGrade(p);
            BuildBreakdown(p);
            BuildBoard(p);
            BuildFooter(p);

            _canvas.gameObject.SetActive(false);
        }

        /// <summary>
        /// Shows a finished run. The board is loaded here, so the screen always draws what is on disk;
        /// a name field is offered if the run would place.
        /// </summary>
        public void Show(RunSummary summary, Leaderboard board)
        {
            if (_canvas == null || summary == null)
                return;

            _summary = summary;
            _board = board;
            _saved = false;
            SavedRank = 0;
            _board?.Load();

            _headline.text = summary.Headline;
            _headline.color = summary.Won ? HudWidgets.Mint : HudWidgets.Tomato;
            _time.text = summary.TimeText;
            _chapters.text = summary.ChaptersText;
            _toys.text = summary.Takedowns.ToString(CultureInfo.InvariantCulture);
            _accuracy.text = summary.AccuracyText;
            _grade.text = summary.Grade.ToString();
            _grade.color = GradeColour(summary.Grade);
            _score.text = summary.Score.ToString("N0", CultureInfo.InvariantCulture);

            for (int i = 0; i < BreakdownRows; i++)
            {
                bool shown = i < summary.Breakdown.Count;
                _breakdownLabels[i].gameObject.SetActive(shown);
                _breakdownPoints[i].gameObject.SetActive(shown);
                if (!shown)
                    continue;

                BreakdownLine line = summary.Breakdown[i];
                _breakdownLabels[i].text = line.Count > 1 ? $"{line.Label}  x{line.Count}" : line.Label;
                _breakdownPoints[i].text = "+" + line.Points.ToString("N0", CultureInfo.InvariantCulture);
            }

            bool offerName = _board != null && _board.Qualifies(summary.Score, summary.Seconds);
            _nameRow.SetActive(offerName);
            _nameField.text = string.Empty;
            _status.text = offerName ? string.Empty : NoPlaceText(summary);
            _rankLine.text = string.Empty;
            DrawBoard(-1);

            _canvas.gameObject.SetActive(true);
            _focusPending = offerName;
        }

        /// <summary>Takes the screen away.</summary>
        public void Hide()
        {
            if (_canvas != null)
                _canvas.gameObject.SetActive(false);
        }

        /// <summary>
        /// Puts the shown run on the board under <paramref name="name"/> and saves the file. Does nothing
        /// if the run was saved already or would not place.
        /// </summary>
        /// <returns>The place the run took, or 0 if nothing was added.</returns>
        public int TrySave(string name)
        {
            if (_saved || _summary == null || _board == null)
                return 0;

            int rank = _board.Add(name, _summary.Score, _summary.Grade, _summary.Seconds, _summary.Outcome, DateTime.Now);
            if (rank == 0)
                return 0;

            _saved = true;
            SavedRank = rank;
            bool written = _board.Save();
            _nameRow.SetActive(false);
            _status.text = written ? string.Empty : "The leaderboard file could not be saved.";
            _rankLine.text = $"Rank {rank} of {Leaderboard.Capacity}";
            DrawBoard(rank - 1);
            return rank;
        }

        void Update()
        {
            if (!IsShowing || !_focusPending)
                return;

            // The field takes focus once the event system has had a frame to exist.
            _focusPending = false;
            if (_nameField != null && _nameField.gameObject.activeInHierarchy)
            {
                _nameField.Select();
                _nameField.ActivateInputField();
            }
        }

        static string NoPlaceText(RunSummary summary) =>
            summary.Score <= 0 ? "No points scored, so no place on the board." : "Not high enough for the top 10.";

        static Color GradeColour(ScoreGrade grade)
        {
            switch (grade)
            {
                case ScoreGrade.S: return HudWidgets.Sun;
                case ScoreGrade.A: return HudWidgets.Mint;
                case ScoreGrade.B: return HudWidgets.Cobalt;
                case ScoreGrade.C: return HudWidgets.Ink;
                default: return HudWidgets.Tomato;
            }
        }

        void DrawBoard(int highlightIndex)
        {
            IReadOnlyList<LeaderboardEntry> entries = _board != null ? _board.Entries : null;
            for (int i = 0; i < Leaderboard.Capacity; i++)
            {
                bool filled = entries != null && i < entries.Count;
                Color colour = i == highlightIndex ? HudWidgets.Sun : filled ? HudWidgets.Ink : HudWidgets.InkDim;
                _boardRank[i].text = (i + 1).ToString(CultureInfo.InvariantCulture);
                _boardName[i].text = filled ? entries[i].name : string.Empty;
                _boardScore[i].text = filled ? entries[i].score.ToString("N0", CultureInfo.InvariantCulture) : string.Empty;
                _boardGrade[i].text = filled ? entries[i].grade : string.Empty;
                _boardRank[i].color = colour;
                _boardName[i].color = colour;
                _boardScore[i].color = colour;
                _boardGrade[i].color = colour;
            }
        }

        // ---- Building

        static void Corner(RectTransform rect, float x, float y, float width, float height) =>
            HudWidgets.Place(rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y), new Vector2(width, height));

        static Text MakeLabel(Transform parent, string name, string text, int size, Color colour, TextAnchor anchor,
            float x, float y, float width, float height, FontStyle style = FontStyle.Bold)
        {
            Text label = HudWidgets.Label(name, parent, text, size, colour, anchor, style);
            Corner(label.rectTransform, x, y, width, height);
            return label;
        }

        void BuildStats(Transform p)
        {
            string[] names = { "TIME", "CHAPTERS CLEARED", "TOYS DISABLED", "ACCURACY" };
            Text[] values = new Text[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                float y = -(170f + i * 62f);
                MakeLabel(p, names[i], names[i], 24, HudWidgets.InkDim, TextAnchor.MiddleLeft, 60f, y, 250f, 50f);
                values[i] = MakeLabel(p, names[i] + " value", string.Empty, 32, HudWidgets.Ink, TextAnchor.MiddleRight, 290f, y, 270f, 50f);
                values[i].resizeTextForBestFit = true;
                values[i].resizeTextMinSize = 18;
                values[i].resizeTextMaxSize = 32;
                values[i].horizontalOverflow = HorizontalWrapMode.Wrap;
            }

            _time = values[0];
            _chapters = values[1];
            _toys = values[2];
            _accuracy = values[3];
        }

        void BuildGrade(Transform p)
        {
            _gradeBox = HudWidgets.Panel("Grade Box", p, HudWidgets.PlumLight);
            Corner(_gradeBox.rectTransform, 60f, -450f, 220f, 220f);
            _grade = HudWidgets.Label("Grade", _gradeBox.transform, string.Empty, 170, HudWidgets.Sun, TextAnchor.MiddleCenter);

            MakeLabel(p, "Score label", "SCORE", 24, HudWidgets.InkDim, TextAnchor.MiddleLeft, 310f, -462f, 250f, 40f);
            _score = MakeLabel(p, "Score", string.Empty, 72, HudWidgets.Ink, TextAnchor.MiddleLeft, 310f, -506f, 300f, 90f);
            _rankLine = MakeLabel(p, "Rank", string.Empty, 26, HudWidgets.Sun, TextAnchor.MiddleLeft, 310f, -610f, 300f, 40f);
        }

        void BuildBreakdown(Transform p)
        {
            MakeLabel(p, "Breakdown header", "BREAKDOWN", 26, HudWidgets.Mint, TextAnchor.MiddleLeft, 660f, -170f, 480f, 40f);

            _breakdownLabels = new Text[BreakdownRows];
            _breakdownPoints = new Text[BreakdownRows];
            for (int i = 0; i < BreakdownRows; i++)
            {
                float y = -(222f + i * 46f);
                _breakdownLabels[i] = MakeLabel(p, "Breakdown " + (i + 1), string.Empty, 26, HudWidgets.Ink, TextAnchor.MiddleLeft, 660f, y, 330f, 42f, FontStyle.Normal);
                _breakdownPoints[i] = MakeLabel(p, "Breakdown points " + (i + 1), string.Empty, 26, HudWidgets.Ink, TextAnchor.MiddleRight, 990f, y, 160f, 42f);
                _breakdownLabels[i].gameObject.SetActive(false);
                _breakdownPoints[i].gameObject.SetActive(false);
            }
        }

        void BuildBoard(Transform p)
        {
            MakeLabel(p, "Board header", "TOP 10", 26, HudWidgets.Mint, TextAnchor.MiddleLeft, 1220f, -170f, 480f, 40f);

            _boardRank = new Text[Leaderboard.Capacity];
            _boardName = new Text[Leaderboard.Capacity];
            _boardScore = new Text[Leaderboard.Capacity];
            _boardGrade = new Text[Leaderboard.Capacity];
            for (int i = 0; i < Leaderboard.Capacity; i++)
            {
                float y = -(222f + i * 46f);
                _boardRank[i] = MakeLabel(p, "Rank " + (i + 1), string.Empty, 26, HudWidgets.Ink, TextAnchor.MiddleRight, 1220f, y, 46f, 42f);
                _boardName[i] = MakeLabel(p, "Name " + (i + 1), string.Empty, 26, HudWidgets.Ink, TextAnchor.MiddleLeft, 1286f, y, 230f, 42f, FontStyle.Normal);
                _boardScore[i] = MakeLabel(p, "Score " + (i + 1), string.Empty, 26, HudWidgets.Ink, TextAnchor.MiddleRight, 1516f, y, 130f, 42f);
                _boardGrade[i] = MakeLabel(p, "Grade " + (i + 1), string.Empty, 26, HudWidgets.Ink, TextAnchor.MiddleCenter, 1656f, y, 50f, 42f);
            }
        }

        void BuildFooter(Transform p)
        {
            _status = MakeLabel(p, "Status", string.Empty, 26, HudWidgets.InkDim, TextAnchor.MiddleLeft, 60f, -740f, 1000f, 40f, FontStyle.Normal);

            // The name row: only offered when the run makes the board.
            RectTransform row = HudWidgets.Rect("Name row", p);
            Corner(row, 60f, -780f, 760f, 80f);
            _nameRow = row.gameObject;
            MakeLabel(row, "Name label", "NAME", 24, HudWidgets.InkDim, TextAnchor.MiddleLeft, 0f, -8f, 100f, 64f);

            _nameField = HudWidgets.MakeInput("Name field", row, "Your name", 30, Leaderboard.MaxNameLength);
            Corner(_nameField.GetComponent<RectTransform>(), 110f, -8f, 380f, 64f);
            _nameField.onSubmit.AddListener(_ => TrySave(_nameField.text));

            _saveButton = HudWidgets.MakeButton("Save", row, "SAVE", HudWidgets.Sun, HudWidgets.Plum, 28);
            Corner((RectTransform)_saveButton.transform, 510f, -8f, 190f, 64f);
            _saveButton.onClick.AddListener(() => TrySave(_nameField.text));

            Button again = HudWidgets.MakeButton("Play again", p, "PLAY AGAIN", HudWidgets.Mint, HudWidgets.Plum, 36);
            Corner((RectTransform)again.transform, 1000f, -820f, 360f, 90f);
            again.onClick.AddListener(() => PlayAgainRequested?.Invoke());

            Button quit = HudWidgets.MakeButton("Quit", p, "QUIT", HudWidgets.PlumLight, HudWidgets.Ink, 36);
            Corner((RectTransform)quit.transform, 1400f, -820f, 300f, 90f);
            quit.onClick.AddListener(() => QuitRequested?.Invoke());
        }
    }

    /// <summary>The four stat lines of the Results screen, for tests.</summary>
    public enum ResultsStat
    {
        Time,
        Chapters,
        Toys,
        Accuracy
    }
}
