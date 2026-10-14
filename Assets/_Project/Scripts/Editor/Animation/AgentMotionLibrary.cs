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
    /// <para><b>Captain:</b> the only agent on legs, so its legs are not sines but solved
    /// (<see cref="CaptainGait"/>): a planted boot travels back under the body at the clip's
    /// speed, so it stays still on the floor. Walk: 0.64 s cycles (0.8 m steps) at 2.5 m/s;
    /// run: 0.52 s cycles (1.2 m steps) at 4.6 m/s.</para>
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
            // Aim: the cannon arm lifts the barrel towards chest height and the shield comes
            // forward. The barrel already points forward at rest, so the lift is small.
            spec.Aim = Clip("Aim", 1f,
                new Wave(cannon, Channel.RotX, -20f),
                new Wave(shield, Channel.RotX, -25f),
                new Wave(torso, Channel.RotX, 4f),
                new Wave(head, Channel.RotX, 5f));
            return Leaning(spec, torso, 6f);
        }

        // ---- Captain ---------------------------------------------------------------------

        const string CaptainWaist = CaptainGait.Pelvis + "/Waist_Pivot";
        const string CaptainChest = CaptainWaist + "/Chest_Pivot";
        const string CaptainNeck = CaptainChest + "/Neck_Pivot";
        const string CaptainHead = CaptainNeck + "/Head_Pivot";
        const string CaptainHat = CaptainHead + "/Hat_Pivot";
        const string CaptainClavicleL = CaptainChest + "/Clavicle_L_Pivot", CaptainClavicleR = CaptainChest + "/Clavicle_R_Pivot";
        const string CaptainShoulderL = CaptainClavicleL + "/Shoulder_L_Pivot", CaptainShoulderR = CaptainClavicleR + "/Shoulder_R_Pivot";
        const string CaptainElbowL = CaptainShoulderL + "/Elbow_L_Pivot", CaptainElbowR = CaptainShoulderR + "/Elbow_R_Pivot";
        const string CaptainWrist = CaptainElbowL + "/Wrist_L_Pivot";

        /// <summary>The Captain's walk: brisk, planted steps with a bouncy vault over each standing leg.</summary>
        public static readonly CaptainGait.Params CaptainWalk = new CaptainGait.Params
        {
            Cycle = 0.64f, Speed = 2.5f, Duty = 0.6f,
            Drop = 0.075f, Bob = 0.05f, BobPeak = 0.3f,          // highest over the standing leg
            Lift = 0.16f, Strike = -10f, Push = 16f, ToeUp = 8f,
            Yaw = 5f, Roll = 3f, RollPhase = 0.3f - 0.25f,
        };

        /// <summary>The Captain's run: a heavy charge, crouched, both boots off the floor between steps.</summary>
        public static readonly CaptainGait.Params CaptainRun = new CaptainGait.Params
        {
            Cycle = 0.52f, Speed = 4.6f, Duty = 0.38f, Detail = 9,   // a quicker heel strike needs more keys
            Drop = 0.12f, Bob = 0.03f, BobPeak = 0.44f,          // highest in the air, lowest mid-stance
            Lift = 0.3f, Strike = -6f, Push = 22f, ToeUp = 10f,
            Yaw = 7f, Roll = 2.5f, RollPhase = 0.19f - 0.25f,
        };

        /// <summary>The Captain standing: knees just off straight, one boot a little ahead, breathing and shifting its weight.</summary>
        public static readonly CaptainGait.Params CaptainStand = new CaptainGait.Params
        {
            Cycle = 4f, Duty = 1f, Detail = 2,
            Drop = 0.015f, Bob = 0.006f, BobPeak = 0f,
            Roll = 1.2f, RollPhase = 0f,
            StanceLeft = 0.06f, StanceRight = -0.04f,
        };

        /// <summary>
        /// Captain: legged, so its legs are solved (<see cref="CaptainGait"/>): the boots stay
        /// planted on the floor at the speed of each clip. The upper body swings against the legs
        /// on sines: arms against the legs, the waist against the pelvis, the head kept level and
        /// looking ahead, and the cap lagging a beat behind the bounce.
        /// </summary>
        public static AgentMotionSpec Captain()
        {
            var spec = new AgentMotionSpec { Model = "CaptainBot", WalkSpeed = CaptainWalk.Speed, RunSpeed = CaptainRun.Speed };

            spec.Idle = Clip("Idle", CaptainStand.Cycle, Concat(CaptainGait.Waves(CaptainStand), new[]
            {
                // Breathing: the chest swells and the shoulders rise with it (two breaths per loop).
                new Wave(CaptainChest, Channel.RotX, 0f, -1.2f, 2),
                new Wave(CaptainClavicleL, Channel.RotZ, 0f, -1.2f, 2), new Wave(CaptainClavicleR, Channel.RotZ, 0f, 1.2f, 2),
                // The waist holds the chest upright over the shifting pelvis.
                new Wave(CaptainWaist, Channel.RotZ, 0f, 0.9f, 1),
                // Looking around the room, slowly, with a small nod.
                new Wave(CaptainNeck, Channel.RotY, 0f, 14f, 1, 0.25f),
                new Wave(CaptainHead, Channel.RotX, 1f, 2f, 2, 0.1f),
                // Arms hang a little out and bent; the fist flexes.
                new Wave(CaptainShoulderL, Channel.RotX, 2f, 1.5f, 1), new Wave(CaptainShoulderL, Channel.RotZ, -5f),
                new Wave(CaptainElbowL, Channel.RotX, -14f, 2f, 2),
                new Wave(CaptainWrist, Channel.RotX, 4f, 3f, 2, 0.3f),
                new Wave(CaptainShoulderR, Channel.RotX, 1f, 1.5f, 1, 0.5f), new Wave(CaptainShoulderR, Channel.RotZ, 5f),
                new Wave(CaptainElbowR, Channel.RotX, -12f, 1.5f, 2, 0.5f),
                new Wave(CaptainHat, Channel.RotX, 0f, 0.6f, 2, 0.05f),
            }));

            spec.Walk = Clip("Walk", CaptainWalk.Cycle, Concat(CaptainGait.Waves(CaptainWalk),
                UpperBody(CaptainWalk, lean: 4f, armSwing: 20f, cannonSwing: 13f, elbow: 14f, elbowSwing: 10f,
                    chestDip: 1.5f, hatLag: 1.5f, armsOut: 5f)));

            spec.Run = Clip("Run", CaptainRun.Cycle, Concat(CaptainGait.Waves(CaptainRun),
                UpperBody(CaptainRun, lean: 12f, armSwing: 34f, cannonSwing: 24f, elbow: 60f, elbowSwing: 16f,
                    chestDip: 3f, hatLag: 3f, armsOut: 8f)));

            spec.Aim = CaptainAim();

            // Banking into a turn from the waist up, so the planted boots stay flat.
            return Leaning(spec, CaptainWaist, 8f);
        }

        // The upper body over a gait whose left heel strikes at 0: the pelvis turns with the
        // forward leg and the waist turns back past it, so the shoulders counter-swing the hips;
        // the arms swing against the legs (left arm forward at 0.5) with the elbows following a
        // beat late; the chest dips just after each heel strike; the head stays level and the
        // cap lags the bounce.
        static Wave[] UpperBody(CaptainGait.Params gait, float lean, float armSwing, float cannonSwing, float elbow,
            float elbowSwing, float chestDip, float hatLag, float armsOut)
        {
            const float follow = 0.04f;   // elbows and wrist trail the shoulders (cycles)
            // Phases that turn a sine into cos(2π(u − lag)), once or twice per cycle.
            float cos(float lag) => 0.25f - lag;
            float cos2(float lag) => 0.25f - 2f * lag;
            return new[]
            {
                new Wave(CaptainWaist, Channel.RotX, lean),
                new Wave(CaptainWaist, Channel.RotY, 0f, -(gait.Yaw + 3f), 1, cos(0f)),
                new Wave(CaptainWaist, Channel.RotZ, 0f, 0.7f * gait.Roll, 1, -gait.RollPhase),   // against the pelvis' roll
                new Wave(CaptainChest, Channel.RotX, 0f, chestDip, 2, cos2(0.08f)),
                new Wave(CaptainNeck, Channel.RotY, 0f, 3f, 1, cos(0f)),
                new Wave(CaptainNeck, Channel.RotX, -0.7f * lean, -chestDip, 2, cos2(0.08f)),
                new Wave(CaptainHat, Channel.RotX, 0f, hatLag, 2, cos2(0.18f)),

                new Wave(CaptainClavicleL, Channel.RotY, 0f, -3f, 1, cos(0f)),
                new Wave(CaptainClavicleR, Channel.RotY, 0f, -3f, 1, cos(0f)),
                new Wave(CaptainShoulderL, Channel.RotX, 0f, armSwing, 1, cos(follow)),
                new Wave(CaptainShoulderL, Channel.RotZ, -armsOut),
                new Wave(CaptainElbowL, Channel.RotX, -elbow, elbowSwing, 1, cos(2f * follow)),
                new Wave(CaptainWrist, Channel.RotX, 0f, 6f, 1, cos(3f * follow)),
                new Wave(CaptainShoulderR, Channel.RotX, 0f, -cannonSwing, 1, cos(follow)),
                new Wave(CaptainShoulderR, Channel.RotZ, armsOut),
                new Wave(CaptainElbowR, Channel.RotX, -elbow, -elbowSwing, 1, cos(2f * follow)),
            };
        }

        // Aim: the cannon arm comes up level and forward, the chest turns the cannon side forward
        // (a fencer's stance, so the cannon lines up under the eyes), the head turns back to
        // face the target and tips down at the much shorter player, and the left fist comes up
        // in a guard. Only the upper body is keyed: the legs keep walking underneath.
        // AgentCannonAim then turns the shoulder so the barrel points exactly at the target.
        static ClipSpec CaptainAim() => Clip("Aim", 1f,
            new Wave(CaptainChest, Channel.RotX, -3f), new Wave(CaptainChest, Channel.RotY, -10f),
            new Wave(CaptainNeck, Channel.RotY, 9f), new Wave(CaptainNeck, Channel.RotX, 2f),
            new Wave(CaptainHead, Channel.RotX, 5f), new Wave(CaptainHat, Channel.RotX, 0f),
            new Wave(CaptainClavicleR, Channel.RotY, 3f), new Wave(CaptainClavicleR, Channel.RotZ, 0f),
            new Wave(CaptainShoulderR, Channel.RotX, -76f), new Wave(CaptainShoulderR, Channel.RotZ, 0f),
            new Wave(CaptainElbowR, Channel.RotX, -14f),
            new Wave(CaptainClavicleL, Channel.RotY, 4f), new Wave(CaptainClavicleL, Channel.RotZ, -2f),
            new Wave(CaptainShoulderL, Channel.RotX, 6f), new Wave(CaptainShoulderL, Channel.RotZ, -12f),
            new Wave(CaptainElbowL, Channel.RotX, -100f),
            new Wave(CaptainWrist, Channel.RotX, 8f));

        static Wave[] Concat(Wave[] a, Wave[] b)
        {
            var all = new Wave[a.Length + b.Length];
            a.CopyTo(all, 0);
            b.CopyTo(all, a.Length);
            return all;
        }
    }
}
