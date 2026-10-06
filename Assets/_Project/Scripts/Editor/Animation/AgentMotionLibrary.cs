using UnityEngine;

namespace ToyFactory.Editor.Animation
{
    /// <summary>
    /// The locomotion clips of the four agents, written as sine waves on the frozen pivots of
    /// the DesignDoc's model contract. Angles are degrees, heights metres, phases in cycles.
    /// </summary>
    /// <remarks>
    /// <para><b>Axes</b> (every pivot has rest rotation 0, so these hold for all of them):
    /// +X pitches forward (a leg swings backward, a claw closes downward), +Y turns right,
    /// +Z rolls left. So the left-turn clip leans with +Z and the right-turn clip with −Z.</para>
    /// <para><b>Not keyed here:</b> wheels and the wind-up key turn in code from the live
    /// speed and energy (<c>WheelSpinner</c>, <c>WindUpKeySpinner</c>), so they never slip.</para>
    /// <para><b>Captain stride:</b> a hip swing of ±θ moves the foot 2·l·sin θ per step for a
    /// leg of length l = 1.08 m (hip height). A cycle is two steps, so the cycle that keeps the
    /// boots from sliding at speed v is <c>L = 2 · 2·l·sin θ / v</c>: walk ±22° at 2.5 m/s gives
    /// 0.65 s, run ±35° at 4.6 m/s gives 0.54 s.</para>
    /// </remarks>
    public static class AgentMotionLibrary
    {
        /// <summary>Turn rate (degrees per second) at which the lean clips are at full weight.</summary>
        public const float LeanTurnRate = 180f;

        public static AgentMotionSpec[] All => new[] { Tracker(), Saboteur(), Guard(), Captain() };

        static ClipSpec Clip(string name, float length, params Wave[] waves) => new ClipSpec(name, length, waves);

        static Wave[] With(ClipSpec clip, params Wave[] extra)
        {
            var all = new Wave[clip.Waves.Length + extra.Length];
            clip.Waves.CopyTo(all, 0);
            extra.CopyTo(all, clip.Waves.Length);
            return all;
        }

        static AgentMotionSpec Leaning(AgentMotionSpec spec, string pivot, float degrees)
        {
            spec.LeanLeft = Clip("LeanLeft", spec.Run.Length, With(spec.Run, new Wave(pivot, Channel.RotZ, degrees)));
            spec.LeanRight = Clip("LeanRight", spec.Run.Length, With(spec.Run, new Wave(pivot, Channel.RotZ, -degrees)));
            return spec;
        }

        /// <summary>Tracker: a wind-up dog on wheels. Bobs, wags its tail, ears flatten at speed.</summary>
        public static AgentMotionSpec Tracker()
        {
            const string body = "TrackerToy_Root/Body_Pivot";
            const string head = body + "/Head_Pivot";
            const string earL = head + "/Ear_L_Pivot", earR = head + "/Ear_R_Pivot";
            const string tail = body + "/Tail_Pivot";

            var spec = new AgentMotionSpec { Model = "TrackerToy", WalkSpeed = 1.9f, RunSpeed = 4.6f };
            spec.Idle = Clip("Idle", 2f,
                new Wave(body, Channel.PosY, 0f, 0.01f, 1),
                new Wave(head, Channel.RotY, 0f, 12f, 1, 0.25f),
                new Wave(head, Channel.RotX, 0f, 3f, 2),
                new Wave(tail, Channel.RotY, 0f, 15f, 2),
                new Wave(earL, Channel.RotX, 0f, 6f, 2),
                new Wave(earR, Channel.RotX, 0f, 6f, 2, 0.5f));
            spec.Walk = Clip("Walk", 1f,
                new Wave(body, Channel.PosY, 0f, 0.02f, 4),
                new Wave(body, Channel.RotX, 4f, 1.5f, 4),
                new Wave(head, Channel.RotX, -3f, 2f, 4, 0.5f),
                new Wave(tail, Channel.RotY, 0f, 25f, 2),
                new Wave(earL, Channel.RotX, -8f, 6f, 4),
                new Wave(earR, Channel.RotX, -8f, 6f, 4, 0.5f));
            spec.Run = Clip("Run", 0.6f,
                new Wave(body, Channel.PosY, 0f, 0.03f, 4),
                new Wave(body, Channel.RotX, 8f, 2f, 4),
                new Wave(head, Channel.RotX, -6f, 2f, 4, 0.5f),
                new Wave(tail, Channel.RotY, 0f, 35f, 3),
                new Wave(earL, Channel.RotX, -25f, 5f, 4),
                new Wave(earR, Channel.RotX, -25f, 5f, 4, 0.5f));
            return Leaning(spec, body, 10f);
        }

