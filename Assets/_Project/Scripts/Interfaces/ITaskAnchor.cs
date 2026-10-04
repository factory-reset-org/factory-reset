using UnityEngine;

namespace ToyFactory.Interfaces
{
    /// <summary>
    /// A named spot in the level where a task prop, switch or console stands. Implemented by
    /// Runtime's TaskAnchor; lets the chapter code (Journey), which cannot reference Runtime,
    /// find where an objective is by its anchor id.
    /// </summary>
    public interface ITaskAnchor
    {
        /// <summary>Stable id, e.g. "ch1.lever" or "switch.2".</summary>
        string AnchorId { get; }

        /// <summary>Chapter whose task uses the anchor, 1 to 4; 0 for anchors used in every chapter.</summary>
        int Chapter { get; }

        /// <summary>World position of the anchor.</summary>
        Vector3 Position { get; }
    }
}
