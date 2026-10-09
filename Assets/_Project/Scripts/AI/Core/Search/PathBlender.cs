using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Core.Grid;

namespace ToyFactory.AI.Core.Search
{
    /// <summary>
    /// Blends a moving agent into a new route. A brain that replans mid-walk hands over a
    /// route whose first leg can point well away from where the agent is heading; walking it
    /// as it is makes the body pivot on the spot. This replaces the start of the new route
    /// with a short curve that leaves along the current heading and joins the route a little
    /// way along it, so the agent swings round like a vehicle instead.
    /// </summary>
    /// <remarks>
    /// <para><b>The curve</b> is a quadratic Bézier B(t) = (1−t)²P + 2(1−t)tC + t²E from the
    /// agent's position P to a point E on the new route, about 0.35 s of travel along it
    /// (0.6 to 1.6 m), with its control point C half that distance ahead along the current
    /// heading. The curve leaves P tangent to the heading and stays inside the triangle P, C,
    /// E, so it bends towards the route without overshooting it. Three points of it replace
    /// the route up to E.</para>
    /// <para><b>When:</b> only while moving (at least 0.5 m/s) and when the new route turns
    /// between 25° and 120° from the heading. A gentler turn needs no help; a sharper one is a
    /// turn back, which reads better as turning on the spot than as a wide loop.</para>
    /// <para><b>Safety:</b> every segment of the curve must pass <see cref="GridLineCheck"/>, the
    /// same check string pulling uses, so a blend never cuts a corner the route avoided. If any
    /// segment fails, the route is used as it is.</para>
    /// </remarks>
    public static class PathBlender
    {
        /// <summary>Slower than this (m/s), the agent can simply turn: no blend.</summary>
        public const float MinSpeed = 0.5f;

        /// <summary>Turns gentler than this (degrees) need no blend.</summary>
        public const float MinTurn = 25f;

        /// <summary>Turns sharper than this (degrees) are turns back: no blend.</summary>
        public const float MaxTurn = 120f;

        /// <summary>Seconds of travel the curve spans, clamped to <see cref="MinLength"/>..<see cref="MaxLength"/> metres.</summary>
        public const float BlendSeconds = 0.35f;
        public const float MinLength = 0.6f;
        public const float MaxLength = 1.6f;

        static readonly float[] Samples = { 0.25f, 0.5f, 0.75f };

        /// <summary>
        /// Writes <paramref name="path"/> into <paramref name="result"/>, blended from the
        /// agent's <paramref name="position"/>, <paramref name="heading"/> and
        /// <paramref name="speed"/>. Returns true if a blend curve was inserted.
        /// </summary>
        public static bool Blend(GridGraph grid, Vector3 position, Vector3 heading, float speed,
            IReadOnlyList<Vector3> path, List<Vector3> result)
        {
            result.Clear();
            for (int i = 0; i < path.Count; i++)
                result.Add(path[i]);
            if (grid == null || path.Count == 0 || speed < MinSpeed)
                return false;

            heading.y = 0f;
            if (heading.sqrMagnitude < 1e-4f)
                return false;
            heading.Normalize();

            float length = Mathf.Clamp(speed * BlendSeconds, MinLength, MaxLength);
            if (!PointAlong(position, path, length, out Vector3 end, out int resumeIndex))
                return false;

            Vector3 toEnd = end - position;
            toEnd.y = 0f;
            float turn = Vector3.Angle(heading, toEnd);
            if (turn < MinTurn || turn > MaxTurn)
                return false;

            Vector3 control = position + heading * (length * 0.5f);
            var curve = new Vector3[Samples.Length + 1];
            for (int i = 0; i < Samples.Length; i++)
            {
                float t = Samples[i];
                float u = 1f - t;
                curve[i] = u * u * position + 2f * u * t * control + t * t * end;
                curve[i].y = end.y;
            }
            curve[Samples.Length] = end;

            Vector3 from = position;
            for (int i = 0; i < curve.Length; i++)
            {
                if (!GridLineCheck.IsWalkable(grid, from, curve[i]))
                    return false;
                from = curve[i];
            }

            result.Clear();
            result.AddRange(curve);
            for (int i = resumeIndex; i < path.Count; i++)
                result.Add(path[i]);
            return true;
        }

        // The point `length` metres along the route, walking from the agent to the route's
        // first waypoint and then waypoint to waypoint. resumeIndex is the first waypoint
        // beyond it. False if the whole route is shorter than that.
        static bool PointAlong(Vector3 position, IReadOnlyList<Vector3> path, float length,
            out Vector3 point, out int resumeIndex)
        {
            Vector3 previous = position;
            float left = length;
            for (int i = 0; i < path.Count; i++)
            {
                Vector3 next = path[i];
                Vector3 step = next - previous;
                step.y = 0f;
                float stepLength = step.magnitude;
                if (stepLength >= left && stepLength > 1e-4f)
                {
                    point = previous + step * (left / stepLength);
                    point.y = next.y;
                    resumeIndex = i;
                    return true;
                }
                left -= stepLength;
                previous = next;
            }
            point = default;
            resumeIndex = path.Count;
            return false;
        }
    }
}
