using System.Collections.Generic;
using UnityEngine;

namespace ToyFactory.Interfaces
{
    /// <summary>The kinds of object a Saboteur can act on.</summary>
    public enum SabotageKind
    {
        Door,
        Trap,
        Battery
    }

    /// <summary>One registered sabotage target, as listed by <see cref="SabotageTargets.CopyTo"/>.</summary>
    public readonly struct SabotageTargetInfo
    {
        public SabotageKind Kind { get; }
        public int Id { get; }
        public ISabotageable Target { get; }
        public Transform Where { get; }

        public SabotageTargetInfo(SabotageKind kind, int id, ISabotageable target, Transform where)
        {
            Kind = kind;
            Id = id;
            Target = target;
            Where = where;
        }
    }

    /// <summary>
    /// Finds a sabotage target from the id a Saboteur's brain names. Doors, traps and battery
    /// pickups register themselves here with the same id the brain sees (a door's id is its
    /// <c>doorId</c>, the grid's door id); the runtime looks the target up, checks the agent
    /// is close enough, and calls <see cref="ISabotageable.Execute"/>. The brain never touches
    /// a GameObject.
    /// </summary>
    /// <remarks>
    /// Register in <c>OnEnable</c> and unregister in <c>OnDisable</c>. A second target under the
    /// same kind and id replaces the first. Domain reload is off in this project, so the table
    /// is cleared at the start of each play session.
    /// </remarks>
    public static class SabotageTargets
    {
        readonly struct Entry
        {
            public readonly ISabotageable Target;
            public readonly Transform Where;

            public Entry(ISabotageable target, Transform where)
            {
                Target = target;
                Where = where;
            }
        }

        static readonly Dictionary<(SabotageKind, int), Entry> Targets = new Dictionary<(SabotageKind, int), Entry>();

        /// <summary>Number of registered targets, for tests and the debug overlay.</summary>
        public static int Count => Targets.Count;

        /// <summary>Registers <paramref name="target"/> under <paramref name="kind"/> and <paramref name="id"/>, standing at <paramref name="where"/>.</summary>
        public static void Register(SabotageKind kind, int id, ISabotageable target, Transform where)
        {
            if (target == null || where == null)
                return;
            Targets[(kind, id)] = new Entry(target, where);
        }

        /// <summary>Removes the target, if <paramref name="target"/> is still the one registered there.</summary>
        public static void Unregister(SabotageKind kind, int id, ISabotageable target)
        {
            if (Targets.TryGetValue((kind, id), out Entry entry) && ReferenceEquals(entry.Target, target))
                Targets.Remove((kind, id));
        }

        /// <summary>
        /// Replaces the contents of <paramref name="into"/> with every registered target that is still
        /// alive. For the runtime's fact writer; allocation-free once the list has grown.
        /// </summary>
        public static int CopyTo(List<SabotageTargetInfo> into)
        {
            into.Clear();
            foreach (KeyValuePair<(SabotageKind, int), Entry> pair in Targets)
            {
                if (pair.Value.Where == null)
                    continue;
                into.Add(new SabotageTargetInfo(pair.Key.Item1, pair.Key.Item2, pair.Value.Target, pair.Value.Where));
            }
            return into.Count;
        }

        /// <summary>The registered target and where it stands; false if there is none (or it was destroyed).</summary>
        public static bool TryGet(SabotageKind kind, int id, out ISabotageable target, out Transform where)
        {
            target = null;
            where = null;
            if (!Targets.TryGetValue((kind, id), out Entry entry))
                return false;
            if (entry.Where == null)   // destroyed without unregistering
            {
                Targets.Remove((kind, id));
                return false;
            }
            target = entry.Target;
            where = entry.Where;
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Clear() => Targets.Clear();
    }
}
