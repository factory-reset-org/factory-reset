using System;
using System.Collections.Generic;
using UnityEngine;

namespace ToyFactory.AI.Agents.Tracker
{
    /// <summary>The noise the Tracker currently wants to follow.</summary>
    public readonly struct NoiseTarget
    {
        public readonly int SourceId;
        public readonly Vector3 Position;
        public readonly float Score;
        /// <summary>True for a source that keeps making noise (a thrown toy, the hack terminal).</summary>
        public readonly bool IsRepeating;

        public NoiseTarget(int sourceId, Vector3 position, float score, bool isRepeating)
        {
            SourceId = sourceId;
            Position = position;
            Score = score;
            IsRepeating = isRepeating;
        }
    }

    /// <summary>One source the Tracker still remembers, as the debug overlay shows it.</summary>
    public readonly struct RememberedNoise
    {
        public readonly int SourceId;
        public readonly Vector3 Position;
        /// <summary>The level heard at the Tracker's cell.</summary>
        public readonly float Level;
        public readonly float Score;
        public readonly bool IsRepeating;
        /// <summary>Already investigated: ignored until the source makes a new noise.</summary>
        public readonly bool IsHandled;

        public RememberedNoise(int sourceId, Vector3 position, float level, float score, bool isRepeating, bool isHandled)
        {
            SourceId = sourceId;
            Position = position;
            Level = level;
            Score = score;
            IsRepeating = isRepeating;
            IsHandled = isHandled;
        }
    }

    /// <summary>
    /// The Tracker's short memory of what it heard. Each source keeps one entry: a new noise
    /// from the same source replaces the old one, so the terminal's beeps every 0.8 s keep a
    /// single entry fresh instead of piling up.
    /// </summary>
    /// <remarks>
    /// <para><b>Score:</b> <c>level * e^(-0.3 * age)</c>, where level is the propagated level at
    /// the Tracker's cell when it was heard. A 5 s old noise keeps about 22% of its score,
    /// so a fresh beep beats an older, louder shot. Entries whose score falls to the hearing
    /// threshold (10) are forgotten.</para>
    /// <para><b>Repeating:</b> a source heard again within 1.5 s of its previous noise is
    /// repeating; it stops counting as repeating once it has been silent for 1.5 s. The player
    /// (<see cref="PlayerSourceId"/>) is never repeating: footsteps every 0.45 s are a trail to
    /// follow, not a lure to watch. A source that marks its noise as a lure (the thrown toy)
    /// counts as repeating from its first noise, so the Tracker reacts to the toy's first tick
    /// instead of 0.6 s later.</para>
    /// <para>Fixed capacity and no allocation after construction.</para>
    /// </remarks>
    public sealed class NoiseMemory
    {
        public const float DecayPerSecond = 0.3f;
        public const float ForgetScore = 10f;
        public const float RepeatWindow = 1.5f;
        public const int DefaultCapacity = 8;

        /// <summary>The source id of every noise the player makes (the <c>NoiseEvent</c> contract).</summary>
        public const int PlayerSourceId = -1;

        struct Entry
        {
            public bool Used;
            public int SourceId;
            public Vector3 Position;
            public float Level;
            public float Time;
            public int RepeatCount;   // consecutive noises less than RepeatWindow apart
            public bool Lure;         // the source marks its noise as a lure (the thrown toy)
            public bool Handled;      // investigated already; ignored until the source makes a new noise
        }

        readonly Entry[] _entries;

        public NoiseMemory(int capacity = DefaultCapacity)
        {
            _entries = new Entry[Mathf.Max(1, capacity)];
        }

        /// <summary>Score of a noise of <paramref name="level"/> heard <paramref name="age"/> seconds ago.</summary>
        public static float Score(float level, float age) => level * Mathf.Exp(-DecayPerSecond * Mathf.Max(0f, age));

        /// <summary>
        /// Records a heard noise. A noise not newer than the source's last one is ignored.
        /// <paramref name="isLure"/> marks the source as a lure from now on (the thrown toy).
        /// </summary>
        public void Remember(int sourceId, Vector3 position, float level, float time, bool isLure = false)
        {
            int slot = Find(sourceId);
            if (slot >= 0)
            {
                ref Entry existing = ref _entries[slot];
                if (time <= existing.Time)
                    return;
                existing.RepeatCount = time - existing.Time <= RepeatWindow ? existing.RepeatCount + 1 : 1;
                existing.Position = position;
                existing.Level = level;
                existing.Time = time;
                existing.Handled = false;
                existing.Lure |= isLure;
                return;
            }

            slot = FreeOrWeakestSlot(time);
            _entries[slot] = new Entry
            {
                Used = true, SourceId = sourceId, Position = position, Level = level,
                Time = time, RepeatCount = 1, Handled = false, Lure = isLure
            };
        }

