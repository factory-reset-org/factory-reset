using System.Collections.Generic;
using UnityEngine;

namespace ToyFactory.Editor.Animation
{
    /// <summary>
    /// The Captain's legs, solved rather than guessed: for each moment of a gait cycle it plans
    /// where each boot is, then works out the hip, knee and ankle angles that put it there
    /// (two-bone inverse kinematics). A planted boot moves back under the body at exactly the
    /// walking speed, so it stays still on the floor instead of sliding.
    /// </summary>
    /// <remarks>
    /// <para><b>The boot's path.</b> A cycle is two steps; the left heel strikes at 0 and the
    /// right half a cycle later. On the ground (the first <see cref="Params.Duty"/> of its
    /// cycle) the boot lands toe-up on its heel, rolls flat, then peels heel-first over the
    /// front edge of its sole before it leaves. The sole is one rigid piece under the ankle,
    /// longer than the toe cap, so the boot rolls over the sole's tip and the toe pivot stays
    /// straight (bending it would only lift the cap off the sole). In the air the boot follows
    /// a Hermite curve that leaves and lands at the ground's speed, so it does not jerk, and
    /// lifts in an arch.</para>
    /// <para><b>The pelvis</b> rises and falls twice a cycle, rolls towards the standing leg's
    /// side and turns with the forward leg; the legs turn back against the turn so the boots
    /// keep pointing ahead. Its height is chosen so a planted leg can always reach the floor
    /// (checked by the clips' tests on the real model).</para>
    /// <para><b>Inverse kinematics</b> in the leg's plane (its pivots are hinges on local X):
    /// the knee angle comes from the law of cosines on the thigh, the shin and the hip-to-ankle
    /// distance, then the hip angle aims the leg at the ankle. The ankle then sets the boot's
    /// pitch.</para>
    /// <para><b>Axes</b> as in <see cref="AgentMotionLibrary"/>: +X swings a leg back and bends a
    /// knee, so a boot pitched +X is toe-down (heel up).</para>
    /// </remarks>
    public static class CaptainGait
    {
        public const string Root = "CaptainBot_Root";
        public const string Pelvis = Root + "/Pelvis_Pivot";
        public const string LegL = Pelvis + "/Leg_L_Pivot", LegR = Pelvis + "/Leg_R_Pivot";
        public const string KneeL = LegL + "/Knee_L_Pivot", KneeR = LegR + "/Knee_R_Pivot";
        public const string AnkleL = KneeL + "/Ankle_L_Pivot", AnkleR = KneeR + "/Ankle_R_Pivot";

        // ---- The rig, measured on the model (Unity metres, rest pose) ----------------------

        /// <summary>Pelvis pivot above the root (the floor).</summary>
        public const float PelvisHeight = 1.21f;

        /// <summary>Right hip from the pelvis pivot; the left one is mirrored.</summary>
        static readonly Vector3 Hip = new Vector3(0.29f, -0.04f, 0f);

        // Hip to knee and knee to ankle, as (y, z): the knee sits 2 cm forward of the line.
        static readonly Vector2 Thigh = new Vector2(-0.48f, 0.02f);
        static readonly Vector2 Shin = new Vector2(-0.45f, -0.02f);

        const float AnkleHeight = 0.24f;   // the ankle pivot above the sole
        const float TipForward = 0.36f;    // the front of the sole ahead of the ankle: the boot peels over it
        const float HeelBack = 0.22f;      // the back of the sole behind the ankle: it lands on it

        /// <summary>One gait: how fast, how long a step, how high and how bouncy.</summary>
        public sealed class Params
        {
            /// <summary>Seconds per cycle (two steps).</summary>
            public float Cycle;

            /// <summary>Speed (m/s) the planted boot moves back at, so it stays still on the floor.</summary>
            public float Speed;

            /// <summary>Share of the cycle a boot is on the ground: over 0.5 walks, under 0.5 runs (both feet off at times).</summary>
            public float Duty = 1f;

            /// <summary>Metres the pelvis sits below its rest height, on average.</summary>
            public float Drop;

            /// <summary>Metres the pelvis rises and falls each way, twice per cycle.</summary>
            public float Bob;

            /// <summary>When the pelvis is highest, as a share of the cycle.</summary>
            public float BobPeak;

            /// <summary>Metres the swinging boot lifts at most.</summary>
            public float Lift;

            /// <summary>Degrees the toe points up as the heel lands (negative).</summary>
            public float Strike;

            /// <summary>Degrees the heel is up as the boot leaves the floor.</summary>
            public float Push;

