using System;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.Interfaces;

namespace ToyFactory.Runtime.Agents
{
    /// <summary>
    /// Writes the current chapter onto the blackboard when S1's chapter manager starts one
    /// (<see cref="ChapterEvents.OnChapterStarted"/>), so brains can read
    /// <see cref="WorldBlackboard.ChapterIndex"/> without referencing journey code. The
    /// Captain uses it to raise the console prior in the final chapter.
    /// </summary>
    public sealed class ChapterIndexWriter : IDisposable
    {
        readonly WorldBlackboard _blackboard;
        bool _disposed;

        public ChapterIndexWriter(WorldBlackboard blackboard)
        {
            _blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
            ChapterEvents.OnChapterStarted += HandleChapterStarted;
        }

        /// <summary>Stops listening. The writer does nothing after this.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            ChapterEvents.OnChapterStarted -= HandleChapterStarted;
        }

        void HandleChapterStarted(int chapter) => _blackboard.SetChapterIndex(chapter);
    }
}
