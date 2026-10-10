using System;
using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Effects;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// The spinning targets task: every target must be shot within a time window. The
    /// window opens on the first hit; if it runs out, all the targets reset.
    /// </summary>
    public sealed class TargetGroup : TaskProp
    {
        [Tooltip("Must equal the task id in the chapter data.")]
        [SerializeField] string taskId = "ch2.targets";

        [Tooltip("The chapter this task belongs to. Before it, hits do nothing.")]
        [SerializeField, Range(1, ChapterEvents.ChapterCount)] int chapter = 2;

        [Tooltip("Seconds allowed from the first hit to the last.")]
        [SerializeField, Min(1f)] float window = 12f;

        [SerializeField] SpinningTarget[] targets = new SpinningTarget[0];

        int _hits;
        float _deadline;

        public override string Id => taskId;

        /// <summary>True while the targets are the task of the chapter being played.</summary>
        public bool IsLive => !IsCompleted && ChapterIsActive(chapter);

        /// <summary>Raised when the window runs out and the targets reset.</summary>
        public event Action OnTimedOut;

        void Awake()
        {
            for (int i = 0; i < targets.Length; i++)
                if (targets[i] != null)
                    targets[i].Bind(this, i);

            // Update only watches the clock, so it runs only while the window is open.
            enabled = false;
        }

        /// <summary>Counts a hit on one of the targets. False if it does not count right now.</summary>
        internal bool RegisterHit()
        {
            if (IsCompleted || !ChapterIsActive(chapter))
                return false;

            if (_hits == 0)
            {
                _deadline = Now + window;
                enabled = true;
            }

            _hits++;
            if (_hits >= targets.Length)
            {
                enabled = false;
                Complete();
                PropEffects.Word("bullseye", transform.position + Vector3.up * 1.2f);
            }
            else
            {
                ReportProgress(_hits / (float)targets.Length);
            }
            return true;
        }

        void Update()
        {
            if (Now < _deadline)
                return;

            _hits = 0;
            enabled = false;
            foreach (SpinningTarget target in targets)
                if (target != null)
                    target.ResetTarget();
            ReportProgress(0f);
            GameSfx.Play(Sfx.Empty);
            OnTimedOut?.Invoke();
        }
    }
}