            /// <summary>Extra degrees the toe lifts mid-swing, so it clears the floor.</summary>
            public float ToeUp;

            /// <summary>Degrees the pelvis turns with the forward leg.</summary>
            public float Yaw;

            /// <summary>Degrees the pelvis rolls towards the standing leg.</summary>
            public float Roll;

            /// <summary>Phase of the roll: the right side is lowest a quarter cycle after it.</summary>
            public float RollPhase;

            /// <summary>Keys per cycle, in twelves: enough that a planted boot stays put between keys.</summary>
            public int Detail = 6;

            /// <summary>Where each boot stands when the gait does not move (idle): ankle forward of the hip, m.</summary>
            public float StanceLeft, StanceRight;
        }

        /// <summary>One leg's pivot angles in degrees.</summary>
        public struct Leg
        {
            public float HipX, HipY, KneeX, AnkleX, AnkleZ;
        }

        /// <summary>The pelvis and both legs at one moment.</summary>
        public struct Pose
        {
            public float PelvisY;          // metres from rest
            public float PelvisYaw, PelvisRoll;
            public Leg Left, Right;
        }

        /// <summary>The waves that drive the pelvis and both legs for this gait.</summary>
        public static Wave[] Waves(Params p)
        {
            var waves = new List<Wave>
            {
                new Wave(Pelvis, Channel.PosY, u => Solve(p, u).PelvisY, p.Detail),
                new Wave(Pelvis, Channel.RotY, u => Solve(p, u).PelvisYaw, p.Detail),
                new Wave(Pelvis, Channel.RotZ, u => Solve(p, u).PelvisRoll, p.Detail),
            };
            AddLeg(waves, p, LegL, KneeL, AnkleL, left: true);
            AddLeg(waves, p, LegR, KneeR, AnkleR, left: false);
            return waves.ToArray();
        }

        static void AddLeg(List<Wave> waves, Params p, string hip, string knee, string ankle, bool left)
        {
            Leg Of(float u) => left ? Solve(p, u).Left : Solve(p, u).Right;
            waves.Add(new Wave(hip, Channel.RotX, u => Of(u).HipX, p.Detail));
            waves.Add(new Wave(hip, Channel.RotY, u => Of(u).HipY, p.Detail));
            waves.Add(new Wave(knee, Channel.RotX, u => Of(u).KneeX, p.Detail));
            waves.Add(new Wave(ankle, Channel.RotX, u => Of(u).AnkleX, p.Detail));
            waves.Add(new Wave(ankle, Channel.RotZ, u => Of(u).AnkleZ, p.Detail));
        }

        /// <summary>The pose at normalised time <paramref name="u"/> of the cycle (0 to 1).</summary>
        public static Pose Solve(Params p, float u)
        {
            u = Mathf.Repeat(u, 1f);
            const float tau = 2f * Mathf.PI;
            var pose = new Pose
            {
                PelvisY = -p.Drop + p.Bob * Mathf.Cos(2f * tau * (u - p.BobPeak)),
                PelvisYaw = p.Yaw * Mathf.Cos(tau * u),                       // left hip forward at its heel strike
                PelvisRoll = -p.Roll * Mathf.Sin(tau * (u - p.RollPhase)),   // right side drops while the left stands
            };

            Quaternion pelvis = Quaternion.Euler(0f, pose.PelvisYaw, pose.PelvisRoll);
            Vector3 pelvisAt = Vector3.up * (PelvisHeight + pose.PelvisY);
            pose.Left = SolveLeg(p, u, pelvis, pelvisAt, pose, left: true);
            pose.Right = SolveLeg(p, Mathf.Repeat(u + 0.5f, 1f), pelvis, pelvisAt, pose, left: false);
            return pose;
        }

        static Leg SolveLeg(Params p, float q, Quaternion pelvis, Vector3 pelvisAt, Pose pose, bool left)
        {
            float side = left ? -1f : 1f;
            Foot(p, q, left, out float ankleY, out float ankleZ, out float pitch);

            // The ankle's target in the leg's own frame: the pelvis' turn is taken back at the hip
            // (HipY), so the leg swings in a plane facing ahead.
            Vector3 hipAt = pelvisAt + pelvis * new Vector3(side * Hip.x, Hip.y, Hip.z);
            Quaternion legFrame = pelvis * Quaternion.Euler(0f, -pose.PelvisYaw, 0f);
            Vector3 target = new Vector3(side * Hip.x, ankleY, ankleZ);
            Vector3 local = Quaternion.Inverse(legFrame) * (target - hipAt);

            TwoBone(new Vector2(local.y, local.z), out float hip, out float knee);
            return new Leg
            {
                HipX = hip,
                HipY = -pose.PelvisYaw,
                KneeX = knee,
                AnkleX = pitch - hip - knee,   // the boot's pitch on the floor
                AnkleZ = -pose.PelvisRoll,     // its sole stays level across
            };
        }

