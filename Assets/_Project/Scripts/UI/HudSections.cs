using System;
using System.Collections.Generic;
using UnityEngine;

namespace ToyFactory.UI
{
    /// <summary>
    /// Which collapsible parts of the HUD the player has folded away: the objectives panel (Tab) and the key
    /// hints (H). The choices are kept between runs, like the volumes. Plain logic, so it is tested without a scene.
    /// </summary>
    public sealed class HudSections
    {
        /// <summary>The key the objectives choice is stored under.</summary>
        public const string ObjectivesKey = "HudObjectivesCollapsed";

        /// <summary>The key the key-hints choice is stored under.</summary>
        public const string HintsKey = "HudHintsHidden";

        readonly Func<string, int> _read;
        readonly Action<string, int> _write;

        /// <summary>True while the objectives panel shows only the task in hand.</summary>
        public bool ObjectivesCollapsed { get; private set; }

        /// <summary>True while the key hints are put away.</summary>
        public bool HintsHidden { get; private set; }

        /// <summary>Reads and writes the choices through PlayerPrefs.</summary>
        public HudSections() : this(key => PlayerPrefs.GetInt(key, 0), (key, value) => PlayerPrefs.SetInt(key, value))
        {
        }

        /// <summary>Reads and writes the choices through the given functions, for tests.</summary>
        public HudSections(Func<string, int> read, Action<string, int> write)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _write = write ?? throw new ArgumentNullException(nameof(write));
            ObjectivesCollapsed = _read(ObjectivesKey) != 0;
            HintsHidden = _read(HintsKey) != 0;
        }

        /// <summary>Folds the objectives panel or opens it again.</summary>
        public void ToggleObjectives()
        {
            ObjectivesCollapsed = !ObjectivesCollapsed;
            _write(ObjectivesKey, ObjectivesCollapsed ? 1 : 0);
        }

        /// <summary>Puts the key hints away or brings them back.</summary>
        public void ToggleHints()
        {
            HintsHidden = !HintsHidden;
            _write(HintsKey, HintsHidden ? 1 : 0);
        }

        /// <summary>
        /// The one row the folded panel keeps: the current task, or else the first task not done yet, or else
        /// the last row (everything is done). -1 when there are no rows.
        /// </summary>
        public static int CompactRow(IReadOnlyList<HudTaskRow> rows, string currentTaskId)
        {
            if (rows == null || rows.Count == 0)
                return -1;

            if (!string.IsNullOrEmpty(currentTaskId))
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    if (!rows[i].Done && rows[i].Id == currentTaskId)
                        return i;
                }
            }

            for (int i = 0; i < rows.Count; i++)
            {
                if (!rows[i].Done)
                    return i;
            }

            return rows.Count - 1;
        }

        /// <summary>The words beside the chapter tag while the panel is folded: how many tasks are done.</summary>
        public static string Summary(int done, int total) => total > 0 ? $"{done} of {total} DONE" : string.Empty;

        /// <summary>The label on the panel's key chip: what pressing Tab will do.</summary>
        public static string ObjectivesChip(bool collapsed) => collapsed ? "TAB  MORE" : "TAB  LESS";
    }
}
