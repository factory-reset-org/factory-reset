using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;

namespace ToyFactory.UI
{
    /// <summary>
    /// The in-game HUD, built in code under one object in <c>UI.unity</c>: the objectives panel with
    /// the switch lamps, the integrity, charge and overcharge bars, the crosshair, the damage
    /// vignette, the arrow to the current objective, the chapter card and the subtitle bar. It is
    /// hidden in cutscenes and outside play.
    /// </summary>
    /// <remarks>
    /// <para><b>Reads, never reaches in.</b> Chapter progress comes from <see cref="ChapterEvents"/>
    /// and the chapter manager's definitions, the player from <see cref="PlayerState"/>, the game
    /// state from <see cref="GameClock"/>, dialogue from <see cref="DialogueEvents"/>. It never reads
    /// a brain, and a missing player, clock or chapter manager just leaves its part empty.</para>
    /// <para><b>Two canvases.</b> The static part (panels, labels) and the part that changes every
    /// frame (bars, arrow, vignette) sit on separate canvases, so a moving bar does not rebuild the
    /// text batches.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class HudPresenter : MonoBehaviour, IGameStateListener
    {
        const int MaxRows = 8;
        const float ArrowMargin = 0.08f;

        [Tooltip("Sorting order of the HUD canvas. The cutscene letterbox is 40; the HUD sits below it.")]
        [SerializeField] int sortingOrder = 20;

        [Tooltip("Sorting order of the subtitle bar. It must sit above the letterbox (40).")]
        [SerializeField] int subtitleSortingOrder = 50;

        readonly HudModel _model = new HudModel();
        readonly List<HudTaskRow> _taskBuffer = new List<HudTaskRow>(MaxRows);

        HudSprites _sprites;
        Canvas _canvas;
        CanvasGroup _group;
        Canvas _dynamic;
        ChapterCardView _card;
        SubtitleView _subtitles;

        // Objectives panel.
        RectTransform _panel;
        Text _tagText, _titleText, _lampCount;
        Image[] _rowBoxes;
        Text[] _rowTexts;
        RectTransform _lampRow;
        Image[] _lamps;
        int _drawnVersion = -1;

        // Bars, arrow and vignette.
        Image _healthFill, _chargeFill, _overchargeFill;
        GameObject _overchargeRow;
        RectTransform _barsPanel;
        RectTransform _arrow;
        Text _arrowDistance;
        Image _vignette;

        IGameClock _clock;
        GameState _state = GameState.Playing;
        bool _cutsceneActive;
        int _pendingCard;
        bool _visible = true;
        float _lastHealth = 1f;
        float _lastHitAt = float.NegativeInfinity;
        float _longestOvercharge = 8f;

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

        /// <summary>The text of row <paramref name="index"/>, or null if it is not shown.</summary>
        public string RowText(int index) =>
            _rowTexts != null && index >= 0 && index < MaxRows && _rowTexts[index].gameObject.activeSelf ? _rowTexts[index].text : null;

        void Awake()
        {
            if (Current != null && Current != this)
                Debug.LogWarning($"More than one {nameof(HudPresenter)} is loaded; using the one on {name}.", this);

            Current = this;
            _sprites = new HudSprites();
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
        }

        void OnDisable()
        {
            ChapterEvents.OnChapterStarted -= HandleChapterStarted;
            ChapterEvents.OnTaskCompleted -= HandleTaskCompleted;
            ChapterEvents.OnSwitchUnsealed -= HandleSwitchUnsealed;
            ChapterEvents.OnSwitchRestored -= HandleSwitchRestored;
            CutsceneEvents.OnCutsceneStarted -= HandleCutsceneStarted;
            CutsceneEvents.OnCutsceneEnded -= HandleCutsceneEnded;
            ReleaseClock();
        }

        void OnDestroy()
        {
            if (Current == this)
                Current = null;
            _sprites?.Dispose();
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
            if (definition != null)
                _card.Show(chapter, definition.Title, definition.Subtitle);
        }

        void HandleTaskCompleted(string taskId) => _model.CompleteTask(taskId);

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

        // ---- Chapter data

        // Fills the panel for a chapter from the journey's definitions, including tasks that were
        // already done (pickups count before their chapter starts).
        void ShowChapter(ChapterFlow flow, int chapter)
        {
            ChapterDefinition definition = flow.GetDefinition(chapter);
            if (definition == null)
                return;

            SyncLamps(flow);
            _taskBuffer.Clear();
            IReadOnlyList<TaskDefinition> tasks = definition.Tasks;
            for (int i = 0; i < tasks.Count && i < MaxRows - 1; i++)
                _taskBuffer.Add(new HudTaskRow(tasks[i].TaskId, tasks[i].DisplayName, flow.IsTaskComplete(tasks[i].TaskId)));
            _model.StartChapter(chapter, definition.Title, definition.Subtitle, _taskBuffer, definition.HasSwitch ? definition.SwitchNumber : 0);
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

            if (_model.Version != _drawnVersion)
                RefreshObjectives();

            IPlayerState player = LivePlayer();
            UpdateBars(player);
            UpdateVignette(player);
            UpdateArrow(player);
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
            if (current is Object unityObject && unityObject == null)
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
            if (_clock != null && !(_clock is Object o && o == null))
                _clock.RemoveListener(this);
            _clock = null;
        }

        static IPlayerState LivePlayer()
        {
            IPlayerState player = PlayerState.Current;
            if (player == null || (player is Object unityObject && unityObject == null))
                return null;
            return player;
        }

        void RefreshObjectives()
        {
            _drawnVersion = _model.Version;
            _tagText.text = _model.Chapter > 0 ? HudMath.ChapterTag(_model.Chapter, ChapterEvents.ChapterCount) : string.Empty;
            _titleText.text = _model.Title;

            IReadOnlyList<HudTaskRow> rows = _model.Rows;
            for (int i = 0; i < MaxRows; i++)
            {
                bool shown = i < rows.Count;
                _rowBoxes[i].gameObject.SetActive(shown);
                _rowTexts[i].gameObject.SetActive(shown);
                if (!shown)
                    continue;

                _rowBoxes[i].color = rows[i].Done ? HudWidgets.Mint : HudWidgets.PlumLight;
                _rowTexts[i].text = rows[i].Text;
                _rowTexts[i].color = rows[i].Done ? HudWidgets.InkDim : HudWidgets.Ink;
            }

            for (int i = 0; i < _lamps.Length; i++)
            {
                LampState state = _model.Lamp(i + 1);
                _lamps[i].color = state == LampState.Restored ? HudWidgets.Mint : state == LampState.Ready ? HudWidgets.Sun : HudWidgets.PlumLight;
            }

            _lampCount.text = $"{_model.SwitchesRestored} of {ChapterEvents.SwitchCount}";

            const float header = 92f;
            const float rowHeight = 32f;
            float rowsHeight = rows.Count * rowHeight;
            _lampRow.anchoredPosition = new Vector2(0f, -(header + rowsHeight + 10f));
            _panel.sizeDelta = new Vector2(_panel.sizeDelta.x, header + rowsHeight + 10f + 50f);
        }

        void UpdateBars(IPlayerState player)
        {
            float health = player != null ? HudMath.Fraction(player.HealthFraction) : 0f;
            float charge = player != null ? HudMath.Fraction(player.AmmoFraction) : 0f;
            float overcharge = player != null ? Mathf.Max(0f, player.OverchargeTimeLeft) : 0f;

            HudWidgets.SetFill(_healthFill, health);
            _healthFill.color = health > 0.5f ? HudWidgets.Mint : health > 0.25f ? HudWidgets.Sun : HudWidgets.Tomato;
            HudWidgets.SetFill(_chargeFill, charge);
            _chargeFill.color = charge > 0.2f ? HudWidgets.Cobalt : HudWidgets.Tomato;

            if (overcharge > _longestOvercharge)
                _longestOvercharge = overcharge;
            bool overcharged = overcharge > 0f;
            if (_overchargeRow.activeSelf != overcharged)
            {
                _overchargeRow.SetActive(overcharged);
                _barsPanel.sizeDelta = new Vector2(_barsPanel.sizeDelta.x, overcharged ? 158f : 112f);
            }
            if (overcharged)
                HudWidgets.SetFill(_overchargeFill, HudMath.OverchargeFraction(overcharge, _longestOvercharge));
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

        void UpdateArrow(IPlayerState player)
        {
            ChapterManager manager = ChapterManager.Current;
            Camera camera = Camera.main;
            if (manager == null || !manager.CurrentBeaconTarget.HasValue || camera == null)
            {
                SetArrowVisible(false);
                return;
            }

            Vector3 target = manager.CurrentBeaconTarget.Value.Position;
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
            Vector3 from = player != null ? player.Position : camera.transform.position;
            _arrowDistance.text = HudMath.DistanceText(Vector3.Distance(from, target));
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
            BuildBarsPanel(_canvas.transform);
            BuildCrosshair(_canvas.transform);

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
        }

        void BuildObjectivesPanel(Transform parent)
        {
            Image background = HudWidgets.Panel("Objectives", parent, HudWidgets.PlumPanel);
            _panel = background.rectTransform;
            HudWidgets.Place(_panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -32f), new Vector2(520f, 200f));

            _tagText = HudWidgets.Label("Chapter", _panel, string.Empty, 22, HudWidgets.Mint, TextAnchor.MiddleLeft);
            HudWidgets.Place(_tagText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, -14f), new Vector2(470f, 28f));
            _titleText = HudWidgets.Label("Title", _panel, string.Empty, 34, HudWidgets.Ink, TextAnchor.MiddleLeft);
            HudWidgets.Place(_titleText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, -42f), new Vector2(480f, 44f));

