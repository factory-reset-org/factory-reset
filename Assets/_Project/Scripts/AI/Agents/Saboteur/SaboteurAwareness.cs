using System;
using UnityEngine;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Perception;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.AI.Agents.Saboteur
{
    /// <summary>
    /// What one Saboteur knows about the player: whether it sees them now, where and when it last saw them,
    /// and the last player noise it heard. Every action that reacts to the player reads it, so none of them
    /// can know more than the Saboteur could see or hear.
    /// </summary>
    /// <remarks>
    /// <para><b>Seeing.</b> The player is seen when a line of sight exists (<see cref="IPlayerSight"/>) and
    /// they are either inside the forward vision cone (<see cref="ViewHalfAngleDegrees"/> each side of the
    /// facing, out to <see cref="ViewRange"/>), or close enough to be sensed from any side
    /// (<see cref="RearRange"/>), or still being tracked: seen within the last <see cref="TrackGraceSeconds"/>
    /// and within <see cref="TrackRange"/>, the way a Saboteur that has spotted someone keeps its eyes on them
    /// while it turns. Walls block all three.</para>
    /// <para><b>Remembering.</b> The last sighting (position, cell, velocity, time) is kept for
    /// <see cref="MemorySeconds"/>; the last player noise for <see cref="NoiseMemorySeconds"/>. Noises
    /// from other agents and from thrown lures (the toy that distracts the Tracker) are not the player and
    /// are ignored. A search that has been carried out marks the memory as used, so it is not searched twice.</para>
    /// <para>The line of sight is the expensive part (a physics ray in the game), so it is only asked when
    /// one of the three rules could make the player seen, and at most every <see cref="SightInterval"/>.</para>
    /// </remarks>
    public sealed class SaboteurAwareness
    {
        /// <summary>Half the width of the forward vision cone: 60 degrees each side, 120 in all.</summary>
        public const float ViewHalfAngleDegrees = 60f;

        /// <summary>How far the forward cone reaches, in metres.</summary>
        public const float ViewRange = 12f;

        /// <summary>How close, in metres, the player is sensed from any side, behind included.</summary>
        public const float RearRange = 3.5f;

        /// <summary>How far, in metres, a player already spotted is followed regardless of facing.</summary>
        public const float TrackRange = 14f;

        /// <summary>Seconds after the last sighting during which the player is still tracked regardless of facing.</summary>
        public const float TrackGraceSeconds = 1f;

        /// <summary>Seconds a sighting is remembered for a search.</summary>
        public const float MemorySeconds = 6f;

        /// <summary>Seconds a player noise is remembered for an investigation.</summary>
        public const float NoiseMemorySeconds = 8f;

        /// <summary>The source id the player's own noises carry (shots, footsteps).</summary>
        public const int PlayerNoiseSourceId = -1;

        /// <summary>Seconds between two line-of-sight checks.</summary>
        public const float SightInterval = 0.1f;

        static readonly float ConeCosine = Mathf.Cos(ViewHalfAngleDegrees * Mathf.Deg2Rad);

        readonly IPlayerSight _sight;
        float _lastCheck = float.NegativeInfinity;
        bool _lastLineOfSight;
        Vector2Int _lastCheckCell;
        float _evaluatedAt = float.NaN;
        float _searchedUpTo = float.NegativeInfinity;

        /// <summary>Creates the awareness; without a sight the player is never seen, only heard.</summary>
        public SaboteurAwareness(IPlayerSight sight)
        {
            _sight = sight;
        }

        /// <summary>True when there is a line-of-sight check to use.</summary>
        public bool HasSight => _sight != null;

        /// <summary>True when the player is seen at the latest update.</summary>
        public bool Sees { get; private set; }

        /// <summary>True when the player stood inside the forward cone at the latest update.</summary>
        public bool InCone { get; private set; }

        /// <summary>True when the player stood within <see cref="RearRange"/> at the latest update.</summary>
        public bool InRear { get; private set; }

        /// <summary>Flat distance to the player at the latest update; infinity when there is no player.</summary>
        public float Distance { get; private set; } = float.PositiveInfinity;

        /// <summary>True when a sighting is remembered and has not been searched yet.</summary>
        public bool HasLastSeen { get; private set; }

        /// <summary>Where the player was last seen.</summary>
        public Vector3 LastSeenPosition { get; private set; }

        /// <summary>The cell the player was last seen in.</summary>
        public Vector2Int LastSeenCell { get; private set; }

        /// <summary>How the player was moving when last seen.</summary>
        public Vector3 LastSeenVelocity { get; private set; }

        /// <summary>Game time of the last sighting.</summary>
        public float LastSeenTime { get; private set; } = float.NegativeInfinity;

        /// <summary>True when a player noise is remembered and has not been searched yet.</summary>
        public bool HasNoise { get; private set; }

        /// <summary>Where the last player noise came from.</summary>
        public Vector3 NoisePosition { get; private set; }

        /// <summary>How loud it was where the Saboteur stood.</summary>
        public float NoiseLevel { get; private set; }

        /// <summary>Game time of the last player noise.</summary>
        public float NoiseTime { get; private set; } = float.NegativeInfinity;

        /// <summary>
        /// True when the Saboteur has reason to know where the player is: it sees them, saw them within
        /// <see cref="MemorySeconds"/>, or heard them within <see cref="NoiseMemorySeconds"/>.
        /// </summary>
        public bool IsAlerted(float now) =>
            Sees
            || now - LastSeenTime <= MemorySeconds
            || now - NoiseTime <= NoiseMemorySeconds;

        /// <summary>True when <paramref name="target"/> lies inside a cone of <see cref="ViewHalfAngleDegrees"/> around <paramref name="forward"/> from <paramref name="from"/>.</summary>
        public static bool InViewCone(Vector3 forward, Vector3 from, Vector3 target)
        {
            float fx = forward.x, fz = forward.z;
            float tx = target.x - from.x, tz = target.z - from.z;
            float fl = (float)Math.Sqrt(fx * fx + fz * fz);
            float tl = (float)Math.Sqrt(tx * tx + tz * tz);
            if (fl < 1e-4f)
                return false;   // no facing known: only the all-round sense applies
            if (tl < 1e-4f)
                return true;
            return (fx * tx + fz * tz) / (fl * tl) >= ConeCosine;
        }

        /// <summary>
        /// Brings the awareness up to date for this tick. Calling it again at the same game time does
        /// nothing, so the brain and every action source can ask it without doing the work twice.
        /// </summary>
        public void Update(in AgentContext ctx)
        {
            if (_evaluatedAt == ctx.Time)
                return;
            _evaluatedAt = ctx.Time;
            float now = ctx.Time;

            ForgetOld(now);
            Hear(ctx.Senses, now);

            Sees = false;
            InCone = false;
            InRear = false;
            Distance = float.PositiveInfinity;

            if (ctx.World == null)
                return;

            PlayerSnapshot player = ctx.World.Player;
            if (!player.IsKnown)
                return;
            if (!player.IsAlive)
            {
                // Nothing left to look for.
                ForgetPlayer(now);
                return;
            }

            float dx = player.Position.x - ctx.Position.x;
            float dz = player.Position.z - ctx.Position.z;
            Distance = (float)Math.Sqrt(dx * dx + dz * dz);
            InCone = Distance <= ViewRange && InViewCone(ctx.Forward, ctx.Position, player.Position);
            InRear = Distance <= RearRange;
            bool tracking = now - LastSeenTime <= TrackGraceSeconds && Distance <= TrackRange;
            if (!(InCone || InRear || tracking) || _sight == null)
                return;

            if (now - _lastCheck >= SightInterval || ctx.Cell != _lastCheckCell || now < _lastCheck)
            {
                _lastCheck = now;
                _lastCheckCell = ctx.Cell;
                _lastLineOfSight = _sight.CanSeeCell(ctx.Cell);
            }

            if (!_lastLineOfSight)
                return;

            Sees = true;
            HasLastSeen = true;
            LastSeenPosition = player.Position;
            LastSeenCell = player.Cell;
            LastSeenVelocity = player.Velocity;
            LastSeenTime = now;

            // A noise from before the player was seen tells nothing new.
            HasNoise = false;
        }

        /// <summary>
        /// The sighting and the noise up to <paramref name="now"/> have been searched: they are not offered
        /// again. A new sighting or noise afterwards is.
        /// </summary>
        public void MarkSearched(float now)
        {
            _searchedUpTo = now;
            HasLastSeen = false;
            HasNoise = false;
        }

        /// <summary>Drops every memory of the player, for a player who died or is gone.</summary>
        public void ForgetPlayer(float now)
        {
            HasLastSeen = false;
            HasNoise = false;
            LastSeenTime = float.NegativeInfinity;
            NoiseTime = float.NegativeInfinity;
            _searchedUpTo = now;
        }

        void Hear(in SensorSnapshot senses, float now)
        {
            if (!senses.HasNoise || senses.NoiseIsLure || senses.NoiseSourceId != PlayerNoiseSourceId)
                return;
            if (senses.NoiseTime <= _searchedUpTo || senses.NoiseTime > now + 1f)
                return;

            HasNoise = true;
            NoisePosition = senses.NoisePosition;
            NoiseLevel = senses.NoiseLevel;
            NoiseTime = senses.NoiseTime;
        }

        void ForgetOld(float now)
        {
            if (HasLastSeen && now - LastSeenTime > MemorySeconds)
                HasLastSeen = false;
            if (HasNoise && now - NoiseTime > NoiseMemorySeconds)
                HasNoise = false;
        }
    }

    /// <summary>
    /// An action source that reacts to the player through its Saboteur's <see cref="SaboteurAwareness"/>, and
    /// that measures walks with the squad's pathfinder. The brain binds its own awareness and pathfinder when
    /// it is built, so every source sees exactly what the brain sees. A source that is never bound makes its
    /// own awareness from the sight it was given (or none, and then it never sees the player).
    /// </summary>
    public interface ISaboteurAwarenessUser
    {
        /// <summary>Uses <paramref name="awareness"/> and <paramref name="pathfinder"/> from now on.</summary>
        void Bind(SaboteurAwareness awareness, IPathfinder pathfinder);
    }
}
