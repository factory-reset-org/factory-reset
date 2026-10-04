using System;
using UnityEngine;

namespace ToyFactory.Interfaces
{
    /// <summary>
    /// Game-wide notification channel for the chapter journey. Raised only by S1's
    /// ChapterManager; cutscenes, score, HUD and the blackboard writer listen, so none of
    /// them needs a reference to Journey code.
    /// </summary>
    /// <remarks>
    /// The plan's "OnObjectiveChanged" is <see cref="ObjectiveEvents.OnTargetsChanged"/>.
    /// Events are raised synchronously on the main thread.
    /// </remarks>
    public static class ChapterEvents
    {
        /// <summary>Number of chapters in the journey.</summary>
        public const int ChapterCount = 4;

        /// <summary>Number of control switches (Chapters 1 to 3 each end with one).</summary>
        public const int SwitchCount = 3;

        /// <summary>Task id of the Chapter 4 console hold; its completion starts the ending.</summary>
        public const string ConsoleTaskId = "console";

        /// <summary>A chapter became active, 1 to <see cref="ChapterCount"/>.</summary>
        public static event Action<int> OnChapterStarted;

        /// <summary>A chapter task completed, by its TaskDefinition task id. Raised once per task.</summary>
        public static event Action<string> OnTaskCompleted;

        /// <summary>A control switch was restored, 1 to <see cref="SwitchCount"/>.</summary>
        public static event Action<int> OnSwitchRestored;

        /// <summary>Notifies listeners that chapter <paramref name="chapter"/> started.</summary>
        public static void RaiseChapterStarted(int chapter) =>
            OnChapterStarted?.Invoke(chapter);

        /// <summary>Notifies listeners that the task <paramref name="taskId"/> completed.</summary>
        public static void RaiseTaskCompleted(string taskId) =>
            OnTaskCompleted?.Invoke(taskId);

        /// <summary>Notifies listeners that switch <paramref name="switchNumber"/> was restored.</summary>
        public static void RaiseSwitchRestored(int switchNumber) =>
            OnSwitchRestored?.Invoke(switchNumber);

        // Domain reload is off in this project, so static event subscribers survive
        // between play sessions unless they are explicitly cleared.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearListeners()
        {
            OnChapterStarted = null;
            OnTaskCompleted = null;
            OnSwitchRestored = null;
        }
    }
}
