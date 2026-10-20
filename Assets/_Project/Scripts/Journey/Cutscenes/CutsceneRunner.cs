using System;
using System.Collections.Generic;
using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>
    /// The rules of running a cutscene, kept apart from Unity so they can be tested:
    /// wait the start delay, put the game in the Cutscene state, play the Timeline (or hold
    /// for a placeholder), fire Critical signals once each, then end it and hand the game
    /// back. Requests that arrive while one cutscene runs wait their turn.
    /// </summary>
    /// <remarks>
    /// <para><b>Order at the end.</b> Every Critical signal the Timeline did not reach fires
    /// first, then <see cref="CutsceneEvents.OnCutsceneEnded"/>, then the game state returns
    /// to Playing. The chapter manager starts the next chapter on the ended event, so its
    /// objectives are already on the blackboard when the agents unfreeze.</para>
    /// <para><b>Pause.</b> The start delay only counts while the game can start a cutscene,
    /// and a playing cutscene is held while Paused, so the pause menu never loses or shortens
    /// one. A skip is ignored while paused.</para>
    /// <para><b>Missing data.</b> An unknown id still raises started and ended, with a
    /// warning, so a missing cutscene can never stall the journey.</para>
    /// </remarks>
    public sealed class CutsceneRunner
    {
        /// <summary>Seconds between a switch being restored and its cutscene starting.</summary>
        public const float DefaultStartDelay = 1.3f;

        enum Phase { Idle, Waiting, Playing }

        readonly struct Pending
        {
            public readonly CutsceneDefinition Cutscene;
            public readonly float Delay;
            public Pending(CutsceneDefinition cutscene, float delay)
            {
                Cutscene = cutscene;
                Delay = delay;
            }
        }

        readonly IReadOnlyList<CutsceneDefinition> _cutscenes;
        readonly ICutscenePlayback _playback;
        readonly Func<IGameClock> _clock;
        readonly Queue<Pending> _queue = new Queue<Pending>();
        readonly HashSet<string> _signalsFired = new HashSet<string>();

        Phase _phase;
        CutsceneDefinition _current;
        float _timer;
        bool _usesTimeline;
        bool _held;

        /// <param name="cutscenes">Every cutscene the game has.</param>
        /// <param name="playback">Plays Timelines; may be null, then every cutscene holds.</param>
        /// <param name="clock">Returns the game clock, or null in scenes without a GameManager.</param>
        public CutsceneRunner(IReadOnlyList<CutsceneDefinition> cutscenes, ICutscenePlayback playback,
            Func<IGameClock> clock)
        {
            _cutscenes = cutscenes ?? throw new ArgumentNullException(nameof(cutscenes));
            _playback = playback;
            _clock = clock ?? (() => null);
        }

        /// <summary>True while a cutscene is on screen (not while waiting for its start delay).</summary>
        public bool IsPlaying => _phase == Phase.Playing;

        /// <summary>True while a cutscene is waiting to start or playing.</summary>
        public bool IsBusy => _phase != Phase.Idle;

        /// <summary>The cutscene on screen, or null.</summary>
        public string CurrentId => IsPlaying ? _current.Id : null;

        /// <summary>
        /// Plays a cutscene by id after <paramref name="delay"/> seconds. An unknown id still
        /// raises started and ended (with a warning). False if it is unknown or already
        /// playing or queued.
        /// </summary>
        public bool Request(string cutsceneId, float delay)
        {
            if (string.IsNullOrEmpty(cutsceneId))
                return false;

            CutsceneDefinition cutscene = Find(c => c.Id == cutsceneId);
            if (cutscene == null)
            {
                Debug.LogWarning($"No cutscene \"{cutsceneId}\" is set up; raising its start and end so the journey carries on.");
                Enqueue(new CutsceneDefinition(cutsceneId, CutsceneTrigger.Manual, placeholderSeconds: 0f), delay);
                return false;
            }
            return Enqueue(cutscene, delay);
        }

        /// <summary>Plays the cutscene that follows switch <paramref name="switchNumber"/>.</summary>
        public bool RequestForSwitch(int switchNumber, float delay)
        {
            CutsceneDefinition cutscene = Find(c =>
                c.Trigger == CutsceneTrigger.SwitchRestored && c.SwitchNumber == switchNumber);
            if (cutscene == null)
            {
                Debug.LogError($"No cutscene is set up for switch {switchNumber}, so the next chapter cannot start.");
                return false;
            }
            return Enqueue(cutscene, delay);
        }

        /// <summary>Plays the first cutscene with <paramref name="trigger"/> (the ending).</summary>
        public bool RequestForTrigger(CutsceneTrigger trigger, float delay)
        {
            CutsceneDefinition cutscene = Find(c => c.Trigger == trigger);
            return cutscene != null && Enqueue(cutscene, delay);
        }

        /// <summary>Advances the start delay or the playing cutscene by <paramref name="deltaTime"/> seconds.</summary>
        public void Tick(float deltaTime)
        {
            IGameClock clock = _clock();
            switch (_phase)
            {
                case Phase.Waiting:
                    if (!CanStart(clock))
                        return;
                    _timer -= deltaTime;
                    if (_timer <= 0f)
                        Begin(clock);
                    return;

                case Phase.Playing:
                    bool held = clock != null && clock.State == GameState.Paused;
                    if (held != _held)
                    {
                        _held = held;
                        if (_usesTimeline)
                            _playback.SetHeld(held);
                    }
                    if (held)
                        return;

                    bool finished = _usesTimeline ? _playback.IsFinished : (_timer -= deltaTime) <= 0f;
                    if (finished)
                        Finish(clock);
                    return;
            }
        }

        /// <summary>The Timeline reached a Critical signal. Each id fires once per cutscene.</summary>
        public void SignalReached(string signalId)
        {
            if (!IsPlaying || string.IsNullOrEmpty(signalId))
                return;
            if (_signalsFired.Add(signalId))
                CutsceneEvents.RaiseCriticalSignal(signalId);
        }

        /// <summary>
        /// Ends the playing cutscene now. Its Critical signals not yet reached fire first, so
        /// skipping leaves the game as if it had been watched. Ignored while paused.
        /// </summary>
        public void Skip()
        {
            if (!IsPlaying || _held)
                return;
            if (_usesTimeline)
                _playback.Stop();
            Finish(_clock());
        }

        bool Enqueue(CutsceneDefinition cutscene, float delay)
        {
            if (_phase != Phase.Idle && _current.Id == cutscene.Id)
                return false;
            foreach (Pending pending in _queue)
                if (pending.Cutscene.Id == cutscene.Id)
                    return false;

            _queue.Enqueue(new Pending(cutscene, Mathf.Max(0f, delay)));
            StartNext();
            return true;
        }

        void StartNext()
        {
            if (_phase != Phase.Idle || _queue.Count == 0)
                return;
            Pending next = _queue.Dequeue();
            _current = next.Cutscene;
            _timer = next.Delay;
            _phase = Phase.Waiting;
        }

        // A cutscene may start from Playing, or from Title for the intro. Not while paused,
        // in another cutscene or on the results screen.
        static bool CanStart(IGameClock clock) =>
            clock == null || clock.State == GameState.Playing || clock.State == GameState.Title;

        void Begin(IGameClock clock)
        {
            _phase = Phase.Playing;
            _signalsFired.Clear();
            _held = false;

            clock?.RequestState(GameState.Cutscene);
            CutsceneEvents.RaiseCutsceneStarted(_current.Id);

            _usesTimeline = _playback != null && _playback.Play(_current);
            _timer = _current.PlaceholderSeconds;
        }

        void Finish(IGameClock clock)
        {
            CutsceneDefinition done = _current;
            foreach (string signal in done.CriticalSignals)
                SignalReached(signal);

            _phase = Phase.Idle;
            _current = null;
            _usesTimeline = false;
            _held = false;

            CutsceneEvents.RaiseCutsceneEnded(done.Id);
            clock?.RequestState(done.StateAfter);
            StartNext();
        }

        CutsceneDefinition Find(Predicate<CutsceneDefinition> match)
        {
            for (int i = 0; i < _cutscenes.Count; i++)
                if (_cutscenes[i] != null && match(_cutscenes[i]))
                    return _cutscenes[i];
            return null;
        }
    }
}
