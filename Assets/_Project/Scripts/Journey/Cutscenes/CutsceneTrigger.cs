namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>What makes a cutscene play.</summary>
    public enum CutsceneTrigger
    {
        /// <summary>Only when something calls <see cref="CutsceneDirector.Play"/> (the intro).</summary>
        Manual,

        /// <summary>A control switch was restored (<c>ChapterEvents.OnSwitchRestored</c>).</summary>
        SwitchRestored,

        /// <summary>The Chapter 4 console hold completed (the ending).</summary>
        ConsoleCompleted
    }
}
