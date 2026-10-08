using System;
using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// Base for every chapter task prop. A prop only reports: it raises its progress and,
    /// once, its completion. Whether that counts (is the chapter active, is the switch
    /// unsealed) is the chapter manager's decision, never the prop's.
    /// </summary>
    /// <remarks>
    /// Completion latches, and the chapter manager ignores a task finished outside its
    /// chapter. So a prop the player can reach early checks <see cref="ChapterIsActive"/>
    /// before it completes, or it would latch for nothing and its chapter could never finish.
    /// </remarks>
    public abstract class TaskProp : MonoBehaviour, ITask
    {
        /// <summary>Must equal the task id in the chapter data, e.g. "ch1.lever".</summary>
        public abstract string Id { get; }

        public event Action<float> OnProgress;
        public event Action OnCompleted;

        /// <summary>True once the prop has reported completion.</summary>
        public bool IsCompleted { get; private set; }

        /// <summary>Game time, which stands still while the game is not being played.</summary>
        protected static float Now => GameClock.Current != null ? GameClock.Current.GameTime : Time.time;

        /// <summary>
        /// True while <paramref name="chapter"/> is the one being played. With no journey
        /// running (a test scene) every chapter counts as active.
        /// </summary>
        protected static bool ChapterIsActive(int chapter)
        {
            ChapterManager manager = ChapterManager.Current;
            if (manager == null || manager.Flow == null || !manager.Flow.HasBegun)
                return true;
            return manager.Flow.GetPhase(chapter) == ChapterPhase.Active;
        }

        protected void ReportProgress(float progress) => OnProgress?.Invoke(Mathf.Clamp01(progress));

        // The object's hash keeps each prop a distinct emitter: small ids belong to agents
        // and -1 means the player.
        protected void EmitNoise(float loudness) =>
            NoiseEvents.Emit(new NoiseEvent(transform.position, loudness, GetHashCode(), Now));

        /// <summary>Reports completion. Later calls do nothing.</summary>
        protected void Complete()
        {
            if (IsCompleted)
                return;

            IsCompleted = true;
            OnProgress?.Invoke(1f);
            OnCompleted?.Invoke();
        }
    }
}
