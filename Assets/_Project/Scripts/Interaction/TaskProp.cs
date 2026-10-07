using System;
using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// Base for every chapter task prop. A prop only reports: it raises its progress and,
    /// once, its completion. Whether that counts (is the chapter active, is the switch
    /// unsealed) is the chapter manager's decision, never the prop's.
    /// </summary>
    public abstract class TaskProp : MonoBehaviour, ITask
    {
        /// <summary>Must equal the task id in the chapter data, e.g. "ch1.lever".</summary>
        public abstract string Id { get; }

        public event Action<float> OnProgress;
        public event Action OnCompleted;

        /// <summary>True once the prop has reported completion.</summary>
        public bool IsCompleted { get; private set; }

        protected void ReportProgress(float progress) => OnProgress?.Invoke(Mathf.Clamp01(progress));

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
