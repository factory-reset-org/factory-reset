using UnityEngine;
using ToyFactory.Interfaces;
using Random = UnityEngine.Random;

namespace ToyFactory.Player
{
    /// <summary>
    /// The blaster as the player sees it, held at the lower right of the view. It bobs as they
    /// walk, kicks back and tilts up when a shot is fired, flashes at the muzzle, and its battery
    /// cell shrinks as the charge runs down. The model is a prefab with no physics, so it can be
    /// swapped for a better one: only the parts named below need to be set again.
    /// </summary>
    /// <remarks>
    /// The gun is drawn by its own overlay camera, on the ViewModel layer, after the rest of the
    /// view, so it is always on top and can never be inside a wall however close the player
    /// stands. It is hidden outside play (cutscenes are filmed through the player's camera).
    /// </remarks>
    public sealed class BlasterViewModel : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        [Header("Parts")]
        [Tooltip("The tip of the barrel: where shots and the flash start.")]
        [SerializeField] Transform muzzle;
        [Tooltip("A glowing quad at the muzzle, shown for a moment after each shot.")]
        [SerializeField] Renderer flash;
        [Tooltip("The battery cell, scaled along its own Y axis by the charge.")]
        [SerializeField] Transform cell;
        [SerializeField] Renderer cellRenderer;

        [Header("Motion")]
        [SerializeField, Min(0f)] float kickBack = 0.07f;
        [Tooltip("Degrees the nose rises at full recoil.")]
        [SerializeField, Min(0f)] float kickPitch = 10f;
        [SerializeField, Min(0.1f)] float recoilDecay = 8f;
        [SerializeField, Min(0f)] float bobSideways = 0.012f;
        [SerializeField, Min(0f)] float bobUp = 0.014f;
        [Tooltip("Speed at which the bob reaches its full size, metres per second.")]
        [SerializeField, Min(0.1f)] float fullBobSpeed = 4f;

        [Header("Look")]
        [SerializeField, Min(0.01f)] float flashSeconds = 0.06f;
        [SerializeField, Min(0.05f)] float flashSize = 0.5f;
        [SerializeField] Color cellColour = new Color(0.24f, 0.86f, 0.69f);
        [SerializeField] Color overchargeCellColour = new Color(1f, 0.78f, 0.2f);

        PlayerBattery _battery;
        Renderer[] _renderers;
        bool _visible = true;
        MaterialPropertyBlock _block;
        Vector3 _restPosition;
        Quaternion _restRotation;
        Vector3 _cellScale = Vector3.one;
        float _recoil;
        float _flashLeft;
        float _bobPhase;
        float _bobWeight;
        float _shownCharge = -1f;
        bool _shownOvercharge;

        /// <summary>Where shots start: the barrel tip, or this object if none was set.</summary>
        public Transform Muzzle => muzzle != null ? muzzle : transform;

        void Awake()
        {
            _battery = GetComponentInParent<PlayerBattery>();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _block = new MaterialPropertyBlock();
            _restPosition = transform.localPosition;
            _restRotation = transform.localRotation;
            if (cell != null)
                _cellScale = cell.localScale;
            if (flash != null)
                flash.enabled = false;
        }

        /// <summary>A shot was fired: kick, and flash in its colour.</summary>
        public void OnShot(Color colour)
        {
            _recoil = 1f;
            _flashLeft = flashSeconds;
            if (flash == null)
                return;

            flash.GetPropertyBlock(_block);
            _block.SetColor(BaseColor, colour);
            flash.SetPropertyBlock(_block);
            flash.enabled = true;
        }

        // Cutscenes and the title and results screens are filmed through the player's own
        // camera, so a gun on it would be in every shot. It is only shown while the game is
        // being played or paused.
        static bool ShouldShow()
        {
            IGameClock clock = GameClock.Current;
            return clock == null || clock.State == GameState.Playing || clock.State == GameState.Paused;
        }

        void SetVisible(bool visible)
        {
            _visible = visible;
            foreach (Renderer part in _renderers)
                if (part != null)
                    part.forceRenderingOff = !visible;   // leaves the flash's own on/off alone
        }

        void LateUpdate()
        {
            bool show = ShouldShow();
            if (show != _visible)
                SetVisible(show);
            if (!show)
                return;

            float dt = Time.deltaTime;
            _recoil = Mathf.Max(0f, _recoil - dt * recoilDecay);

            // Walking bob, from how fast the player is moving along the ground.
            float speed = 0f;
            IPlayerState player = PlayerState.Current;
            if (player != null)
            {
                Vector3 velocity = player.Velocity;
                speed = Mathf.Sqrt(velocity.x * velocity.x + velocity.z * velocity.z);
            }
            _bobWeight = Mathf.MoveTowards(_bobWeight, Mathf.Clamp01(speed / fullBobSpeed), dt * 6f);
            _bobPhase += speed * dt * 1.1f;
            var bob = new Vector3(Mathf.Cos(_bobPhase) * bobSideways, Mathf.Abs(Mathf.Sin(_bobPhase)) * bobUp, 0f) * _bobWeight;

            transform.localPosition = _restPosition + bob + new Vector3(0f, 0f, -_recoil * kickBack);
            transform.localRotation = _restRotation * Quaternion.Euler(-_recoil * kickPitch, 0f, 0f);

            if (_flashLeft > 0f)
            {
                _flashLeft -= dt;
                if (flash != null)
                {
                    flash.enabled = _flashLeft > 0f;
                    flash.transform.localScale = Vector3.one * (flashSize * Random.Range(1f, 1.6f));
                }
            }

            ShowCharge();
        }

        // The cell is as long as the charge is full, and glows gold while overcharged.
        void ShowCharge()
        {
            if (cell == null || _battery == null)
                return;

            bool overcharged = _battery.OverchargeTimeLeft > 0f;
            float charge = overcharged ? 1f : _battery.IsReloading ? 0f : _battery.Fraction;
            if (Mathf.Approximately(charge, _shownCharge) && overcharged == _shownOvercharge)
                return;
            _shownCharge = charge;
            _shownOvercharge = overcharged;

            Vector3 scale = _cellScale;
            scale.y *= Mathf.Max(0.05f, charge);
            cell.localScale = scale;

            if (cellRenderer == null)
                return;
            cellRenderer.GetPropertyBlock(_block);
            _block.SetColor(EmissionColor, (overcharged ? overchargeCellColour : cellColour) * (0.3f + charge * 2.2f));
            cellRenderer.SetPropertyBlock(_block);
        }
    }
}
