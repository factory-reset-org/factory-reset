namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>
    /// Plays a cutscene's Timeline for <see cref="CutsceneRunner"/>. The director implements
    /// it with a PlayableDirector; tests use a fake, so the runner is tested without Unity.
    /// </summary>
    public interface ICutscenePlayback
    {
        /// <summary>Starts the cutscene's Timeline. False if it has none, so the runner holds instead.</summary>
        bool Play(CutsceneDefinition cutscene);

        /// <summary>Freezes (true) or resumes (false) the playing Timeline, for the pause menu.</summary>
        void SetHeld(bool held);

        /// <summary>Stops the Timeline early (skip).</summary>
        void Stop();

        /// <summary>True once the Timeline started by <see cref="Play"/> has reached its end.</summary>
        bool IsFinished { get; }
    }
}
