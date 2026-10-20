using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Journey.Cutscenes;

namespace ToyFactory.UI
{
    /// <summary>
    /// The in-game HUD, built in code under one object in <c>UI.unity</c>: the objectives panel with
    /// the current task, task counts, relay order chips and the switch lamps, the score panel, the room
    /// name, the integrity, charge and overcharge readouts, the crosshair with its prompt and hold bar,
    /// the key hints, the damage vignette, the arrow to the current objective, the chapter card and the
    /// subtitle bar. Tab folds the objectives panel down to the task in hand and H puts the key hints away
    /// (<see cref="HudSections"/>). It is hidden in cutscenes and outside play. It also hosts the title and results
    /// screens (<see cref="ScreensPresenter"/>), which own the other two game states.
    /// </summary>
    /// <remarks>
    /// <para><b>Reads, never reaches in.</b> Chapter progress comes from <see cref="ChapterEvents"/>
    /// and the chapter manager's definitions, task progress from the props' <see cref="ITask.OnProgress"/>,
    /// the player from <see cref="PlayerState"/>, the game state from <see cref="GameClock"/>, dialogue
    /// from <see cref="DialogueEvents"/>, points from <see cref="ScoreManager"/>. It never reads a brain,
    /// and a missing player, clock, score manager or chapter manager just leaves its part empty.</para>
    /// <para><b>Fed from outside.</b> The room name, the prompt line, the hit marker and the wind-up toy
    /// count have no event in <c>Interfaces</c>; <see cref="SetRoom"/>, <see cref="SetPrompt"/>,
    /// <see cref="ReportHit"/> and <see cref="SetToys"/> are their entry points. The room name is also
    /// shown from the chapter definition when a chapter's card shows, and the prompt, hit marker and toy
    /// dots stay hidden until something feeds them.</para>
    /// <para><b>Canvases.</b> The static part (panels, labels) and the parts that change every frame
    /// (bars, arrow, vignette, the current-task marker, the score panel) sit on separate canvases, so
    /// a moving bar does not rebuild the text batches.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class HudPresenter : MonoBehaviour, IGameStateListener
    {
        const int MaxRows = 8;
        const int MaxChips = 8;
        const int MaxToyDots = 5;
        const float ArrowMargin = 0.08f;
        const float RowHeight = 32f;
        const float PanelHeader = 92f;
        const float ChipsHeight = 44f;
        const float PanelWidth = 560f;
        const float TextX = 54f;
        const float TextWidth = 366f;
        const float CountX = 426f;
        const float BarPanelWidth = 430f;
        const float BarRowStep = 46f;

        [Tooltip("Sorting order of the HUD canvas. The cutscene letterbox is 40; the HUD sits below it.")]
        [SerializeField] int sortingOrder = 20;

        [Tooltip("Sorting order of the subtitle bar. It must sit above the letterbox (40).")]
        [SerializeField] int subtitleSortingOrder = 50;

        readonly HudModel _model = new HudModel();
        readonly List<HudTaskRow> _taskBuffer = new List<HudTaskRow>(MaxRows);
        readonly Dictionary<ITask, Action<float>> _tracked = new Dictionary<ITask, Action<float>>();
        readonly Dictionary<string, float> _holdProgress = new Dictionary<string, float>();
        readonly HashSet<string> _holdIds = new HashSet<string>();

        HudSprites _sprites;
        Canvas _canvas;
        CanvasGroup _group;
        Canvas _dynamic;
        ChapterCardView _card;
        SubtitleView _subtitles;
        ScreensPresenter _screens;
        ScorePanelView _scorePanel;
        RoomBannerView _room;
        PromptView _prompt;

        // Objectives panel.
        RectTransform _panel;
        Text _tagText, _titleText, _lampCount, _chipLabel;
        float[] _rowY;
        Image[] _rowBoxes;
        Text[] _rowTexts;
        Text[] _rowCounts;
        RectTransform _lampRow;
        Image[] _lamps;
        RectTransform _chipRow;
        Image[] _chips;
        Text[] _chipTexts;
        RectTransform _currentMarker;
        Image _currentGlow;
        Text _currentDistance;
        int _drawnVersion = -1;

        // Bars, arrow, crosshair and vignette.
        Image _healthFill, _chargeFill, _overchargeFill;
        Image _overchargeTrack;
        Text _chargeStatus;
        RectTransform _toysRow;
        Image[] _toyDots;
        RectTransform _chargePanel;
        RectTransform _statusRow;
        RectTransform _hintsPlate;
        HudSections _sections;
        RectTransform _arrow;
        Text _arrowDistance;
        Image _vignette;
        Image[] _crosshair;

        RectTransform _integrityPanel;
        RectTransform _crosshairRoot;
        IGameClock _clock;
        GameState _state = GameState.Playing;
        bool _cutsceneActive;
        int _pendingCard;
        bool _visible = true;
        float _lastHealth = 1f;
        float _lastHitAt = float.NegativeInfinity;
        float _lastHitMarkAt = float.NegativeInfinity;
        float _longestOvercharge = 8f;
        bool _markWasShown;
        int _toysCarried = -1;
        int _toysMax;

        /// <summary>The HUD in the loaded scenes, or null before it wakes.</summary>
        public static HudPresenter Current { get; private set; }

        /// <summary>The data behind the objectives panel and the lamps, for tests.</summary>
        public HudModel Model => _model;

        /// <summary>True while the HUD is drawn: in play, not in a cutscene.</summary>
        public bool IsVisible => _visible;

        /// <summary>The chapter card, for tests.</summary>
        public ChapterCardView Card => _card;

        /// <summary>The subtitle bar, for tests.</summary>
        public SubtitleView Subtitles => _subtitles;

        /// <summary>The title and results screens, for tests.</summary>
        public ScreensPresenter Screens => _screens;

        /// <summary>The text of the objectives panel's title line, for tests.</summary>
        public string TitleText => _titleText != null ? _titleText.text : string.Empty;

        /// <summary>The text of the objectives panel's chapter line, for tests.</summary>
        public string ChapterText => _tagText != null ? _tagText.text : string.Empty;

        /// <summary>The fraction the integrity bar shows, for tests.</summary>
        public float IntegrityShown => _healthFill != null ? _healthFill.rectTransform.anchorMax.x : 0f;

        /// <summary>The fraction the charge bar shows, for tests.</summary>
        public float ChargeShown => _chargeFill != null ? _chargeFill.rectTransform.anchorMax.x : 0f;

        /// <summary>The vignette's current opacity, for tests.</summary>
        public float VignetteShown => _vignette != null ? _vignette.color.a : 0f;

        /// <summary>The score panel's score text, for tests.</summary>
        public string ScoreShown => _scorePanel != null ? _scorePanel.ScoreText : string.Empty;

        /// <summary>The score panel's run time text, for tests.</summary>
        public string TimeShown => _scorePanel != null ? _scorePanel.TimeText : string.Empty;

        /// <summary>The score panel's combo text (empty without a combo), for tests.</summary>
        public string ComboShown => _scorePanel != null ? _scorePanel.ComboText : string.Empty;

        /// <summary>The room name last put on the banner, for tests; empty before one was shown.</summary>
        public string RoomShown => _room != null ? _room.Room : string.Empty;

        /// <summary>True while the room banner is up, for tests.</summary>
        public bool RoomBannerShowing => _room != null && _room.IsShowing;

        /// <summary>The prompt line, for tests.</summary>
        public string PromptShown => _prompt != null ? _prompt.Prompt : string.Empty;

        /// <summary>True while the hold bar is drawn, for tests.</summary>
        public bool HoldBarShown => _prompt != null && _prompt.BarVisible;

        /// <summary>The fraction the hold bar shows, for tests.</summary>
        public float HoldBarFraction => _prompt != null ? _prompt.BarFraction : 0f;

        /// <summary>The line beside the charge bar ("OVERCHARGE 8s", "Charging..." or empty), for tests.</summary>
        public string ChargeStatusShown => _chargeStatus != null ? _chargeStatus.text : string.Empty;

        /// <summary>How yellow the crosshair is, 0 to 1, for tests.</summary>
        public float CrosshairHitAmount => HudMath.HitMarkerAmount(Time.unscaledTime - _lastHitMarkAt);

        /// <summary>How many wind-up toys are carried; -1 while nothing has reported a count, for tests.</summary>
        public int ToysShown => _toysCarried;

        /// <summary>True while the objectives panel is folded down to the task in hand.</summary>
        public bool ObjectivesCollapsed => _sections != null && _sections.ObjectivesCollapsed;

        /// <summary>True while the key hints are put away.</summary>
        public bool HintsHidden => _sections != null && _sections.HintsHidden;

        /// <summary>The objectives panel's height in layout pixels, for tests.</summary>
        public float ObjectivesHeight => _panel != null ? _panel.sizeDelta.y : 0f;

        /// <summary>The label on the objectives panel's key chip ("TAB  LESS" or "TAB  MORE"), for tests.</summary>
        public string ObjectivesChipText => _chipLabel != null ? _chipLabel.text : string.Empty;

        /// <summary>The height of the charge panel in layout pixels, for tests.</summary>
        public float ChargePanelHeight => _chargePanel != null ? _chargePanel.sizeDelta.y : 0f;

        /// <summary>The rectangles of the panels and lines that are on screen, by name, for the layout tests.</summary>
        public IEnumerable<KeyValuePair<string, RectTransform>> LayoutRects()
        {
            if (_panel != null && _panel.gameObject.activeInHierarchy)
                yield return new KeyValuePair<string, RectTransform>("Objectives", _panel);
            if (_chargePanel != null && _chargePanel.gameObject.activeInHierarchy)
                yield return new KeyValuePair<string, RectTransform>("Charge", _chargePanel);
            if (_hintsPlate != null && _hintsPlate.gameObject.activeInHierarchy)
                yield return new KeyValuePair<string, RectTransform>("Key hints", _hintsPlate);
            foreach (KeyValuePair<string, RectTransform> pair in _scorePanel.Rects())
                yield return pair;
            foreach (KeyValuePair<string, RectTransform> pair in _room.Rects())
                yield return pair;
            foreach (KeyValuePair<string, RectTransform> pair in _prompt.Rects())
                yield return pair;
            foreach (KeyValuePair<string, RectTransform> pair in _card.Rects())
                yield return pair;
            if (_integrityPanel != null && _integrityPanel.gameObject.activeInHierarchy)
                yield return new KeyValuePair<string, RectTransform>("Integrity", _integrityPanel);
            if (_crosshairRoot != null && _crosshairRoot.gameObject.activeInHierarchy)
                yield return new KeyValuePair<string, RectTransform>("Crosshair", _crosshairRoot);
            if (_screens != null && _screens.CheckpointToast != null)
            {
                foreach (KeyValuePair<string, RectTransform> pair in _screens.CheckpointToast.Rects())
                    yield return pair;
            }
        }

        /// <summary>True while the panel's current-task marker is drawn, for tests.</summary>
        public bool CurrentMarkerShown => _currentMarker != null && _currentMarker.gameObject.activeSelf;

        /// <summary>The distance text next to the current task, for tests.</summary>
        public string CurrentDistanceShown => _currentDistance != null ? _currentDistance.text : string.Empty;

        /// <summary>The text of row <paramref name="index"/>, or null if it is not shown.</summary>
        public string RowText(int index) =>
            _rowTexts != null && index >= 0 && index < MaxRows && _rowTexts[index].gameObject.activeSelf ? _rowTexts[index].text : null;

        /// <summary>The count of row <paramref name="index"/> ("1/3"), or empty if it has none.</summary>
        public string RowCount(int index) =>
            _rowCounts != null && index >= 0 && index < MaxRows && _rowCounts[index].gameObject.activeSelf ? _rowCounts[index].text : string.Empty;

        /// <summary>The opacity of row <paramref name="index"/>'s text, for tests.</summary>
        public float RowAlpha(int index) =>
            _rowTexts != null && index >= 0 && index < MaxRows ? _rowTexts[index].color.a : 0f;

        /// <summary>The number on relay chip <paramref name="index"/>, or null if it is not shown.</summary>
        public string ChipText(int index) =>
            _chipTexts != null && index >= 0 && index < MaxChips && _chipTexts[index].gameObject.activeSelf ? _chipTexts[index].text : null;

        /// <summary>The colour of relay chip <paramref name="index"/>, for tests.</summary>
        public Color ChipColour(int index) =>
            _chips != null && index >= 0 && index < MaxChips ? _chips[index].color : Color.clear;

        void Awake()
        {
            if (Current != null && Current != this)
                Debug.LogWarning($"More than one {nameof(HudPresenter)} is loaded; using the one on {name}.", this);

            Current = this;
            _sprites = new HudSprites();
            _sections = new HudSections();
            Build();
        }

        void OnEnable()
        {
            ChapterEvents.OnChapterStarted += HandleChapterStarted;
            ChapterEvents.OnTaskCompleted += HandleTaskCompleted;
            ChapterEvents.OnSwitchUnsealed += HandleSwitchUnsealed;
            ChapterEvents.OnSwitchRestored += HandleSwitchRestored;
            CutsceneEvents.OnCutsceneStarted += HandleCutsceneStarted;
            CutsceneEvents.OnCutsceneEnded += HandleCutsceneEnded;
            TaskEvents.OnTaskSpawned += Track;
        }

        void OnDisable()
        {
            ChapterEvents.OnChapterStarted -= HandleChapterStarted;
            ChapterEvents.OnTaskCompleted -= HandleTaskCompleted;
            ChapterEvents.OnSwitchUnsealed -= HandleSwitchUnsealed;
            ChapterEvents.OnSwitchRestored -= HandleSwitchRestored;
            CutsceneEvents.OnCutsceneStarted -= HandleCutsceneStarted;
            CutsceneEvents.OnCutsceneEnded -= HandleCutsceneEnded;
            TaskEvents.OnTaskSpawned -= Track;
            UntrackAll();
            ReleaseClock();
        }

        void OnDestroy()
        {
            if (Current == this)
                Current = null;
            _sprites?.Dispose();
        }

        // ---- Fed from outside

        /// <summary>
        /// Announces a room on the banner at the top centre. Nothing publishes the room the player is in
        /// yet, so the HUD also calls this with the chapter's area when the chapter's card shows.
        /// </summary>
        public void SetRoom(string room) => _room.Show(room);

        /// <summary>Sets the prompt line under the crosshair, such as "Press E to use the lever"; null or empty hides it.</summary>
        public void SetPrompt(string text) => _prompt.SetPrompt(text);

        /// <summary>The player's blaster hit something: the crosshair turns yellow for a moment.</summary>
        public void ReportHit() => _lastHitMarkAt = Time.unscaledTime;

        /// <summary>Sets how many wind-up toys the player carries of how many it can carry; the dots show from then on.</summary>
        public void SetToys(int carried, int max)
        {
            max = Mathf.Clamp(max, 0, MaxToyDots);
            carried = Mathf.Clamp(carried, 0, max);
            if (carried == _toysCarried && max == _toysMax)
                return;

            _toysCarried = carried;
            _toysMax = max;
            bool known = max > 0;
            LayoutCharge();
            for (int i = 0; i < _toyDots.Length; i++)
            {
                _toyDots[i].gameObject.SetActive(i < max);
                _toyDots[i].color = i < carried ? HudWidgets.Sun : HudWidgets.PlumLight;
            }
        }

        /// <summary>Folds the objectives panel down to the task in hand, or opens it again. Tab does this in play.</summary>
        public void ToggleObjectives()
        {
            _sections.ToggleObjectives();
            _drawnVersion = -1;
        }

        /// <summary>Puts the key hints away, or brings them back. H does this in play.</summary>
        public void ToggleHints()
        {
            _sections.ToggleHints();
            _hintsPlate.gameObject.SetActive(!_sections.HintsHidden);
        }

        // Tab and H, only while the game is being played: not on a menu, not in a cutscene.
        void HandleSectionKeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || _state != GameState.Playing)
                return;

