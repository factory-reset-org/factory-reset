using System;
using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Player
{
    /// <summary>
    /// The blaster's ammo. The battery in the blaster holds a set number of shots; when it
    /// runs down the player reloads, which swaps in one of the spare cells they carry and
    /// takes a moment. Overcharge makes shots free for a short time.
    /// </summary>
    /// <remarks>
    /// The agents read this through the player state: the Guard pushes forward when the
    /// battery is low or being reloaded and stays in cover during overcharge, and the
    /// Captain expects the player to go for a battery when it is low.
    /// </remarks>
    public sealed class PlayerBattery : MonoBehaviour
    {
        [Tooltip("Shots in one full battery.")]
        [SerializeField, Min(1)] int shotsPerCell = 20;

        [Tooltip("Spare cells carried at the start.")]
        [SerializeField, Min(0)] int startingSpareCells = 3;

        [Tooltip("Most spare cells that can be carried.")]
        [SerializeField, Min(0)] int maxSpareCells = 5;

        [SerializeField, Min(0.1f)] float reloadSeconds = 1.5f;

        [SerializeField, Min(0.1f)] float overchargeSeconds = 8f;

        int _shots;
        float _reloadDoneAt;
        float _overchargeUntil;

        /// <summary>Charge left in the battery in the blaster, 0 (empty) to 1 (full).</summary>
        public float Fraction => _shots / (float)shotsPerCell;

        public int SpareCells { get; private set; }

        public bool IsReloading { get; private set; }

        /// <summary>Seconds of overcharge left, 0 when it is not active.</summary>
        public float OverchargeTimeLeft => Mathf.Max(0f, _overchargeUntil - Now);

        public bool IsEmpty => _shots <= 0;

        /// <summary>Raised whenever the charge, the spare cells or the reload state changes.</summary>
        public event Action OnChanged;

        static float Now => GameClock.Current != null ? GameClock.Current.GameTime : Time.time;

        void Awake()
        {
            _shots = shotsPerCell;
            SpareCells = Mathf.Min(startingSpareCells, maxSpareCells);
        }

        /// <summary>Takes one shot's worth of charge. False if the blaster cannot fire right now.</summary>
        public bool TrySpendShot()
        {
            if (IsReloading)
                return false;
            if (OverchargeTimeLeft > 0f)
                return true;   // free while overcharged
            if (_shots <= 0)
                return false;

            _shots--;
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>Starts swapping in a spare cell. False if there is none, or no need.</summary>
        public bool StartReload()
        {
            if (IsReloading || SpareCells <= 0 || _shots >= shotsPerCell)
                return false;

            SpareCells--;
            IsReloading = true;
            _reloadDoneAt = Now + reloadSeconds;
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>A battery pickup: one more spare cell. False if no more can be carried.</summary>
        public bool AddSpareCell()
        {
            if (SpareCells >= maxSpareCells)
                return false;

            SpareCells++;
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>The charger: a full battery and every spare cell.</summary>
        public void RefillAll()
        {
            IsReloading = false;
            _shots = shotsPerCell;
            SpareCells = maxSpareCells;
            OnChanged?.Invoke();
        }

        /// <summary>An overcharge pickup: free shots for a while, starting now.</summary>
        public void StartOvercharge()
        {
            _overchargeUntil = Now + overchargeSeconds;
            OnChanged?.Invoke();
        }

        void Update()
        {
            if (!IsReloading || Now < _reloadDoneAt)
                return;

            IsReloading = false;
            _shots = shotsPerCell;
            OnChanged?.Invoke();
        }
    }
}
