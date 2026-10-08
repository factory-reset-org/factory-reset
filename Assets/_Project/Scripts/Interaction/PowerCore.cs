using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// A power core of the last chapter. It is shielded, and takes no damage, until the
    /// chapter 4 cutscene drops the shields; after that a set number of hits destroys it.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class PowerCore : TaskProp, IDamageable
    {
        [Tooltip("Must equal the task id in the chapter data.")]
        [SerializeField] string taskId = "ch4.core.1";

        [Tooltip("The chapter this task belongs to. Before it, hits do nothing.")]
        [SerializeField, Range(1, ChapterEvents.ChapterCount)] int chapter = 4;

        [SerializeField, Min(1)] int hitsToDestroy = 8;

        [Tooltip("The shield visual, hidden when the shields drop. Optional.")]
        [SerializeField] GameObject shield;

        int _hits;

        public override string Id => taskId;

        public bool IsShielded { get; private set; } = true;

        void OnEnable()
        {
            CutsceneEvents.OnCriticalSignal += HandleSignal;
            ChapterEvents.OnChapterStarted += HandleChapterStarted;
        }

        void OnDisable()
        {
            CutsceneEvents.OnCriticalSignal -= HandleSignal;
            ChapterEvents.OnChapterStarted -= HandleChapterStarted;
        }

        void HandleSignal(string signalId)
        {
            if (signalId == CutsceneSignals.CoreShieldsDown)
                DropShield();
        }

        // The signal is what normally drops the shield. The chapter starting does it too,
        // so a core can never stay shielded in its own chapter.
        void HandleChapterStarted(int started)
        {
            if (started >= chapter)
                DropShield();
        }

        [ContextMenu("Drop Shield")]
        void DropShield()
        {
            if (!IsShielded)
                return;

            IsShielded = false;
            if (shield != null)
                shield.SetActive(false);
        }

        [ContextMenu("Take Hit")]
        public void TakeHit()
        {
            if (IsCompleted || IsShielded || !ChapterIsActive(chapter))
                return;

            _hits++;
            if (_hits < hitsToDestroy)
            {
                ReportProgress(_hits / (float)hitsToDestroy);
                return;
            }

            EmitNoise(NoiseLoudness.CoreExplosion);
            Complete();
            gameObject.SetActive(false);
        }
    }
}
