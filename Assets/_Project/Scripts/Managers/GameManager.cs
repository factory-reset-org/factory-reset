using System.Collections.Generic;
using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Managers
{
    /// <summary>
    /// Owns the single game state and the game clock that only advances while
    /// Playing. Other systems implement <see cref="IGameStateListener"/> and register
    /// here to react to a state change instead of polling every frame.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [SerializeField] GameState startState = GameState.Title;

        readonly List<IGameStateListener> _listeners = new List<IGameStateListener>();

        /// <summary>The current game state.</summary>
        public GameState State { get; private set; }

        /// <summary>Seconds elapsed while Playing. Frozen during Cutscene and Paused.</summary>
        public float GameTime { get; private set; }

        void Awake()
        {
            Instance = this;
            State = startState;
        }

        void Update()
        {
            if (State == GameState.Playing)
                GameTime += Time.deltaTime;
        }

        /// <summary>Adds a listener notified on every future state change.</summary>
        public void AddListener(IGameStateListener listener) => _listeners.Add(listener);

        /// <summary>Removes a previously added listener.</summary>
        public void RemoveListener(IGameStateListener listener) => _listeners.Remove(listener);

        /// <summary>
        /// Switches to a new state and notifies every registered listener. No effect
        /// if already in that state.
        /// </summary>
        public void SetState(GameState newState)
        {
            if (newState == State)
                return;

            GameState previous = State;
            State = newState;

            for (int i = 0; i < _listeners.Count; i++)
                _listeners[i].OnGameStateChanged(previous, newState);
        }
    }
}
