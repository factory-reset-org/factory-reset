using System.Collections.Generic;
using UnityEngine;
using ToyFactory.Runtime.Pooling;

namespace ToyFactory.Runtime.Effects
{
    /// <summary>
    /// The shared look of the props: spark bursts, explosions, comic words, the glow material,
    /// and the field and belt materials. One instance holds the prefabs and pools, so a prop
    /// asks for "a spark here" or "DING! here" and never makes an object while the game runs.
    /// </summary>
    /// <remarks>
    /// The instance is made on first use from <c>Resources/PropEffects</c>, so no scene has to
    /// carry it. Everything is optional: with no effects asset the calls do nothing, and the
    /// props still work.
    /// </remarks>
    public sealed class PropEffects : MonoBehaviour
    {
        const string ResourceName = "PropEffects";
        const string WordPrefix = "Word_";

        [Header("Bursts")]
        [Tooltip("Six small sparks. Used for hits and pickups.")]
        [SerializeField] ImpactBurst sparkPrefab;
        [Tooltip("A big burst. Used for explosions.")]
        [SerializeField] ImpactBurst explosionPrefab;

        [Header("Words")]
        [SerializeField] ComicWord wordPrefab;
        [Tooltip("The comic word sprites, named Word_<key>.")]
        [SerializeField] Sprite[] words = new Sprite[0];

        [Header("Materials")]
        [Tooltip("Additive glow, for PropGlow.")]
        [SerializeField] Material glowMaterial;
        [Tooltip("The see-through energy field of a switch cage.")]
        [SerializeField] Material fieldMaterial;
        [Tooltip("A conveyor belt's surface, with its direction chevrons.")]
        [SerializeField] Material beltMaterial;

        ObjectPool<ImpactBurst> _sparks;
        ObjectPool<ImpactBurst> _explosions;
        ObjectPool<ComicWord> _words;
        Dictionary<string, Sprite> _wordByKey;

        /// <summary>The live instance, or null if none has been made.</summary>
        public static PropEffects Current { get; private set; }

        public static Material GlowMaterial => Ensure() != null ? Current.glowMaterial : null;

        public static Material FieldMaterial => Ensure() != null ? Current.fieldMaterial : null;

        public static Material BeltMaterial => Ensure() != null ? Current.beltMaterial : null;

        /// <summary>The instance, made from the effects asset if it does not exist yet. Null if there is no asset.</summary>
        public static PropEffects Ensure()
        {
            if (Current != null)
                return Current;

            var asset = Resources.Load<PropEffects>(ResourceName);
            if (asset == null)
                return null;

            Instantiate(asset).name = "PropEffects";   // its Awake makes it Current
            return Current;
        }

        // Domain reload is off, so the instance from the last play session must be forgotten.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => Current = null;

        void Awake()
        {
            if (Current != null && Current != this)
            {
                Destroy(gameObject);
                return;
            }
            Current = this;
        }

        void OnDestroy()
        {
            if (Current == this)
                Current = null;
        }

        /// <summary>A small burst of sparks, thrown up from <paramref name="position"/>.</summary>
        public static void Spark(Vector3 position, Color colour)
        {
            PropEffects effects = Ensure();
            if (effects != null)
                effects.PlaySpark(position, Vector3.up, colour);
        }

        /// <summary>A big burst, for something blowing up.</summary>
        public static void Explosion(Vector3 position, Color colour)
        {
            PropEffects effects = Ensure();
            if (effects != null)
                effects.PlayExplosion(position, colour);
        }

        /// <summary>A comic word over <paramref name="position"/>. The key is the end of the sprite's name, e.g. "ding".</summary>
        public static void Word(string key, Vector3 position, float sizeScale = 1f)
        {
            PropEffects effects = Ensure();
            if (effects != null)
                effects.PlayWord(key, position, sizeScale);
        }

        /// <summary>True if there is a word sprite for <paramref name="key"/>.</summary>
        public bool HasWord(string key)
        {
            BuildWordIndex();
            return key != null && _wordByKey.ContainsKey(key);
        }

        public void PlaySpark(Vector3 position, Vector3 normal, Color colour)
        {
            if (sparkPrefab == null)
                return;
            if (_sparks == null)
            {
                _sparks = new ObjectPool<ImpactBurst>(sparkPrefab, transform, burst => burst.Finished += ReleaseSpark);
                _sparks.Prewarm(12);
            }
            _sparks.Get().Play(position, normal, colour);
        }

        public void PlayExplosion(Vector3 position, Color colour)
        {
            if (explosionPrefab == null)
                return;
            if (_explosions == null)
            {
                _explosions = new ObjectPool<ImpactBurst>(explosionPrefab, transform,
                    burst => burst.Finished += ReleaseExplosion);
                _explosions.Prewarm(3);
            }
            _explosions.Get().Play(position, Vector3.up, colour);
        }

        public void PlayWord(string key, Vector3 position, float sizeScale = 1f)
        {
            if (wordPrefab == null)
                return;
            BuildWordIndex();
            if (key == null || !_wordByKey.TryGetValue(key, out Sprite sprite))
                return;

            if (_words == null)
            {
                _words = new ObjectPool<ComicWord>(wordPrefab, transform, word => word.Finished += ReleaseWord);
                _words.Prewarm(6);
            }
            _words.Get().Show(sprite, position, sizeScale);
        }

        void BuildWordIndex()
        {
            if (_wordByKey != null)
                return;

            _wordByKey = new Dictionary<string, Sprite>();
            foreach (Sprite sprite in words)
            {
                if (sprite == null)
                    continue;
                string key = sprite.name.StartsWith(WordPrefix) ? sprite.name.Substring(WordPrefix.Length) : sprite.name;
                _wordByKey[key] = sprite;
            }
        }

        void ReleaseSpark(ImpactBurst burst) => _sparks.Release(burst);

        void ReleaseExplosion(ImpactBurst burst) => _explosions.Release(burst);

        void ReleaseWord(ComicWord word) => _words.Release(word);
    }
}
