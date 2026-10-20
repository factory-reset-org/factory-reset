using System;

namespace ToyFactory.Interfaces
{
    /// <summary>
    /// A chapter task prop (lever, plate, targets, terminal, relays, cores, console).
    /// Implemented by the prop itself; the chapter and score systems only ever listen
    /// to these events, they never poll or reach into the prop's own behaviour.
    /// </summary>
    public interface ITask
    {
        /// <summary>Stable id matching this task's TaskDefinition asset.</summary>
        string Id { get; }

        /// <summary>Raised whenever progress changes, in [0, 1].</summary>
        event Action<float> OnProgress;

        /// <summary>Raised once, when the task is fully done.</summary>
        event Action OnCompleted;
    }
}