            if (keyboard.tabKey.wasPressedThisFrame)
                ToggleObjectives();
            if (keyboard.hKey.wasPressedThisFrame)
                ToggleHints();
        }

        // ---- Events

        void HandleChapterStarted(int chapter)
        {
            ChapterFlow flow = ChapterManager.Current != null ? ChapterManager.Current.Flow : null;
            if (flow == null)
                return;

            ShowChapter(flow, chapter);
            AnnounceChapter(chapter);
        }

        // The card waits for a cutscene to finish, so it is not shown behind the hidden HUD. A chapter
        // can start just before the cutscene that introduces it begins, or while it is ending.
        void AnnounceChapter(int chapter)
        {
            if (_cutsceneActive)
                _pendingCard = chapter;
            else
                ShowCard(chapter);
        }

        void ShowCard(int chapter)
        {
            ChapterFlow flow = ChapterManager.Current != null ? ChapterManager.Current.Flow : null;
            ChapterDefinition definition = flow != null ? flow.GetDefinition(chapter) : null;
            if (definition == null)
                return;

            _card.Show(chapter, definition.Title, definition.Subtitle);
            _room.Show(definition.Area);
        }

        void HandleTaskCompleted(string taskId)
        {
            _model.CompleteTask(taskId);
            _holdProgress.Remove(taskId);
        }

        void HandleSwitchUnsealed(int number)
        {
            if (number >= 1 && number <= ChapterEvents.SwitchCount && _model.Lamp(number) == LampState.Sealed)
                _model.SetLamp(number, LampState.Ready);
        }

        void HandleSwitchRestored(int number)
        {
            if (number >= 1 && number <= ChapterEvents.SwitchCount)
                _model.SetLamp(number, LampState.Restored);
        }

        void HandleCutsceneStarted(string id)
        {
            _cutsceneActive = true;
            if (_card.IsShowing)
                _pendingCard = _card.Chapter;
            _card.Hide();
            _room.Hide();
        }

        void HandleCutsceneEnded(string id)
        {
            _cutsceneActive = false;
            if (_pendingCard > 0)
            {
                int chapter = _pendingCard;
                _pendingCard = 0;
                ShowCard(chapter);
            }
        }

        /// <inheritdoc />
        public void OnGameStateChanged(GameState previous, GameState current) => _state = current;

        // ---- Task progress

        // Props announce progress on their own ITask. They are found once per chapter (they exist from
        // the start) and when a prop is spawned during play, never every frame.
        void TrackSceneTasks()
        {
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is ITask task)
                    Track(task);
            }
        }

        void Track(ITask task)
        {
            if (task == null || _tracked.ContainsKey(task))
                return;

            string id = task.Id;
            Action<float> handler = progress => HandleProgress(id, progress);
            _tracked.Add(task, handler);
            task.OnProgress += handler;
        }

        void UntrackAll()
        {
            foreach (KeyValuePair<ITask, Action<float>> pair in _tracked)
            {
                if (!(pair.Key is UnityEngine.Object o) || o != null)
                    pair.Key.OnProgress -= pair.Value;
            }

            _tracked.Clear();
        }

        void HandleProgress(string taskId, float progress)
        {
            _model.SetProgress(taskId, progress);
            if (_holdIds.Contains(taskId))
                _holdProgress[taskId] = progress;
        }

        // ---- Chapter data

        // Fills the panel for a chapter from the journey's definitions, including tasks that were
        // already done (pickups count before their chapter starts).
        void ShowChapter(ChapterFlow flow, int chapter)
        {
            ChapterDefinition definition = flow.GetDefinition(chapter);
            if (definition == null)
                return;

            SyncLamps(flow);
            _holdIds.Clear();
            _holdProgress.Clear();
            foreach (TaskDefinition any in flow.AllTasks)
            {
                if (any.Type == TaskType.Hold || any.Type == TaskType.HoldAt)
                    _holdIds.Add(any.TaskId);
            }

            _taskBuffer.Clear();
            IReadOnlyList<TaskDefinition> tasks = definition.Tasks;
            IReadOnlyList<int> order = null;
            string sequenceId = null;
            for (int i = 0; i < tasks.Count && i < MaxRows - 1; i++)
            {
                bool done = flow.IsTaskComplete(tasks[i].TaskId);
                int total = tasks[i].Count;
                _taskBuffer.Add(new HudTaskRow(tasks[i].TaskId, tasks[i].DisplayName, done, total, done ? total : 0));
                if (tasks[i].Type == TaskType.Sequence && flow.TryGetSequenceOrder(tasks[i].TaskId, out IReadOnlyList<int> found))
                {
                    sequenceId = tasks[i].TaskId;
                    order = found;
                }
            }

            _model.StartChapter(chapter, definition.Title, definition.Subtitle, _taskBuffer, definition.HasSwitch ? definition.SwitchNumber : 0);
            _model.SetSequence(sequenceId, order);
            TrackSceneTasks();
        }

        // The lamps follow each chapter's phase, so a HUD that wakes up late still shows the truth.
        void SyncLamps(ChapterFlow flow)
        {
            for (int chapter = 1; chapter <= flow.ChapterCount; chapter++)
            {
                ChapterDefinition definition = flow.GetDefinition(chapter);
                if (definition == null || !definition.HasSwitch)
                    continue;

                ChapterPhase phase = flow.GetPhase(chapter);
                LampState lamp = phase == ChapterPhase.TasksDone ? LampState.Ready
                    : phase == ChapterPhase.SwitchRestored || phase == ChapterPhase.Transition || phase == ChapterPhase.Done
                        ? LampState.Restored
                        : LampState.Sealed;
                _model.SetLamp(definition.SwitchNumber, lamp);
            }
        }

        // ---- Per frame

        void Update()
        {
            TrackClock();
            SyncChapterIfBehind();

            bool visible = HudModel.IsVisible(_state, _cutsceneActive);
            if (visible != _visible)
            {
                _visible = visible;
                _canvas.enabled = visible;
            }

            if (!_visible)
                return;

            HandleSectionKeys();

            ChapterManager manager = ChapterManager.Current;
            _model.SetCurrentTask(manager != null && manager.CurrentBeaconTarget.HasValue ? manager.CurrentBeaconTarget.Value.TaskId : null);

            if (_model.Version != _drawnVersion)
                RefreshObjectives();

            IPlayerState player = LivePlayer();
            UpdateBars(player);
            UpdateVignette(player);
            UpdateArrow(player);
            UpdateCrosshair();
            UpdateHoldBar();
            _scorePanel.Refresh(ScoreManager.Current);
            _room.Tick();
        }

        // The HUD may wake after the journey began; catch up once instead of waiting for the next chapter.
        void SyncChapterIfBehind()
        {
            ChapterManager manager = ChapterManager.Current;
            ChapterFlow flow = manager != null ? manager.Flow : null;
            if (flow == null || !flow.HasBegun || flow.CurrentChapter == _model.Chapter || flow.CurrentChapter < 1)
                return;

            ShowChapter(flow, flow.CurrentChapter);
            AnnounceChapter(flow.CurrentChapter);
        }

        void TrackClock()
        {
            IGameClock current = GameClock.Current;
            if (current is UnityEngine.Object unityObject && unityObject == null)
                current = null;

            if (ReferenceEquals(current, _clock))
                return;

            ReleaseClock();
            _clock = current;
            if (_clock != null)
            {
                _clock.AddListener(this);
                _state = _clock.State;
            }
            else
            {
                _state = GameState.Playing;
            }
        }

        void ReleaseClock()
        {
            if (_clock != null && !(_clock is UnityEngine.Object o && o == null))
                _clock.RemoveListener(this);
            _clock = null;
        }

        static IPlayerState LivePlayer()
        {
            IPlayerState player = PlayerState.Current;
            if (player == null || (player is UnityEngine.Object unityObject && unityObject == null))
                return null;
            return player;
        }

        void RefreshObjectives()
        {
            _drawnVersion = _model.Version;
            bool compact = _sections.ObjectivesCollapsed;
            IReadOnlyList<HudTaskRow> rows = _model.Rows;

            string tag = _model.Chapter > 0 ? HudMath.ChapterTag(_model.Chapter, ChapterEvents.ChapterCount) : string.Empty;
            if (compact && rows.Count > 0)
                tag += "   " + HudSections.Summary(_model.RowsDone, rows.Count);
            _tagText.text = tag;
            _titleText.text = _model.Title;
            _chipLabel.text = HudSections.ObjectivesChip(compact);

            // The folded panel keeps one row: the task in hand. Each row is as tall as its text needs, so a long
            // task wraps onto a second line at the normal size instead of shrinking.
            int focus = compact ? HudSections.CompactRow(rows, _model.CurrentTaskId) : -1;
            float y = PanelHeader;
            int currentRow = -1;
            for (int i = 0; i < MaxRows; i++)
            {
                bool shown = i < rows.Count && (!compact || i == focus);
                _rowBoxes[i].gameObject.SetActive(shown);
                _rowTexts[i].gameObject.SetActive(shown);
                _rowCounts[i].gameObject.SetActive(shown && rows[i].HasCount && !rows[i].Done);
                if (!shown)
                    continue;

                HudTaskRow row = rows[i];
                bool current = !row.Done && row.Id == _model.CurrentTaskId;
                if (current)
                    currentRow = i;

                // The switch row is faded while its switch is sealed.
                bool sealedSwitch = row.Id.StartsWith(HudModel.SwitchRowPrefix, StringComparison.Ordinal)
                    && _model.SwitchNumber > 0 && _model.Lamp(_model.SwitchNumber) == LampState.Sealed;
                float alpha = sealedSwitch ? HudMath.SealedRowAlpha : 1f;

                _rowBoxes[i].color = WithAlpha(row.Done ? HudWidgets.Mint : current ? HudWidgets.Sun : HudWidgets.PlumLight, alpha);
                _rowTexts[i].text = row.Text;
                _rowTexts[i].color = WithAlpha(row.Done ? HudWidgets.InkDim : HudWidgets.Ink, alpha);
                if (_rowCounts[i].gameObject.activeSelf)
                    _rowCounts[i].text = HudMath.CountText(row.Count, row.Total);

                float height = Mathf.Max(RowHeight, Mathf.Ceil(_rowTexts[i].preferredHeight) + 6f);
                _rowY[i] = y;
                HudWidgets.Place(_rowBoxes[i].rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(HudWidgets.Pad, -(y + 4f)), new Vector2(18f, 18f));
                HudWidgets.Place(_rowTexts[i].rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(TextX, -y), new Vector2(TextWidth, height));
                HudWidgets.Place(_rowCounts[i].rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(CountX, -y), new Vector2(44f, RowHeight));
                y += height;
            }

            // The current task gets a glowing box and its distance; the marker is moved onto its row.
            _currentMarker.gameObject.SetActive(currentRow >= 0);
            if (currentRow >= 0)
                _currentMarker.anchoredPosition = new Vector2(0f, -_rowY[currentRow]);

            for (int i = 0; i < _lamps.Length; i++)
            {
                LampState state = _model.Lamp(i + 1);
                _lamps[i].color = state == LampState.Restored ? HudWidgets.Mint : state == LampState.Ready ? HudWidgets.Sun : HudWidgets.PlumLight;
            }

            _lampCount.text = $"{_model.SwitchesRestored} of {ChapterEvents.SwitchCount}";

            // Relay order chips: the order the relays must be pressed in, the next one lit.
            IReadOnlyList<int> order = _model.SequenceOrder;
            int step = _model.SequenceStep;
            bool chipsShown = order.Count > 0;
            _chipRow.gameObject.SetActive(chipsShown);
            for (int i = 0; i < MaxChips; i++)
            {
                bool shown = chipsShown && i < order.Count;
                _chips[i].gameObject.SetActive(shown);
                _chipTexts[i].gameObject.SetActive(shown);
                if (!shown)
                    continue;

                _chipTexts[i].text = order[i].ToString(CultureInfo.InvariantCulture);
                _chips[i].color = i < step ? HudWidgets.Mint : i == step ? HudWidgets.Sun : HudWidgets.PlumLight;
                _chipTexts[i].color = i <= step ? HudWidgets.Plum : HudWidgets.Ink;
            }

            float rowsHeight = y - PanelHeader;
            float chipsHeight = chipsShown ? ChipsHeight : 0f;
            _chipRow.anchoredPosition = new Vector2(0f, -(PanelHeader + rowsHeight + 4f));
            _lampRow.anchoredPosition = new Vector2(0f, -(PanelHeader + rowsHeight + chipsHeight + 10f));
            _panel.sizeDelta = new Vector2(PanelWidth, PanelHeader + rowsHeight + chipsHeight + 10f + 50f);
        }

        static Color WithAlpha(Color colour, float alpha)
        {
            colour.a *= alpha;
            return colour;
        }

        void UpdateBars(IPlayerState player)
        {
            float health = player != null ? HudMath.Fraction(player.HealthFraction) : 0f;
            float charge = player != null ? HudMath.Fraction(player.AmmoFraction) : 0f;
            float overcharge = player != null ? Mathf.Max(0f, player.OverchargeTimeLeft) : 0f;
            bool reloading = player != null && player.IsReloading;

            HudWidgets.SetFill(_healthFill, health);
            _healthFill.color = health > 0.5f ? HudWidgets.Mint : health > 0.25f ? HudWidgets.Sun : HudWidgets.Tomato;
            HudWidgets.SetFill(_chargeFill, charge);
            _chargeFill.color = charge > 0.2f ? HudWidgets.Cobalt : HudWidgets.Tomato;

            if (overcharge > _longestOvercharge)
                _longestOvercharge = overcharge;
            bool overcharged = overcharge > 0f;
            if (_overchargeTrack.gameObject.activeSelf != overcharged)
                _overchargeTrack.gameObject.SetActive(overcharged);
            if (overcharged)
                HudWidgets.SetFill(_overchargeFill, HudMath.OverchargeFraction(overcharge, _longestOvercharge));

            // The text changes once a second while overcharged, so it is compared before it is set.
            string status = HudMath.ChargeStatus(overcharge, reloading);
            if (_chargeStatus.text != status)
            {
                bool rowChanges = (status.Length > 0) != (_chargeStatus.text.Length > 0);
                _chargeStatus.text = status;
                if (rowChanges)
                    LayoutCharge();
            }

            _chargeStatus.color = overcharged ? HudWidgets.Sun : HudWidgets.Cobalt;
        }

        void UpdateVignette(IPlayerState player)
        {
            float health = player != null ? HudMath.Fraction(player.HealthFraction) : 1f;
            if (health < _lastHealth - 0.001f)
                _lastHitAt = Time.unscaledTime;
            _lastHealth = health;

            float alpha = HudMath.VignetteAlpha(Time.unscaledTime - _lastHitAt, health);
            Color colour = HudWidgets.Tomato;
            colour.a = alpha;
            _vignette.color = colour;
            _vignette.enabled = alpha > 0.002f;
        }

        // The crosshair turns yellow for a moment after a hit; it is only redrawn while that lasts.
        void UpdateCrosshair()
        {
            float amount = HudMath.HitMarkerAmount(Time.unscaledTime - _lastHitMarkAt);
            if (amount <= 0f && !_markWasShown)
                return;

            _markWasShown = amount > 0f;
            Color baseColour = HudWidgets.Ink;
            baseColour.a = 0.85f;
            Color colour = Color.Lerp(baseColour, HudWidgets.Sun, amount);
            for (int i = 0; i < _crosshair.Length; i++)
                _crosshair[i].color = colour;
        }

        // The hold bar follows the hold task furthest along.
        void UpdateHoldBar()
        {
            float best = 0f;
            foreach (KeyValuePair<string, float> pair in _holdProgress)
                best = Mathf.Max(best, pair.Value);
            _prompt.SetHold(best);
        }

        void UpdateArrow(IPlayerState player)
        {
            ChapterManager manager = ChapterManager.Current;
            Camera camera = Camera.main;
            if (manager == null || !manager.CurrentBeaconTarget.HasValue || camera == null)
            {
                SetArrowVisible(false);
                _currentDistance.text = string.Empty;
                return;
            }

            Vector3 target = manager.CurrentBeaconTarget.Value.Position;
            Vector3 from = player != null ? player.Position : camera.transform.position;
            string distance = HudMath.DistanceText(Vector3.Distance(from, target));
            _currentDistance.text = distance;
            _currentGlow.color = WithAlpha(HudWidgets.Sun, HudMath.GlowAlpha(Time.unscaledTime));

            if (!HudMath.TryPlaceArrow(camera.WorldToViewportPoint(target), ArrowMargin, out Vector2 edge, out float angle))
            {
                SetArrowVisible(false);
                return;
            }

            SetArrowVisible(true);
            _arrow.anchorMin = edge;
            _arrow.anchorMax = edge;
            _arrow.anchoredPosition = Vector2.zero;
            _arrow.localRotation = Quaternion.Euler(0f, 0f, angle);

            // The distance label stays upright and sits just inside the arrow, towards the screen centre.
            Vector2 inward = (new Vector2(0.5f, 0.5f) - edge).normalized * 64f;
            _arrowDistance.rectTransform.anchorMin = edge;
            _arrowDistance.rectTransform.anchorMax = edge;
            _arrowDistance.rectTransform.anchoredPosition = inward;
            _arrowDistance.text = distance;
        }

        void SetArrowVisible(bool visible)
        {
            if (_arrow.gameObject.activeSelf != visible)
            {
                _arrow.gameObject.SetActive(visible);
                _arrowDistance.gameObject.SetActive(visible);
            }
        }

        // ---- Building

        void Build()
        {
            _canvas = HudWidgets.ScreenCanvas("HUD Canvas", transform, sortingOrder);
            _group = _canvas.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

            BuildObjectivesPanel(_canvas.transform);
            BuildIntegrityPanel(_canvas.transform);
            BuildChargePanel(_canvas.transform);
            BuildCrosshair(_canvas.transform);
            BuildHints(_canvas.transform);

            _scorePanel = new ScorePanelView();
            _scorePanel.Build(_canvas.transform);
            _room = new RoomBannerView();
            _room.Build(_canvas.transform);
            _prompt = new PromptView();
            _prompt.Build(_canvas.transform);

            // Everything that moves every frame lives on its own canvas.
            var dynamicRoot = HudWidgets.Rect("Dynamic", _canvas.transform);
            _dynamic = dynamicRoot.gameObject.AddComponent<Canvas>();
            BuildDynamic(dynamicRoot);

            RectTransform cardRoot = HudWidgets.Rect("Chapter Card", _canvas.transform);
            _card = cardRoot.gameObject.AddComponent<ChapterCardView>();
            _card.Build(_sprites);

            var subtitleObject = new GameObject("Subtitles");
            subtitleObject.transform.SetParent(transform, false);
            _subtitles = subtitleObject.AddComponent<SubtitleView>();
            _subtitles.Build(subtitleSortingOrder);

            var screensObject = new GameObject("Screens");
            screensObject.transform.SetParent(transform, false);
            _screens = screensObject.AddComponent<ScreensPresenter>();
        }

        void BuildObjectivesPanel(Transform parent)
        {
            Image background = HudWidgets.Plate("Objectives", parent);
            _panel = background.rectTransform;
            HudWidgets.Place(_panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -32f), new Vector2(PanelWidth, 200f));

            _tagText = HudWidgets.Label("Chapter", _panel, string.Empty, HudWidgets.Caption, HudWidgets.Mint, TextAnchor.MiddleLeft);
            HudWidgets.Place(_tagText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(HudWidgets.Pad, -14f), new Vector2(PanelWidth - HudWidgets.Pad - 170f, 28f));
            _titleText = HudWidgets.Label("Title", _panel, string.Empty, 34, HudWidgets.Ink, TextAnchor.MiddleLeft);
            _titleText.resizeTextForBestFit = true;
            _titleText.resizeTextMinSize = 24;
            _titleText.resizeTextMaxSize = 34;
            HudWidgets.Place(_titleText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(HudWidgets.Pad, -42f), new Vector2(PanelWidth - 2f * HudWidgets.Pad, 44f));

            // The key chip that says what Tab does: fold the list down to the task in hand, or open it again.
            Image chip = HudWidgets.Panel("Fold key", _panel, HudWidgets.PlumLight);
            HudWidgets.Place(chip.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-HudWidgets.Pad, -13f), new Vector2(140f, 30f));
            _chipLabel = HudWidgets.Label("Text", chip.transform, HudSections.ObjectivesChip(false), HudWidgets.Caption, HudWidgets.InkDim, TextAnchor.MiddleCenter);

            _rowBoxes = new Image[MaxRows];
            _rowTexts = new Text[MaxRows];
            _rowCounts = new Text[MaxRows];
            _rowY = new float[MaxRows];
            for (int i = 0; i < MaxRows; i++)
            {
                float y = -(PanelHeader + i * RowHeight);
                _rowBoxes[i] = HudWidgets.Panel("Box " + (i + 1), _panel, HudWidgets.PlumLight);
                HudWidgets.Place(_rowBoxes[i].rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(HudWidgets.Pad, y - 4f), new Vector2(18f, 18f));
                _rowTexts[i] = HudWidgets.Label("Task " + (i + 1), _panel, string.Empty, HudWidgets.Body, HudWidgets.Ink, TextAnchor.UpperLeft, FontStyle.Normal);
                _rowTexts[i].horizontalOverflow = HorizontalWrapMode.Wrap;
                _rowTexts[i].verticalOverflow = VerticalWrapMode.Overflow;
                HudWidgets.Place(_rowTexts[i].rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(TextX, y), new Vector2(TextWidth, RowHeight));
                _rowCounts[i] = HudWidgets.Label("Count " + (i + 1), _panel, string.Empty, HudWidgets.Body, HudWidgets.Sun, TextAnchor.UpperRight);
                HudWidgets.Place(_rowCounts[i].rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(CountX, y), new Vector2(44f, RowHeight));
                _rowBoxes[i].gameObject.SetActive(false);
                _rowTexts[i].gameObject.SetActive(false);
                _rowCounts[i].gameObject.SetActive(false);
            }

            // The relay order chips sit under the last row; the row moves with the list.
            _chipRow = HudWidgets.Rect("Order", _panel);
            HudWidgets.Place(_chipRow, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -140f), new Vector2(PanelWidth, ChipsHeight));
            Text orderLabel = HudWidgets.Label("Label", _chipRow, "ORDER", HudWidgets.Caption, HudWidgets.InkDim, TextAnchor.MiddleLeft);
            HudWidgets.Place(orderLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(HudWidgets.Pad, 0f), new Vector2(80f, 30f));
            _chips = new Image[MaxChips];
            _chipTexts = new Text[MaxChips];
            for (int i = 0; i < MaxChips; i++)
            {
                _chips[i] = HudWidgets.Panel("Chip " + (i + 1), _chipRow, HudWidgets.PlumLight);
                HudWidgets.Place(_chips[i].rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(110f + i * 44f, 0f), new Vector2(36f, 36f));
                _chipTexts[i] = HudWidgets.Label("Number", _chips[i].transform, string.Empty, HudWidgets.Body, HudWidgets.Ink, TextAnchor.MiddleCenter);
                _chips[i].gameObject.SetActive(false);
            }

            _chipRow.gameObject.SetActive(false);

            // The switch lamps sit under the last row; the row moves with the list.
            _lampRow = HudWidgets.Rect("Switches", _panel);
            HudWidgets.Place(_lampRow, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -140f), new Vector2(PanelWidth, 40f));
            Text label = HudWidgets.Label("Label", _lampRow, "SWITCHES", HudWidgets.Caption, HudWidgets.InkDim, TextAnchor.MiddleLeft);
            HudWidgets.Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(HudWidgets.Pad, 0f), new Vector2(130f, 30f));

            _lamps = new Image[ChapterEvents.SwitchCount];
            for (int i = 0; i < _lamps.Length; i++)
            {
                _lamps[i] = HudWidgets.Panel("Lamp " + (i + 1), _lampRow, HudWidgets.PlumLight, _sprites.Circle);
                HudWidgets.Place(_lamps[i].rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(170f + i * 36f, 0f), new Vector2(26f, 26f));
            }

            _lampCount = HudWidgets.Label("Count", _lampRow, "0 of 3", HudWidgets.Body, HudWidgets.Ink, TextAnchor.MiddleLeft);
            HudWidgets.Place(_lampCount.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(290f, 0f), new Vector2(120f, 30f));

            // The current task's glow and distance, on a nested canvas because the distance changes every
            // frame. It is moved onto the current task's row whenever the rows are redrawn.
            _currentMarker = HudWidgets.Rect("Current task", _panel);
            HudWidgets.Place(_currentMarker, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(PanelWidth, RowHeight));
            _currentMarker.gameObject.AddComponent<Canvas>();
            _currentGlow = HudWidgets.Panel("Glow", _currentMarker, HudWidgets.Sun, _sprites.Circle);
            HudWidgets.Place(_currentGlow.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(33f, -13f), new Vector2(34f, 34f));
            _currentDistance = HudWidgets.Label("Distance", _currentMarker, string.Empty, HudWidgets.Body, HudWidgets.Sun, TextAnchor.UpperRight);
            HudWidgets.Place(_currentDistance.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-HudWidgets.Pad, 0f), new Vector2(80f, RowHeight));
            _currentMarker.gameObject.SetActive(false);
        }

        // Integrity, bottom left.
        void BuildIntegrityPanel(Transform parent)
        {
            Image background = HudWidgets.Plate("Integrity", parent);
            _integrityPanel = background.rectTransform;
            HudWidgets.Place(_integrityPanel, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(32f, 32f), new Vector2(BarPanelWidth, 66f));
            BuildBarRow(background.transform, "INTEGRITY", 0, out _, out _, out _healthFill);
        }

        // Charge, the overcharge or charging line and the wind-up toy dots, bottom right. The line and the dots
        // take a row only while they have something to say (LayoutCharge).
        void BuildChargePanel(Transform parent)
        {
            Image background = HudWidgets.Plate("Charge", parent);
            _chargePanel = background.rectTransform;
            HudWidgets.Place(_chargePanel, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-32f, 32f), new Vector2(BarPanelWidth, 112f));

            BuildBarRow(background.transform, "CHARGE", 0, out _, out _, out _chargeFill);
            BuildBarRow(background.transform, string.Empty, 1, out _chargeStatus, out _overchargeTrack, out _overchargeFill);
            _statusRow = (RectTransform)_chargeStatus.transform.parent;
            _overchargeFill.color = HudWidgets.Sun;
            _overchargeTrack.gameObject.SetActive(false);

            _toysRow = HudWidgets.Rect("Wind-up toys", background.transform);
            HudWidgets.Place(_toysRow, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -(18f + 2 * BarRowStep)), new Vector2(BarPanelWidth, 34f));
            Text label = HudWidgets.Label("Label", _toysRow, "WIND-UP TOYS", HudWidgets.Caption, HudWidgets.InkDim, TextAnchor.MiddleLeft);
            HudWidgets.Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(HudWidgets.Pad, 0f), new Vector2(176f, 30f));
            _toyDots = new Image[MaxToyDots];
            for (int i = 0; i < MaxToyDots; i++)
            {
                _toyDots[i] = HudWidgets.Panel("Toy " + (i + 1), _toysRow, HudWidgets.PlumLight, _sprites.Circle);
                HudWidgets.Place(_toyDots[i].rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(210f, 0f) + new Vector2(i * 34f, 0f), new Vector2(24f, 24f));
                _toyDots[i].gameObject.SetActive(false);
            }

            LayoutCharge();
        }

        // The bar is always there; the overcharge or charging line and the toy dots take a row each only when they
        // are showing, so the panel is as short as it can be and the world is not covered for nothing.
        void LayoutCharge()
        {
            bool status = _chargeStatus != null && _chargeStatus.text.Length > 0;
            bool toys = _toysMax > 0;
            int rows = 1 + (status ? 1 : 0) + (toys ? 1 : 0);

            _statusRow.gameObject.SetActive(status);
            _statusRow.anchoredPosition = new Vector2(0f, -(18f + BarRowStep));
            _toysRow.gameObject.SetActive(toys);
            _toysRow.anchoredPosition = new Vector2(0f, -(18f + (status ? 2 : 1) * BarRowStep));
            _chargePanel.sizeDelta = new Vector2(BarPanelWidth, 20f + rows * BarRowStep);
        }

        // One labelled bar; row 0 is the top. The fill is a child that is stretched from the left.
        static void BuildBarRow(Transform parent, string label, int row, out Text labelText, out Image track, out Image fill)
        {
            RectTransform rowRect = HudWidgets.Rect(label.Length > 0 ? label : "Status", parent);
            HudWidgets.Place(rowRect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -(18f + row * BarRowStep)), new Vector2(BarPanelWidth, 34f));

            labelText = HudWidgets.Label("Label", rowRect, label, HudWidgets.Caption, HudWidgets.InkDim, TextAnchor.MiddleLeft);
            HudWidgets.Place(labelText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(HudWidgets.Pad, 0f), new Vector2(176f, 30f));

            track = HudWidgets.Panel("Track", rowRect, HudWidgets.PlumLight);
            HudWidgets.Place(track.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(200f, 0f), new Vector2(BarPanelWidth - 200f - HudWidgets.Pad, 18f));
            track.gameObject.AddComponent<Canvas>();
            fill = HudWidgets.Panel("Fill", track.transform, HudWidgets.Mint);
            HudWidgets.SetFill(fill, 1f);
        }

        void BuildCrosshair(Transform parent)
        {
            RectTransform root = HudWidgets.Rect("Crosshair", parent);
            _crosshairRoot = root;
            HudWidgets.Place(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60f, 60f));
            Color colour = HudWidgets.Ink;
            colour.a = 0.85f;

            // Four ticks round an empty centre.
            _crosshair = new[]
            {
                Tick(root, new Vector2(0f, 14f), new Vector2(3f, 12f), colour),
                Tick(root, new Vector2(0f, -14f), new Vector2(3f, 12f), colour),
                Tick(root, new Vector2(14f, 0f), new Vector2(12f, 3f), colour),
                Tick(root, new Vector2(-14f, 0f), new Vector2(12f, 3f), colour)
            };
        }

        static Image Tick(Transform parent, Vector2 position, Vector2 size, Color colour)
        {
            Image tick = HudWidgets.Panel("Tick", parent, colour);
            HudWidgets.Place(tick.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);
            return tick;
        }

        // The keys the player uses, at the bottom centre. They match the title screen's controls. H puts them away.
        void BuildHints(Transform parent)
        {
            Image plate = HudWidgets.Plate("Key hints", parent);
            _hintsPlate = plate.rectTransform;
            HudWidgets.Place(_hintsPlate, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 32f), new Vector2(860f, 46f));
            Text hints = HudWidgets.Label("Text", plate.transform, HudMath.KeyHints, HudWidgets.Caption, HudWidgets.InkDim, TextAnchor.MiddleCenter, FontStyle.Normal);
            hints.rectTransform.offsetMin = new Vector2(10f, 2f);
            hints.rectTransform.offsetMax = new Vector2(-10f, -2f);
            _hintsPlate.gameObject.SetActive(!_sections.HintsHidden);
        }

        void BuildDynamic(Transform root)
        {
            _vignette = HudWidgets.Panel("Vignette", root, new Color(1f, 0.35f, 0.3f, 0f), _sprites.Vignette);
            _vignette.enabled = false;

            _arrow = HudWidgets.Rect("Objective Arrow", root);
            HudWidgets.Place(_arrow, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56f, 56f));
            Image head = HudWidgets.Panel("Head", _arrow, HudWidgets.Sun, _sprites.Triangle);
            head.rectTransform.sizeDelta = Vector2.zero;

            _arrowDistance = HudWidgets.Label("Distance", root, string.Empty, HudWidgets.Body + 2, HudWidgets.Ink, TextAnchor.MiddleCenter);
            HudWidgets.Place(_arrowDistance.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(110f, 30f));
            _arrow.gameObject.SetActive(false);
            _arrowDistance.gameObject.SetActive(false);
        }
    }
}
