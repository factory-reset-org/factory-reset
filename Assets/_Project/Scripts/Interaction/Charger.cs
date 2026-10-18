using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.Player;
using ToyFactory.Runtime.Effects;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// The charging station. Using it fills the blaster's battery and every spare cell.
    /// It stays where it is and can be used as often as the player likes.
    /// </summary>
    public sealed class Charger : MonoBehaviour, IInteractable, IInteractionPrompt
    {
        static readonly Color ChargeColour = new Color(0.24f, 0.86f, 0.69f);

        // The glow sits on the charger's orb at the top of the coil, so it reads as part of the station.
        void Start() => PropGlow.Attach(gameObject, ChargeColour, 1.3f, 0.6f, 0.25f, 5f, new Vector3(0f, OrbHeight(), 0f));

        // Height of the orb above this object's position: just under the top of the drawn model.
        float OrbHeight()
        {
            bool any = false;
            float top = 0f;
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled)
                    continue;
                top = any ? Mathf.Max(top, r.bounds.max.y) : r.bounds.max.y;
                any = true;
            }
            // The glow's offset is in this object's space, which is scaled (the station's box), so it is
            // divided back out to land at the orb's true height.
            float height = any ? top - 0.12f - transform.position.y : 0.5f;
            return height / Mathf.Max(0.01f, transform.lossyScale.y);
        }

        /// <summary>The prompt line while the player looks at this.</summary>
        public string PromptText => "charge the blaster";

        public void Interact()
        {
            if (!(PlayerState.Current is Component player) || !player.TryGetComponent(out PlayerBattery battery))
                return;

            battery.RefillAll();
            PropEffects.Spark(transform.position + Vector3.up * 1.2f, ChargeColour);
            GameSfx.Play(Sfx.Pickup);
        }
    }
}
