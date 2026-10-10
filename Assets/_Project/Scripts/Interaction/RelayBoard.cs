using System;
using System.Collections.Generic;
using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Runtime.Effects;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// The relay task: the relays must be used in the order the board shows, which the
    /// chapter manager picks at random each run. The board shows the order as the relays'
    /// colours, and counts as read once the player has come close to it. A wrong relay
    /// switches them all off again and sets off the alarm.
    /// </summary>
    public sealed class RelayBoard : TaskProp
    {
        [Tooltip("Must equal the task id in the chapter data.")]
        [SerializeField] string taskId = "ch3.relays";

        [Tooltip("The chapter this task belongs to. Before it, the relays do nothing.")]
        [SerializeField, Range(1, ChapterEvents.ChapterCount)] int chapter = 3;

        [SerializeField] Relay[] relays = new Relay[0];

        [Tooltip("One per step, first step first. Each takes the colour of the relay to use at that step.")]
        [SerializeField] Renderer[] orderSlots = new Renderer[0];

        [Header("Reading")]
        [Tooltip("The player has read the board once they are this close to it, measured on the ground (metres).")]
        [SerializeField, Min(0.5f)] float readRange = 8f;
        [SerializeField, Min(0.05f)] float checkInterval = 0.25f;

        IReadOnlyList<int> _order;
        int _step;
        float _nextCheckTime;

        // How long every relay flickers red after a wrong press.
        const float WrongOrderFlicker = 0.8f;

        public override string Id => taskId;

        /// <summary>True once the player has been close enough to read the order.</summary>
        public bool IsRead { get; private set; }

        /// <summary>Raised when a relay is used out of order and they all reset.</summary>
        public event Action OnWrongOrder;

        void Awake()
        {
            foreach (Relay relay in relays)
                if (relay != null)
                    relay.Bind(this);
        }

        void OnEnable() => ChapterEvents.OnSequenceChosen += HandleSequenceChosen;

        void OnDisable() => ChapterEvents.OnSequenceChosen -= HandleSequenceChosen;

        void HandleSequenceChosen(string sequenceTaskId, IReadOnlyList<int> order)
        {
            if (sequenceTaskId != taskId)
                return;

            _order = order;
            ShowOrder();
        }

        // Only watches for the player reading the board, then switches itself off.
        void Update()
        {
            if (Time.time < _nextCheckTime)
                return;
            _nextCheckTime = Time.time + checkInterval;

            IPlayerState player = PlayerState.Current;
            if (player == null)
                return;

            Vector3 toPlayer = player.Position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > readRange * readRange)
                return;

            IsRead = true;
            enabled = false;
        }

        internal void Press(Relay relay)
        {
            if (IsCompleted || relay.IsOn || !IsRead || !ChapterIsActive(chapter))
                return;

            IReadOnlyList<int> order = Order();
            if (_step < order.Count && relay.Number == order[_step])
            {
                relay.SetOn(true);
                _step++;
                GameSfx.Play(Sfx.Switch);
                PropEffects.Word(relay.ColourWord, relay.transform.position + Vector3.up * 2.2f);
                if (_step >= order.Count)
                {
                    Complete();
                    PropEffects.Word("sequenceok", transform.position + Vector3.up * 1.5f);
                }
                else
                {
                    ReportProgress(_step / (float)order.Count);
                }
                return;
            }

            _step = 0;
            foreach (Relay other in relays)
            {
                if (other == null)
                    continue;
                other.SetOn(false);
                other.Alarm(WrongOrderFlicker);
            }
            EmitNoise(NoiseLoudness.RelayAlarm);
            GameSfx.Play(Sfx.Alarm);
            PropEffects.Word("wrong", relay.transform.position + Vector3.up * 2.2f);
            ReportProgress(0f);
            OnWrongOrder?.Invoke();
        }

        // The order normally arrives through OnSequenceChosen. If the board missed it, the
        // journey is asked; with no journey (a test scene) the relays go in number order.
        IReadOnlyList<int> Order()
        {
            if (_order != null)
                return _order;

            ChapterManager manager = ChapterManager.Current;
            if (manager != null && manager.Flow != null &&
                manager.Flow.TryGetSequenceOrder(taskId, out IReadOnlyList<int> chosen))
            {
                _order = chosen;
            }
            else
            {
                var inNumberOrder = new int[relays.Length];
                for (int i = 0; i < inNumberOrder.Length; i++)
                    inNumberOrder[i] = i + 1;
                _order = inNumberOrder;
            }

            ShowOrder();
            return _order;
        }

        void ShowOrder()
        {
            for (int step = 0; step < orderSlots.Length && step < _order.Count; step++)
            {
                foreach (Relay relay in relays)
                {
                    if (relay == null || relay.Number != _order[step])
                        continue;
                    PropTint.Set(orderSlots[step], relay.Colour);
                    break;
                }
            }
        }
    }
}