        /// <summary>Saboteur: balances on one wheel. Leans into its speed, swings its arms, snaps its claws.</summary>
        public static AgentMotionSpec Saboteur()
        {
            const string body = "SaboteurBot_Root/Body_Pivot";
            const string shL = body + "/Shoulder_L_Pivot", shR = body + "/Shoulder_R_Pivot";
            const string elL = shL + "/Elbow_L_Pivot", elR = shR + "/Elbow_R_Pivot";
            const string topL = elL + "/Claw_L_Top_Pivot", botL = elL + "/Claw_L_Bottom_Pivot";
            const string topR = elR + "/Claw_R_Top_Pivot", botR = elR + "/Claw_R_Bottom_Pivot";

            // Claws: top closes with +X, bottom with −X; they meet about 4° each from rest.
            Wave[] snap(int cycles) => new[]
            {
                new Wave(topL, Channel.RotX, -4f, 4f, cycles), new Wave(botL, Channel.RotX, 4f, -4f, cycles),
                new Wave(topR, Channel.RotX, -4f, 4f, cycles, 0.5f), new Wave(botR, Channel.RotX, 4f, -4f, cycles, 0.5f),
            };

            var spec = new AgentMotionSpec { Model = "SaboteurBot", WalkSpeed = 2.2f, RunSpeed = 4.3f };
            spec.Idle = Clip("Idle", 2f, Concat(new[]
            {
                new Wave(body, Channel.RotX, 0f, 2f, 1),                 // balancing on the wheel
                new Wave(body, Channel.RotZ, 0f, 1.5f, 1, 0.25f),
                new Wave(shL, Channel.RotX, 0f, 4f, 1), new Wave(shR, Channel.RotX, 0f, 4f, 1, 0.5f),
                new Wave(elL, Channel.RotX, -5f), new Wave(elR, Channel.RotX, -5f),
            }, snap(2)));
            spec.Walk = Clip("Walk", 1f, Concat(new[]
            {
                new Wave(body, Channel.RotX, 8f, 2f, 2),
                new Wave(body, Channel.RotZ, 0f, 2f, 1),
                new Wave(shL, Channel.RotX, 0f, 15f, 1), new Wave(shR, Channel.RotX, 0f, 15f, 1, 0.5f),
                new Wave(elL, Channel.RotX, -10f, 5f, 1), new Wave(elR, Channel.RotX, -10f, 5f, 1, 0.5f),
            }, snap(2)));
            spec.Run = Clip("Run", 0.6f, Concat(new[]
            {
                new Wave(body, Channel.RotX, 15f, 2f, 2),
                new Wave(body, Channel.RotZ, 0f, 1f, 1),
                // Arms rest pointing forward, so +X lowers them: tucked down while racing.
                new Wave(shL, Channel.RotX, 20f, 6f, 1), new Wave(shR, Channel.RotX, 20f, 6f, 1, 0.5f),
                new Wave(elL, Channel.RotX, -20f), new Wave(elR, Channel.RotX, -20f),
            }, snap(1)));
            return Leaning(spec, body, 12f);
        }

        /// <summary>Guard: rigid treads, so the motion is in the torso; it scans with its head when idle.</summary>
        public static AgentMotionSpec Guard()
        {
            const string torso = "GuardBot_Root/Torso_Pivot";
            const string head = torso + "/Head_Pivot";
            const string shield = torso + "/ShieldArm_L_Pivot", cannon = torso + "/CannonArm_R_Pivot";

            var spec = new AgentMotionSpec { Model = "GuardBot", WalkSpeed = 2f, RunSpeed = 4f };
            spec.Idle = Clip("Idle", 3f,
                new Wave(torso, Channel.PosY, 0f, 0.008f, 1),
                new Wave(head, Channel.RotY, 0f, 25f, 1),               // scanning
                new Wave(cannon, Channel.RotX, -5f, 2f, 1),
                new Wave(shield, Channel.RotX, 0f, 2f, 1, 0.5f));
            spec.Walk = Clip("Walk", 1f,
                new Wave(torso, Channel.PosY, 0f, 0.015f, 4),           // tread judder
                new Wave(torso, Channel.RotX, 3f, 1f, 2),
                new Wave(head, Channel.RotY, 0f, 8f, 1),
                new Wave(cannon, Channel.RotX, -10f, 3f, 2),
                new Wave(shield, Channel.RotX, -5f, 3f, 2, 0.5f));
            spec.Run = Clip("Run", 0.6f,
                new Wave(torso, Channel.PosY, 0f, 0.02f, 4),
                new Wave(torso, Channel.RotX, 7f, 1.5f, 2),
                new Wave(head, Channel.RotX, -5f),
                new Wave(cannon, Channel.RotX, -20f, 3f, 2),
                new Wave(shield, Channel.RotX, -15f, 3f, 2, 0.5f));
            return Leaning(spec, torso, 6f);
        }

