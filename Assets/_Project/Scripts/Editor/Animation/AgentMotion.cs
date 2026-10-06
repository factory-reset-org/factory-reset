using System.Collections.Generic;

namespace ToyFactory.Editor.Animation
{
    /// <summary>Which transform value a wave drives. Rotations are local Euler degrees; PosY is metres above rest.</summary>
    public enum Channel { RotX, RotY, RotZ, PosY }

    /// <summary>
    /// One looping sine on one pivot: <c>value(t) = offset + amplitude · sin(2π · (cycles · t/L + phase))</c>,
    /// with t from 0 to the clip length L. Whole numbers of cycles make every clip loop seamlessly.
    /// </summary>
    public readonly struct Wave
    {
        public readonly string Path;
        public readonly Channel Channel;
        public readonly float Offset;
        public readonly float Amplitude;
        public readonly int Cycles;
        public readonly float Phase;

        public Wave(string path, Channel channel, float offset, float amplitude = 0f, int cycles = 1, float phase = 0f)
        {
            Path = path;
            Channel = channel;
            Offset = offset;
            Amplitude = amplitude;
            Cycles = cycles;
            Phase = phase;
        }
    }

    /// <summary>One looping clip: a name, a length and its waves.</summary>
    public sealed class ClipSpec
    {
        public readonly string Name;
        public readonly float Length;
        public readonly Wave[] Waves;

        public ClipSpec(string name, float length, params Wave[] waves)
        {
            Name = name;
            Length = length;
            Waves = waves;
        }
    }

    /// <summary>
    /// The locomotion set for one agent: five clips placed on a 2D blend tree over Speed
    /// (m/s) and TurnRate (degrees per second, positive = right).
    /// </summary>
    public sealed class AgentMotionSpec
    {
        /// <summary>Folder name under Models/, Animations/ and the S3 prefab name.</summary>
        public string Model;
        public float WalkSpeed;
        public float RunSpeed;
        public ClipSpec Idle, Walk, Run, LeanLeft, LeanRight;

        /// <summary>
        /// Optional aim pose for agents that shoot, played on an override layer whose weight
        /// the animator bridge fades in while attacking. Null for agents that do not shoot.
        /// </summary>
        public ClipSpec Aim;

        public IEnumerable<ClipSpec> Clips
        {
            get
            {
                yield return Idle;
                yield return Walk;
                yield return Run;
                yield return LeanLeft;
                yield return LeanRight;
            }
        }
    }
}
