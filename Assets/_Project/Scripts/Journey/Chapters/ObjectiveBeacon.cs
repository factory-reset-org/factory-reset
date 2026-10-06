using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Journey.Chapters
{
    /// <summary>
    /// The light beam, ground ring and bobbing arrow that mark where the player should go
    /// next: <see cref="ChapterManager.CurrentBeaconTarget"/>. Shown only while the game is
    /// Playing and no cutscene runs, so it first appears when the intro ends and disappears
    /// for every later cutscene, the pause menu and the results screen.
    /// </summary>
    /// <remarks>
    /// The look (stripes, soft edges, depth fade, dimming within 2 m) is in the
    /// <c>ToyFactory/ObjectiveBeacon</c> shader; this component only places and animates the
    /// parts. It reads the target each frame instead of listening, so it works whichever of
    /// it and the chapter manager wakes first.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ObjectiveBeacon : MonoBehaviour
    {
        [Tooltip("Child holding the beam, ring and arrow; shown and hidden as a whole.")]
        [SerializeField] GameObject visuals;

        [SerializeField] Transform ring;
        [SerializeField] Transform arrow;

        [Header("Motion")]
        [Tooltip("Height of the arrow's tip above the floor (metres).")]
        [SerializeField] float arrowHeight = 4.2f;
        [SerializeField] float bobAmplitude = 0.2f;
        [Tooltip("Bob speed (radians per second).")]
        [SerializeField] float bobSpeed = 4f;
        [Tooltip("Arrow spin (degrees per second).")]
        [SerializeField] float arrowSpinSpeed = 115f;
        [Tooltip("Ring pulse: scale goes 1 ± this.")]
        [SerializeField] float ringPulse = 0.15f;
        [SerializeField] float ringPulseSpeed = 5f;

        [Header("Following")]
        [Tooltip("Smoothing time when the target moves (a pushed prop, the next relay close by).")]
        [SerializeField, Min(0f)] float glideTime = 0.15f;
        [Tooltip("A new target further than this away is jumped to instead of glided to (metres).")]
        [SerializeField, Min(0f)] float snapDistance = 3f;

        bool _inCutscene;
        bool _wasShowing;
        int _targetId;
        Vector3 _velocity;

        /// <summary>True while the beacon is visible.</summary>
        public bool IsShowing => visuals != null && visuals.activeSelf;

        void OnEnable()
        {
            CutsceneEvents.OnCutsceneStarted += HandleCutsceneStarted;
            CutsceneEvents.OnCutsceneEnded += HandleCutsceneEnded;
        }

        void OnDisable()
        {
            CutsceneEvents.OnCutsceneStarted -= HandleCutsceneStarted;
            CutsceneEvents.OnCutsceneEnded -= HandleCutsceneEnded;
        }

        void HandleCutsceneStarted(string cutsceneId) => _inCutscene = true;

        void HandleCutsceneEnded(string cutsceneId) => _inCutscene = false;

        void LateUpdate()
        {
            ChapterManager manager = ChapterManager.Current;
            BeaconTarget? target = manager != null ? manager.CurrentBeaconTarget : null;
            bool show = target.HasValue && !_inCutscene && IsPlaying();
            if (visuals != null && visuals.activeSelf != show)
                visuals.SetActive(show);

            if (!show)
            {
                _wasShowing = false;
                return;
            }

            BeaconTarget current = target.Value;
            Vector3 goal = new Vector3(current.Position.x, transform.position.y, current.Position.z);
            bool jump = !_wasShowing || (current.Id != _targetId && Vector3.Distance(transform.position, goal) > snapDistance);
            transform.position = jump || glideTime <= 0f
                ? goal
                : Vector3.SmoothDamp(transform.position, goal, ref _velocity, glideTime);
            if (jump)
                _velocity = Vector3.zero;
            _targetId = current.Id;
            _wasShowing = true;

            Animate(Time.time);
        }

        void Animate(float time)
        {
            if (arrow != null)
            {
                arrow.localPosition = new Vector3(0f, arrowHeight + Mathf.Sin(time * bobSpeed) * bobAmplitude, 0f);
                arrow.localRotation = Quaternion.Euler(0f, time * arrowSpinSpeed, 0f);
            }
            if (ring != null)
            {
                float scale = 1f + ringPulse * Mathf.Sin(time * ringPulseSpeed);
                ring.localScale = new Vector3(scale, 1f, scale);
            }
        }

        static bool IsPlaying()
        {
            IGameClock clock = GameClock.Current;
            return clock == null || clock.State == GameState.Playing;
        }
    }
}
