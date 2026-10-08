using Unity.Cinemachine;
using UnityEngine;

namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>
    /// Hands the gameplay camera to Cinemachine for a cutscene and gives it back afterwards.
    /// </summary>
    /// <remarks>
    /// The gameplay camera belongs to the player prefab and is driven by mouse look, so it has
    /// no Cinemachine brain of its own. When a cutscene starts the rig adds (or re-enables) a
    /// brain on it, plus a "gameplay" Cinemachine camera parked exactly at the player's view.
    /// That camera is live when the Timeline's first shot eases in and when its last shot
    /// eases out, so entering and leaving a cutscene is a blend from and back to the player's
    /// eyes, not a cut. When the cutscene ends the brain is switched off and the camera's local
    /// pose and lens are put back, so mouse look carries on exactly where it left off. Between
    /// cutscenes no brain or Cinemachine camera runs at all.
    /// </remarks>
    public sealed class CutsceneCameraRig
    {
        /// <summary>Above any shot camera's priority, so the gameplay view is live outside the Timeline's shots.</summary>
        public const int GameplayPriority = 100;

        readonly Transform _parent;
        readonly CinemachineBlendDefinition _defaultBlend;

        Camera _camera;
        CinemachineBrain _brain;
        bool _brainWasEnabled;
        CinemachineCamera _gameplay;
        Vector3 _localPosition;
        Quaternion _localRotation;
        float _fieldOfView, _near, _far;

        /// <param name="parent">Where the gameplay Cinemachine camera lives (the director, in the Agents scene).</param>
        /// <param name="defaultBlend">Blend between cameras that the Timeline does not time itself.</param>
        public CutsceneCameraRig(Transform parent, CinemachineBlendDefinition defaultBlend)
        {
            _parent = parent;
            _defaultBlend = defaultBlend;
        }

        /// <summary>True between <see cref="Begin"/> and <see cref="End"/>.</summary>
        public bool IsActive { get; private set; }

        /// <summary>The brain on the gameplay camera while a cutscene runs, for the Timeline's Cinemachine tracks.</summary>
        public CinemachineBrain Brain => IsActive ? _brain : null;

        /// <summary>The Cinemachine camera parked at the player's view.</summary>
        public CinemachineCamera GameplayCamera => _gameplay;

        /// <summary>Takes the main camera for a cutscene. False if there is no main camera.</summary>
        public bool Begin()
        {
            if (IsActive)
                return true;
            _camera = Camera.main;
            if (_camera == null)
                return false;

            Transform cameraTransform = _camera.transform;
            _localPosition = cameraTransform.localPosition;
            _localRotation = cameraTransform.localRotation;
            _fieldOfView = _camera.fieldOfView;
            _near = _camera.nearClipPlane;
            _far = _camera.farClipPlane;

            if (!_camera.TryGetComponent(out _brain))
            {
                _brain = _camera.gameObject.AddComponent<CinemachineBrain>();
                _brain.enabled = false;
            }
            _brainWasEnabled = _brain.enabled;
            _brain.DefaultBlend = _defaultBlend;

            if (_gameplay == null)
            {
                var go = new GameObject("Gameplay View (Cinemachine)");
                go.transform.SetParent(_parent, false);
                go.SetActive(false);
                _gameplay = go.AddComponent<CinemachineCamera>();
                _gameplay.Priority.Enabled = true;
                _gameplay.Priority.Value = GameplayPriority;
            }
            _gameplay.transform.SetPositionAndRotation(cameraTransform.position, cameraTransform.rotation);
            _gameplay.Lens = LensSettings.FromCamera(_camera);
            _gameplay.gameObject.SetActive(true);

            _brain.enabled = true;
            IsActive = true;
            return true;
        }

        /// <summary>Gives the camera back to the player: brain off, local pose and lens restored.</summary>
        public void End()
        {
            if (!IsActive)
                return;
            IsActive = false;

            if (_gameplay != null)
                _gameplay.gameObject.SetActive(false);
            if (_brain != null)
                _brain.enabled = _brainWasEnabled;
            if (_camera == null)
                return;

            _camera.transform.localPosition = _localPosition;
            _camera.transform.localRotation = _localRotation;
            _camera.fieldOfView = _fieldOfView;
            _camera.nearClipPlane = _near;
            _camera.farClipPlane = _far;
        }

        /// <summary>Ends any cutscene and removes the gameplay Cinemachine camera.</summary>
        public void Dispose()
        {
            End();
            if (_gameplay != null)
                Object.Destroy(_gameplay.gameObject);
            _gameplay = null;
        }
    }
}
