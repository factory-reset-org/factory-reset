using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Effects;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// The energy field round a control switch. While it stands it is a faint cyan box that
    /// breathes, and its collider keeps the player's interact ray off the switch. When the
    /// switch is unsealed the collider goes at once and the field drops to the floor.
    /// </summary>
    public sealed class SwitchCage : MonoBehaviour
    {
        static readonly Color FieldColour = new Color(0.38f, 0.85f, 1f);

        [SerializeField, Range(1, ChapterEvents.SwitchCount)] int switchNumber = 1;

        [Tooltip("Seconds for the field to drop. The prototype's takes two thirds of a second.")]
        [SerializeField, Min(0.05f)] float dropTime = 0.66f;

        Collider _collider;
        Renderer _renderer;
        bool _hasFieldLook;
        Vector3 _fullScale;
        float _bottom;
        float _progress;
        bool _dropping;

        void Awake()
        {
            _collider = GetComponent<Collider>();
            _renderer = GetComponent<Renderer>();
            _fullScale = transform.localScale;
            _bottom = transform.position.y - _fullScale.y * 0.5f;

            // See-through light instead of a solid box, when the effects asset is there.
            Material field = PropEffects.FieldMaterial;
            if (_renderer != null && field != null)
            {
                _renderer.sharedMaterial = field;
                _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _hasFieldLook = true;
            }
        }

        void OnEnable() => ChapterEvents.OnSwitchUnsealed += HandleUnsealed;

        void OnDisable() => ChapterEvents.OnSwitchUnsealed -= HandleUnsealed;

        void HandleUnsealed(int number)
        {
            if (number != switchNumber || _dropping)
                return;

            _dropping = true;
            if (_collider != null)
                _collider.enabled = false;
        }

        void Update()
        {
            if (!_dropping)
            {
                if (_hasFieldLook)
                {
                    float strength = 0.16f + 0.06f * Mathf.Sin(Time.time * 3f);
                    Color shown = FieldColour * (strength * 3.5f);
                    shown.a = 1f;
                    PropTint.Set(_renderer, shown);
                }
                return;
            }

            // Squashes down to the floor rather than shrinking away round its middle.
            _progress += Time.deltaTime / dropTime;
            float height = Mathf.Max(0.001f, 1f - _progress);
            transform.localScale = new Vector3(_fullScale.x, _fullScale.y * height, _fullScale.z);
            Vector3 position = transform.position;
            position.y = _bottom + _fullScale.y * height * 0.5f;
            transform.position = position;

            if (_progress >= 1f)
                gameObject.SetActive(false);
        }
    }
}