        /// <summary>
        /// The best noise to follow at <paramref name="now"/>: the highest score among
        /// remembered, not yet handled noises. Equal scores prefer the one closer to
        /// <paramref name="listener"/>.
        /// </summary>
        public bool TryGetBest(float now, Vector3 listener, out NoiseTarget best) =>
            TryGetBest(now, listener, repeatingOnly: false, out best);

        /// <summary>
        /// Like <see cref="TryGetBest(float, Vector3, out NoiseTarget)"/>, but only among sources
        /// that are still repeating (a ticking toy, a beeping terminal), so a louder one-off
        /// noise such as a blaster shot never hides a lure.
        /// </summary>
        public bool TryGetBestRepeating(float now, Vector3 listener, out NoiseTarget best) =>
            TryGetBest(now, listener, repeatingOnly: true, out best);

        bool TryGetBest(float now, Vector3 listener, bool repeatingOnly, out NoiseTarget best)
        {
            best = default;
            int bestSlot = -1;
            float bestScore = 0f, bestDistance = 0f;
            for (int i = 0; i < _entries.Length; i++)
            {
                if (!IsAlive(i, now) || _entries[i].Handled)
                    continue;
                if (repeatingOnly && !IsRepeating(i, now))
                    continue;
                float score = Score(_entries[i].Level, now - _entries[i].Time);
                float distance = FlatDistanceSq(listener, _entries[i].Position);
                bool better = bestSlot < 0 || score > bestScore + 1e-4f ||
                              (Mathf.Abs(score - bestScore) <= 1e-4f && distance < bestDistance);
                if (!better)
                    continue;
                bestSlot = i;
                bestScore = score;
                bestDistance = distance;
            }

            if (bestSlot < 0)
                return false;
            best = new NoiseTarget(_entries[bestSlot].SourceId, _entries[bestSlot].Position, bestScore,
                IsRepeating(bestSlot, now));
            return true;
        }

        /// <summary>True while <paramref name="sourceId"/> is still repeating (heard within the last 1.5 s).</summary>
        public bool IsStillRepeating(int sourceId, float now)
        {
            int slot = Find(sourceId);
            return slot >= 0 && IsAlive(slot, now) && IsRepeating(slot, now);
        }

        /// <summary>Last heard position of <paramref name="sourceId"/>, if remembered.</summary>
        public bool TryGetPosition(int sourceId, out Vector3 position)
        {
            int slot = Find(sourceId);
            position = slot >= 0 ? _entries[slot].Position : default;
            return slot >= 0;
        }

        /// <summary>Marks the source as investigated, so it is ignored until it makes a new noise.</summary>
        public void MarkHandled(int sourceId)
        {
            int slot = Find(sourceId);
            if (slot >= 0)
                _entries[slot].Handled = true;
        }

        public void Clear()
        {
            for (int i = 0; i < _entries.Length; i++)
                _entries[i] = default;
        }

        /// <summary>
        /// Adds every source still remembered at <paramref name="now"/> to <paramref name="into"/>,
        /// handled ones included, for the debug overlay. Read-only: unlike the queries above it
        /// never forgets a decayed entry, so looking cannot change what the Tracker does.
        /// </summary>
        public void CopyTo(float now, List<RememberedNoise> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            for (int i = 0; i < _entries.Length; i++)
            {
                Entry entry = _entries[i];
                if (!entry.Used)
                    continue;
                float score = Score(entry.Level, now - entry.Time);
                if (score <= ForgetScore)
                    continue;
                into.Add(new RememberedNoise(entry.SourceId, entry.Position, entry.Level, score,
                    IsRepeating(i, now), entry.Handled));
            }
        }

        bool IsRepeating(int slot, float now) =>
            _entries[slot].SourceId != PlayerSourceId &&
            (_entries[slot].Lure || _entries[slot].RepeatCount >= 2) &&
            now - _entries[slot].Time <= RepeatWindow;

        bool IsAlive(int slot, float now)
        {
            if (!_entries[slot].Used)
                return false;
            if (Score(_entries[slot].Level, now - _entries[slot].Time) > ForgetScore)
                return true;
            _entries[slot].Used = false;   // decayed below hearing: forget it
            return false;
        }

        int Find(int sourceId)
        {
            for (int i = 0; i < _entries.Length; i++)
                if (_entries[i].Used && _entries[i].SourceId == sourceId)
                    return i;
            return -1;
        }

        int FreeOrWeakestSlot(float now)
        {
            int weakest = 0;
            float weakestScore = float.MaxValue;
            for (int i = 0; i < _entries.Length; i++)
            {
                if (!IsAlive(i, now))
                    return i;
                float score = Score(_entries[i].Level, now - _entries[i].Time);
                if (score < weakestScore)
                {
                    weakestScore = score;
                    weakest = i;
                }
            }
            return weakest;
        }

        static float FlatDistanceSq(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }
    }
}