            _rowBoxes = new Image[MaxRows];
            _rowTexts = new Text[MaxRows];
            for (int i = 0; i < MaxRows; i++)
            {
                float y = -(92f + i * 32f);
                _rowBoxes[i] = HudWidgets.Panel("Box " + (i + 1), _panel, HudWidgets.PlumLight);
                HudWidgets.Place(_rowBoxes[i].rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, y - 4f), new Vector2(18f, 18f));
                _rowTexts[i] = HudWidgets.Label("Task " + (i + 1), _panel, string.Empty, 24, HudWidgets.Ink, TextAnchor.MiddleLeft, FontStyle.Normal);
                _rowTexts[i].resizeTextForBestFit = true;
                _rowTexts[i].resizeTextMinSize = 14;
                _rowTexts[i].resizeTextMaxSize = 24;
                _rowTexts[i].horizontalOverflow = HorizontalWrapMode.Wrap;
                _rowTexts[i].verticalOverflow = VerticalWrapMode.Truncate;
                HudWidgets.Place(_rowTexts[i].rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(54f, y), new Vector2(450f, 30f));
                _rowBoxes[i].gameObject.SetActive(false);
                _rowTexts[i].gameObject.SetActive(false);
            }

            // The switch lamps sit under the last row; the row moves with the list.
            _lampRow = HudWidgets.Rect("Switches", _panel);
            HudWidgets.Place(_lampRow, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -140f), new Vector2(520f, 40f));
            Text label = HudWidgets.Label("Label", _lampRow, "SWITCHES", 20, HudWidgets.InkDim, TextAnchor.MiddleLeft);
            HudWidgets.Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(22f, 0f), new Vector2(130f, 30f));

            _lamps = new Image[ChapterEvents.SwitchCount];
            for (int i = 0; i < _lamps.Length; i++)
            {
                _lamps[i] = HudWidgets.Panel("Lamp " + (i + 1), _lampRow, HudWidgets.PlumLight, _sprites.Circle);
                HudWidgets.Place(_lamps[i].rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(170f + i * 36f, 0f), new Vector2(26f, 26f));
            }

            _lampCount = HudWidgets.Label("Count", _lampRow, "0 of 3", 22, HudWidgets.Ink, TextAnchor.MiddleLeft);
            HudWidgets.Place(_lampCount.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(290f, 0f), new Vector2(120f, 30f));
        }

        void BuildBarsPanel(Transform parent)
        {
            Image background = HudWidgets.Panel("Player", parent, HudWidgets.PlumPanel);
            _barsPanel = background.rectTransform;
            HudWidgets.Place(_barsPanel, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(32f, 32f), new Vector2(430f, 112f));

            BuildBarRow(background.transform, "INTEGRITY", 0, out _healthFill, out _);
            BuildBarRow(background.transform, "CHARGE", 1, out _chargeFill, out _);
            BuildBarRow(background.transform, "OVERCHARGE", 2, out _overchargeFill, out _overchargeRow);
            _overchargeFill.color = HudWidgets.Sun;
            _overchargeRow.SetActive(false);
        }

        // One labelled bar; row 0 is the top. The fill is a child that is stretched from the left.
        static void BuildBarRow(Transform parent, string label, int row, out Image fill, out GameObject rowObject)
        {
            RectTransform rowRect = HudWidgets.Rect(label, parent);
            HudWidgets.Place(rowRect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -(18f + row * 46f)), new Vector2(430f, 34f));
            rowObject = rowRect.gameObject;

            Text text = HudWidgets.Label("Label", rowRect, label, 20, HudWidgets.InkDim, TextAnchor.MiddleLeft);
            HudWidgets.Place(text.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(22f, 0f), new Vector2(150f, 30f));

            Image track = HudWidgets.Panel("Track", rowRect, HudWidgets.PlumLight);
            HudWidgets.Place(track.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(176f, 0f), new Vector2(232f, 18f));
            track.gameObject.AddComponent<Canvas>();
            fill = HudWidgets.Panel("Fill", track.transform, HudWidgets.Mint);
            HudWidgets.SetFill(fill, 1f);
        }

        static void BuildCrosshair(Transform parent)
        {
            RectTransform root = HudWidgets.Rect("Crosshair", parent);
            HudWidgets.Place(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60f, 60f));
            Color colour = HudWidgets.Ink;
            colour.a = 0.85f;

            // Four ticks round an empty centre.
            Tick(root, new Vector2(0f, 14f), new Vector2(3f, 12f), colour);
            Tick(root, new Vector2(0f, -14f), new Vector2(3f, 12f), colour);
            Tick(root, new Vector2(14f, 0f), new Vector2(12f, 3f), colour);
            Tick(root, new Vector2(-14f, 0f), new Vector2(12f, 3f), colour);
        }

        static void Tick(Transform parent, Vector2 position, Vector2 size, Color colour)
        {
            Image tick = HudWidgets.Panel("Tick", parent, colour);
            HudWidgets.Place(tick.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);
        }

        void BuildDynamic(Transform root)
        {
            _vignette = HudWidgets.Panel("Vignette", root, new Color(1f, 0.35f, 0.3f, 0f), _sprites.Vignette);
            _vignette.enabled = false;

            _arrow = HudWidgets.Rect("Objective Arrow", root);
            HudWidgets.Place(_arrow, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56f, 56f));
            Image head = HudWidgets.Panel("Head", _arrow, HudWidgets.Sun, _sprites.Triangle);
            head.rectTransform.sizeDelta = Vector2.zero;

            _arrowDistance = HudWidgets.Label("Distance", root, string.Empty, 24, HudWidgets.Ink, TextAnchor.MiddleCenter);
            HudWidgets.Place(_arrowDistance.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(110f, 30f));
            _arrow.gameObject.SetActive(false);
            _arrowDistance.gameObject.SetActive(false);
        }
    }
}
