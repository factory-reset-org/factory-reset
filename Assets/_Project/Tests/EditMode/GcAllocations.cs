using System;
using UnityEngine.Profiling;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// Counts the managed (GC) allocations an action makes on this thread, with Unity's
    /// "GC.Alloc" profiler recorder (the same one Unity's <c>Is.Not.AllocatingGCMemory()</c>
    /// uses). Use it for "allocates nothing" tests: <c>GC.GetAllocatedBytesForCurrentThread()</c>
    /// always returns 0 under Unity's Mono, so a test built on it can never fail.
    /// </summary>
    /// <remarks>The count is allocation events, not bytes: a new <c>List&lt;T&gt;</c> with a set
    /// capacity is 2 (the list and its array).</remarks>
    public static class GcAllocations
    {
        public static int Count(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            Recorder recorder = Recorder.Get("GC.Alloc");
            recorder.FilterToCurrentThread();
            recorder.enabled = false;   // reset
            recorder.enabled = true;
            action();
            recorder.enabled = false;
            int count = recorder.sampleBlockCount;
            recorder.CollectFromAllThreads();
            return count;
        }
    }
}
