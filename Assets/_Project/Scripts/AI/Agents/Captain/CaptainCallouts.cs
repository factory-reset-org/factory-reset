namespace ToyFactory.AI.Agents.Captain
{
    /// <summary>
    /// When the Captain lets the player know it has read them. The prediction is the Captain's
    /// whole idea, and without a sign of it the player only sees a robot that is somehow ahead
    /// of them. On every new commitment (a goal it starts cutting off or guarding, or a switch
    /// to another goal) it flashes a red marker on that goal.
    /// </summary>
    /// <remarks>
    /// <para>The flash comes at most every <see cref="MarkCooldown"/> seconds, so a Captain
    /// changing its mind quickly does not strobe. It shows wherever the Captain is: the
    /// player learns it has been read even when it is out of sight.</para>
    /// <para>Until 15 October it also said a comic line above its head ("THE CONSOLE. OF
    /// COURSE."); the lines were removed. The cutscenes keep their own comic words. Pure C#,
    /// so the rule is tested without a scene; the body only shows the result.</para>
    /// </remarks>
    public sealed class CaptainCallouts
    {
        public const float MarkCooldown = 2f;

        const int NoGoal = int.MinValue;

        int _committedId = NoGoal;
        float _lastMarkAt = float.NegativeInfinity;

        /// <summary>
        /// Call once a frame with the Captain's commitment. True when the marker should flash
        /// on <paramref name="goal"/>: a new commitment, and the last flash was long enough ago.
        /// </summary>
        /// <param name="time">Game time in seconds.</param>
        /// <param name="committed">True while it is committed to a goal.</param>
        /// <param name="goal">The committed goal (ignored when not committed).</param>
        public bool Update(float time, bool committed, in CandidateGoal goal)
        {
            if (!committed)
            {
                _committedId = NoGoal;
                return false;
            }
            if (goal.Id == _committedId)
                return false;

            // A new commitment: from nothing, or a switch to another goal.
            _committedId = goal.Id;
            if (time - _lastMarkAt < MarkCooldown)
                return false;
            _lastMarkAt = time;
            return true;
        }
    }
}
