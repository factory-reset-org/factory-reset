using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using ToyFactory.Interaction;
using ToyFactory.Interfaces;

namespace ToyFactory.Player
{
    /// <summary>
    /// Throws wind-up toys with Q or the right mouse button. The player carries a few, and
    /// one more is wound up after a fixed time while they carry fewer than the most. Thrown
    /// toys are kept and reused, so throwing never creates new objects once all have been out.
    /// </summary>
    public sealed class PlayerToyThrower : MonoBehaviour
    {
        [SerializeField] WindUpToy toyPrefab;

        [Tooltip("Where throws start and point: the camera pivot.")]
        [SerializeField] Transform aim;

        [SerializeField, Min(1)] int maxToys = 3;

        [Tooltip("Seconds to wind up one more toy while carrying fewer than the most.")]
        [SerializeField, Min(0.5f)] float rewindSeconds = 15f;

        [SerializeField, Min(0f)] float throwSpeed = 8f;
        [SerializeField, Min(0f)] float throwLift = 2.5f;

        [Tooltip("How far in front of the aim the toy appears, clear of the player's body.")]
        [SerializeField, Min(0.1f)] float spawnDistance = 0.7f;

        readonly List<WindUpToy> _pool = new List<WindUpToy>();
        Collider[] _playerColliders;
        float _nextRewindAt;

        public int ToysCarried { get; private set; }

        public int MaxToys => maxToys;

        /// <summary>Raised when a toy is thrown or wound up.</summary>
        public event Action OnToysChanged;

        static float Now => GameClock.Current != null ? GameClock.Current.GameTime : Time.time;

        void Awake()
        {
            ToysCarried = maxToys;
            _playerColliders = GetComponentsInChildren<Collider>();
        }

        void Update()
        {
            if (GameClock.Current != null && GameClock.Current.State != GameState.Playing)
                return;

            Rewind();

            // A free cursor means the click is for something else.
            if (Cursor.lockState != CursorLockMode.Locked)
                return;

            bool pressed = (Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame) ||
                           (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame);
            if (pressed)
                Throw();
        }

        void Rewind()
        {
            if (ToysCarried >= maxToys || Now < _nextRewindAt)
                return;

            ToysCarried++;
            if (ToysCarried < maxToys)
                _nextRewindAt = Now + rewindSeconds;
            OnToysChanged?.Invoke();
        }

        public void Throw()
        {
            if (ToysCarried <= 0 || toyPrefab == null)
                return;

            // The rewind clock starts when the first toy leaves a full hand.
            if (ToysCarried == maxToys)
                _nextRewindAt = Now + rewindSeconds;
            ToysCarried--;

            Vector3 position = aim.position + aim.forward * spawnDistance;
            Vector3 velocity = aim.forward * throwSpeed + Vector3.up * throwLift;
            TakeToy().Launch(position, velocity);
            OnToysChanged?.Invoke();
        }

        WindUpToy TakeToy()
        {
            foreach (WindUpToy pooled in _pool)
                if (pooled != null && !pooled.gameObject.activeSelf)
                    return pooled;

            WindUpToy toy = Instantiate(toyPrefab);
            toy.gameObject.SetActive(false);
            foreach (Collider toyCollider in toy.GetComponentsInChildren<Collider>())
                foreach (Collider playerCollider in _playerColliders)
                    Physics.IgnoreCollision(toyCollider, playerCollider);
            _pool.Add(toy);
            return toy;
        }
    }
}
