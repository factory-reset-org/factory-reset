using System;
using UnityEngine;

namespace ToyFactory.AI.Agents.Captain
{
    /// <summary>
    /// Remembers where the player has been, so goal inference can compare where the player
    /// was a few seconds ago with where they are now. A fixed-size ring buffer of
    /// (game time, cell) samples: when it is full, the oldest sample is overwritten.
    /// </summary>
    /// <remarks>
    /// Times are game time, so the window pauses with the game during cutscenes and the
    /// pause menu. Allocates only in the constructor.
    /// </remarks>
    public sealed class PlayerTrack
    {
        /// <summary>How far back goal inference looks, in seconds.</summary>
        public const float DefaultWindow = 5f;

        /// <summary>Default number of samples kept: 32 seconds at the 2 Hz prediction rate.</summary>
        public const int DefaultCapacity = 64;

        readonly float[] _times;
        readonly Vector2Int[] _cells;

        // Index of the oldest sample; the newest is at (_oldest + Count - 1) % capacity.
        int _oldest;

        /// <summary>How far back <see cref="TryGetPast"/> looks, in seconds.</summary>
        public float Window { get; }

        /// <summary>Number of samples currently held.</summary>
        public int Count { get; private set; }

        public PlayerTrack(int capacity = DefaultCapacity, float window = DefaultWindow)
        {
            if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity), "At least two samples are needed.");
            if (!(window > 0f) || float.IsInfinity(window))
                throw new ArgumentOutOfRangeException(nameof(window), "The window must be a positive, finite number of seconds.");

            _times = new float[capacity];
            _cells = new Vector2Int[capacity];
            Window = window;
        }

        /// <summary>
        /// Adds a sample. Times must not go backwards; if they do (a restart or a
        /// respawn with a new clock), the old history no longer means anything and is cleared.
        /// </summary>
        public void Record(float time, Vector2Int cell)
        {
            if (Count > 0 && time < _times[Index(Count - 1)])
                Clear();

            if (Count < _times.Length)
            {
                Count++;
            }
            else
            {
                // Full: the new sample takes the oldest one's slot.
                _oldest = (_oldest + 1) % _times.Length;
            }

            int newest = Index(Count - 1);
            _times[newest] = time;
            _cells[newest] = cell;
        }

        /// <summary>Forgets every sample, e.g. after the player respawns or teleports.</summary>
        public void Clear()
        {
            Count = 0;
            _oldest = 0;
        }

        /// <summary>The most recent cell. False if nothing has been recorded.</summary>
        public bool TryGetCurrent(out Vector2Int cell)
        {
            if (Count == 0)
            {
                cell = default;
                return false;
            }
            cell = _cells[Index(Count - 1)];
            return true;
        }

        /// <summary>
        /// The player's cell about <see cref="Window"/> seconds before <paramref name="now"/>:
        /// the newest sample at least that old. If no sample is that old yet (just after the
        /// start or a respawn), the oldest sample is used instead. False with fewer than two
        /// samples, because one sample says nothing about direction.
        /// </summary>
        public bool TryGetPast(float now, out Vector2Int cell)
        {
            if (Count < 2)
            {
                cell = default;
                return false;
            }

            float cutoff = now - Window;
            for (int i = Count - 1; i >= 0; i--)
            {
                int index = Index(i);
                if (_times[index] <= cutoff)
                {
                    cell = _cells[index];
                    return true;
                }
            }

            cell = _cells[_oldest];
            return true;
        }

        // Ring position of the i-th oldest sample.
        int Index(int i) => (_oldest + i) % _times.Length;
    }
}