        /// <summary>Captain: legged. A stride-matched walk and run, torso bob and counter-swinging cannon arms.</summary>
        public static AgentMotionSpec Captain()
        {
            const string root = "CaptainBot_Root";
            const string legL = root + "/Leg_L_Pivot", legR = root + "/Leg_R_Pivot";
            const string kneeL = legL + "/Knee_L_Pivot", kneeR = legR + "/Knee_R_Pivot";
            const string ankleL = kneeL + "/Ankle_L_Pivot", ankleR = kneeR + "/Ankle_R_Pivot";
            const string torso = root + "/Torso_Pivot";
            const string head = torso + "/Head_Pivot";
            const string armL = torso + "/CannonArm_L_Pivot", armR = torso + "/CannonArm_R_Pivot";

            // One gait cycle: hips ±swing (−X forward), the knee bends only while the leg
            // swings forward (peak at mid-swing, phase 0.75 after the hip), the ankle keeps the
            // boot roughly level, the torso bobs twice per cycle and the arms swing against the
            // legs on the same side.
            Wave[] gait(float swing, float knee, float bob, float arm, float armOffset) => new[]
            {
                new Wave(legL, Channel.RotX, 0f, swing, 1), new Wave(legR, Channel.RotX, 0f, swing, 1, 0.5f),
                new Wave(kneeL, Channel.RotX, knee, knee, 1, 0.75f), new Wave(kneeR, Channel.RotX, knee, knee, 1, 0.25f),
                new Wave(ankleL, Channel.RotX, -knee * 0.5f, -knee * 0.5f, 1, 0.75f),
                new Wave(ankleR, Channel.RotX, -knee * 0.5f, -knee * 0.5f, 1, 0.25f),
                new Wave(torso, Channel.PosY, 0f, bob, 2),
                new Wave(torso, Channel.RotY, 0f, 4f, 1, 0.5f),
                new Wave(armL, Channel.RotX, armOffset, arm, 1, 0.5f), new Wave(armR, Channel.RotX, armOffset, arm, 1),
            };

            var spec = new AgentMotionSpec { Model = "CaptainBot", WalkSpeed = 2.5f, RunSpeed = 4.6f };
            spec.Idle = Clip("Idle", 3f,
                new Wave(torso, Channel.PosY, 0f, 0.012f, 1),           // breathing
                new Wave(head, Channel.RotY, 0f, 10f, 1, 0.25f),
                new Wave(armL, Channel.RotX, 0f, 2f, 1), new Wave(armR, Channel.RotX, 0f, 2f, 1, 0.5f));
            spec.Walk = Clip("Walk", StrideCycle(22f, 2.5f), gait(22f, 17f, 0.04f, 10f, 0f));
            spec.Run = Clip("Run", StrideCycle(35f, 4.6f),
                With(Clip("", 0f, gait(35f, 30f, 0.06f, 20f, -10f)), new Wave(torso, Channel.RotX, 8f)));
            return Leaning(spec, torso, 8f);
        }

        /// <summary>Seconds per gait cycle (two steps) that keeps the Captain's boots from sliding.</summary>
        public static float StrideCycle(float hipSwingDegrees, float speed)
        {
            const float legLength = 1.08f;   // hip pivot height
            float step = 2f * legLength * Mathf.Sin(hipSwingDegrees * Mathf.Deg2Rad);
            return 2f * step / speed;
        }

        static Wave[] Concat(Wave[] a, Wave[] b)
        {
            var all = new Wave[a.Length + b.Length];
            a.CopyTo(all, 0);
            b.CopyTo(all, a.Length);
            return all;
        }
    }
}
