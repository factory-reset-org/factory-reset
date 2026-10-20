using System.Collections.Generic;
using UnityEngine;

namespace ToyFactory.Journey.Chapters
{
    /// <summary>
    /// One chapter of the journey: its area, its tasks, the control switch its tasks unseal,
    /// and the cutscene that plays once that switch is restored.
    /// </summary>
    [CreateAssetMenu(menuName = "Factory Reset/Chapter Definition", fileName = "Chapter")]
    public sealed class ChapterDefinition : ScriptableObject
    {
        [SerializeField, Range(1, 4)] int index = 1;
        [SerializeField] string title;
        [SerializeField] string subtitle;

        [Tooltip("Area name, matching the AreaVolume, e.g. \"Assembly Floor\".")]
        [SerializeField] string area;

        [SerializeField] TaskDefinition[] tasks = new TaskDefinition[0];

        [Tooltip("Control switch unsealed by finishing every task, 1 to 3; 0 for none (Chapter 4).")]
        [SerializeField, Range(0, 3)] int switchNumber;

        [Tooltip("Cutscene played after the switch is restored; the next chapter starts when it ends. Empty for none.")]
        [SerializeField] string cutsceneId;

        public int Index => index;
        public string Title => title;
        public string Subtitle => subtitle;
        public string Area => area;
        public IReadOnlyList<TaskDefinition> Tasks => tasks;
        public int SwitchNumber => switchNumber;
        public string CutsceneId => cutsceneId;
        public bool HasSwitch => switchNumber > 0;

        /// <summary>Builds a definition in code (tests and the asset generator).</summary>
        public static ChapterDefinition Create(int index, string title, string subtitle, string area,
            TaskDefinition[] tasks, int switchNumber, string cutsceneId)
        {
            var chapter = CreateInstance<ChapterDefinition>();
            chapter.index = index;
            chapter.title = title;
            chapter.subtitle = subtitle;
            chapter.area = area;
            chapter.tasks = tasks ?? new TaskDefinition[0];
            chapter.switchNumber = switchNumber;
            chapter.cutsceneId = cutsceneId ?? "";
            chapter.name = "Chapter" + index;
            return chapter;
        }
    }
}
