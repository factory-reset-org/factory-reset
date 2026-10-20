using System;
using UnityEngine;

namespace ToyFactory.Runtime.Effects
{
    /// <summary>
    /// A comic word ("DING!", "KABOOM!") that pops up over a prop, drifts upward, faces the
    /// camera and fades. The sprite is one of the pre-drawn words in the prototype's starburst
    /// style, already in the colour of the moment.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class ComicWord : MonoBehaviour
    {
        [Tooltip("Width of the word at rest, in metres.")]
        [SerializeField, Min(0.1f)] float width = 1.3f;

        [Tooltip("Seconds from appearing to gone.")]
        [SerializeField, Min(0.2f)] float seconds = 1.05f;

        [Tooltip("How far it drifts up over its life, in metres.")]
        [SerializeField, Min(0f)] float rise = 0.6f;

        SpriteRenderer _renderer;
        Vector3 _start;
        float _scale;
        float _time;

        /// <summary>Raised once, when the word has faded out.</summary>
        public event Action<ComicWord> Finished;

        public bool IsShowing { get; private set; }

        public Sprite Sprite => _renderer != null ? _renderer.sprite : null;

        void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _renderer.sortingOrder = 60;
        }

        /// <summary>Shows <paramref name="sprite"/> over <paramref name="position"/>.</summary>
        /// <param name="sizeScale">1 is the normal width.</param>
        public void Show(Sprite sprite, Vector3 position, float sizeScale = 1f)
        {
            _renderer.sprite = sprite;
            float natural = sprite != null ? sprite.bounds.size.x : 1f;
            _scale = natural > 0.0001f ? width * sizeScale / natural : 1f;
            _start = position;
            _time = 0f;
            transform.position = position;
            _renderer.color = Color.white;
            IsShowing = true;
            Apply();
        }

        void Update()
        {
            if (!IsShowing)
                return;

            _time += Time.deltaTime;
            if (_time >= seconds)
            {
                IsShowing = false;
                Finished?.Invoke(this);
                return;
            }
            Apply();
        }

        void Apply()
        {
            // Grows from 60% to 110% in the first 0.12 s with a small overshoot, then holds.
            float grow = _time < 0.12f ? _time / 0.12f : 1f;
            float scale = (0.6f + 0.5f * grow) * (1f + Mathf.Max(0f, 0.15f - _time));
            transform.localScale = Vector3.one * (_scale * scale);
            transform.position = _start + Vector3.up * (rise * _time / seconds);

            float fade = _time > seconds * 0.67f ? Mathf.Clamp01(1f - (_time - seconds * 0.67f) / (seconds * 0.33f)) : 1f;
            _renderer.color = new Color(1f, 1f, 1f, fade);

            Camera camera = Camera.main;
            if (camera != null)
            {
                Vector3 away = transform.position - camera.transform.position;
                if (away.sqrMagnitude > 1e-6f)
                    transform.rotation = Quaternion.LookRotation(away, camera.transform.up);
            }
        }
    }
}
