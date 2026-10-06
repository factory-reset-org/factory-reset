using System;
using UnityEngine;
using UnityEngine.Playables;
using ToyFactory.Interfaces;

namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>
    /// One cutscene: its id (the one S1's chapter data waits for), what triggers it, its
    /// Timeline, the Critical signals it must fire, and the game state to return to.
    /// </summary>
    /// <remarks>
    /// A cutscene with no Timeline yet still runs: it holds for <see cref="PlaceholderSeconds"/>
    /// and fires its Critical signals at the end, so the journey can be played through
    /// before the real Timelines exist.
    /// </remarks>
    [Serializable]
    public sealed class CutsceneDefinition
    {
        [Tooltip("Id raised in CutsceneEvents. Must match the chapter data's cutscene id (ch2, ch3, ch4).")]
        [SerializeField] string id;

        [SerializeField] CutsceneTrigger trigger;

        [Tooltip("For SwitchRestored: which switch, 1 to 3.")]
        [SerializeField] int switchNumber;

        [Tooltip("The Timeline to play. Leave empty to hold for the placeholder time instead.")]
        [SerializeField] PlayableAsset timeline;

        [Tooltip("Seconds to hold when there is no Timeline.")]
        [Min(0f)]
        [SerializeField] float placeholderSeconds = 2f;

        [Tooltip("CutsceneSignals ids this cutscene must fire. Any not reached by the Timeline fire on skip or at the end.")]
        [SerializeField] string[] criticalSignals = new string[0];

        [Tooltip("Game state after the cutscene: Playing, or Results for the ending.")]
        [SerializeField] GameState stateAfter = GameState.Playing;

        public string Id => id;
        public CutsceneTrigger Trigger => trigger;
        public int SwitchNumber => switchNumber;
        public PlayableAsset Timeline => timeline;
        public float PlaceholderSeconds => placeholderSeconds;
        public string[] CriticalSignals => criticalSignals;
        public GameState StateAfter => stateAfter;

        public CutsceneDefinition(string id, CutsceneTrigger trigger, int switchNumber = 0,
            string[] criticalSignals = null, GameState stateAfter = GameState.Playing,
            float placeholderSeconds = 2f, PlayableAsset timeline = null)
        {
            this.id = id;
            this.trigger = trigger;
            this.switchNumber = switchNumber;
            this.criticalSignals = criticalSignals ?? new string[0];
            this.stateAfter = stateAfter;
            this.placeholderSeconds = placeholderSeconds;
            this.timeline = timeline;
        }

        /// <summary>
        /// The journey's five cutscenes, as the plan orders them. Used as the director's
        /// defaults; ch2 to ch4 match the cutscene ids in S1's chapter data.
        /// </summary>
        public static CutsceneDefinition[] JourneyDefaults() => new[]
        {
            new CutsceneDefinition("intro", CutsceneTrigger.Manual),
            new CutsceneDefinition("ch2", CutsceneTrigger.SwitchRestored, 1),
            new CutsceneDefinition("ch3", CutsceneTrigger.SwitchRestored, 2,
                new[] { CutsceneSignals.CaptainWake, CutsceneSignals.ControlRoomUnlock }),
            new CutsceneDefinition("ch4", CutsceneTrigger.SwitchRestored, 3,
                new[] { CutsceneSignals.CoreShieldsDown }),
            new CutsceneDefinition("ending", CutsceneTrigger.ConsoleCompleted,
                criticalSignals: new[] { CutsceneSignals.FactoryShutdown }, stateAfter: GameState.Results),
        };
    }
}