        // The ankle and the boot's pitch at phase q of this leg's own cycle (0 = heel strike).
        static void Foot(Params p, float q, bool left, out float y, out float z, out float pitch)
        {
            float stride = p.Speed * p.Duty * p.Cycle;   // how far the planted boot travels back
            if (p.Duty >= 1f)
            {
                // Standing: the boot stays where it is, flat.
                pitch = 0f;
                Ankle(left ? p.StanceLeft : p.StanceRight, 0f, out y, out z);
                return;
            }

            if (q < p.Duty)
            {
                pitch = StancePitch(p, q);
                Ankle(stride * (0.5f - q / p.Duty), pitch, out y, out z);
                return;
            }

            // In the air: from the toe-off to the next heel strike.
            float s = (q - p.Duty) / (1f - p.Duty);
            Ankle(-0.5f * stride, p.Push, out float y0, out float z0);
            Ankle(0.5f * stride, p.Strike, out float y1, out float z1);
            float groundSpeed = -p.Speed * p.Cycle * (1f - p.Duty);   // dz/ds of a planted boot
            z = Hermite(z0, groundSpeed, z1, groundSpeed, s);
            y = Mathf.Lerp(y0, y1, s) + p.Lift * Mathf.Sin(Mathf.PI * s);
            pitch = Mathf.Lerp(p.Push, p.Strike, Smooth(s)) - p.ToeUp * Mathf.Sin(Mathf.PI * s);
        }

        // On the ground: lands toe-up, flat by 15% of the stance, heel up over the last 30%.
        static float StancePitch(Params p, float q)
        {
            float land = 0.15f * p.Duty, peel = 0.3f * p.Duty;
            if (q < land)
                return Mathf.Lerp(p.Strike, 0f, Smooth(q / land));
            if (q > p.Duty - peel)
                return p.Push * Smooth((q - (p.Duty - peel)) / peel);
            return 0f;
        }

        // The ankle of a boot whose sole would be flat at ground point z, pitched by
        // `pitch` degrees: heel-up turns about the sole's tip, toe-up about its heel.
        static void Ankle(float z, float pitch, out float ankleY, out float ankleZ)
        {
            float a = pitch * Mathf.Deg2Rad;
            float pivotZ = pitch >= 0f ? z + TipForward : z - HeelBack;
            float armZ = pitch >= 0f ? -TipForward : HeelBack;   // from that pivot to the ankle
            ankleY = AnkleHeight * Mathf.Cos(a) - armZ * Mathf.Sin(a);
            ankleZ = pivotZ + AnkleHeight * Mathf.Sin(a) + armZ * Mathf.Cos(a);
        }

        // Two-bone IK in the leg plane. A vector (y, z) pointing down has angle 0, and a +X turn
        // of θ adds θ: angle = atan2(−z, −y). Returns the hip and knee turns in degrees.
        static void TwoBone(Vector2 target, out float hip, out float knee)
        {
            float a = Thigh.magnitude, b = Shin.magnitude;
            float thighAngle = Angle(Thigh), shinAngle = Angle(Shin);
            float d = Mathf.Clamp(target.magnitude, Mathf.Abs(a - b) + 1e-4f, (a + b) * 0.9995f);

            // The bend between thigh and shin; positive bends the knee (the shin swings back).
            float bend = Mathf.Acos(Mathf.Clamp((d * d - a * a - b * b) / (2f * a * b), -1f, 1f));
            float thigh = Angle(target) - Mathf.Atan2(b * Mathf.Sin(bend), a + b * Mathf.Cos(bend));
            hip = (thigh - thighAngle) * Mathf.Rad2Deg;
            knee = (bend - (shinAngle - thighAngle)) * Mathf.Rad2Deg;
        }

        // v is (y, z), stored in (x, y).
        static float Angle(Vector2 v) => Mathf.Atan2(-v.y, -v.x);

        static float Hermite(float p0, float m0, float p1, float m1, float s)
        {
            float s2 = s * s, s3 = s2 * s;
            return (2f * s3 - 3f * s2 + 1f) * p0 + (s3 - 2f * s2 + s) * m0 + (-2f * s3 + 3f * s2) * p1 + (s3 - s2) * m1;
        }

        static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
