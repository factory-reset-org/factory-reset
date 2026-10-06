using System;
using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Runtime.Visuals
{
    /// <summary>
    /// Tints one Saboteur with its squad colour (A purple, B teal, C orange, D pink) so the four
    /// instances share one mesh and one material. It reads the squad slot once, in <c>Start</c>,
    /// from the <see cref="IAgentState"/> above it, and writes the colour into a
    /// <see cref="MaterialPropertyBlock"/>; nothing runs afterwards and no material is copied.
    /// </summary>
    /// <remarks>
    /// The spawner sets an agent's identity right after <c>Instantiate</c>, so it is not known in
    /// <c>Awake</c> or <c>OnEnable</c> but is ready by <c>Start</c>. The component guards itself, so it
    /// is harmless on any body: it does nothing unless the agent above it is a Saboteur with a squad slot.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class SaboteurTint : MonoBehaviour
    {
        /// <summary>One material slot of one renderer to tint.</summary>
        [Serializable]
        public struct Target
        {
            /// <summary>The renderer that owns the slot.</summary>
            public Renderer renderer;

            /// <summary>The material slot whose base colour is the squad colour.</summary>
            public int materialIndex;
        }

        // Art Bible, "Saboteur squad tints": A, B, C, D.
        static readonly Color32[] SquadColours =
        {
            new Color32(0x8F, 0x6B, 0xFF, 0xFF),
            new Color32(0x2F, 0xB8, 0x94, 0xFF),
            new Color32(0xFF, 0x8A, 0x1C, 0xFF),
            new Color32(0xE0, 0x40, 0x9A, 0xFF)
        };

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("Slots that carry the body colour. Leave empty to tint every renderer below this object, which is what a single-material placeholder body needs.")]
        [SerializeField] Target[] targets = Array.Empty<Target>();

        [Tooltip("Tint with Fallback Squad Index when no agent state is above this object, for example in a presentation scene. Off by default, so the component is inert on any other body.")]
        [SerializeField] bool useFallbackSquadIndex;

        [Range(0, 3)]
        [SerializeField] int fallbackSquadIndex;

        /// <summary>True once a squad colour has been applied.</summary>
        public bool IsTinted { get; private set; }

        /// <summary>The colour that was applied; meaningful only when <see cref="IsTinted"/>.</summary>
        public Color AppliedColour { get; private set; }

        /// <summary>The squad colour for a squad slot 0-3 (A-D), or false for any other slot.</summary>
        public static bool TryGetColour(int squadIndex, out Color colour)
        {
            if (squadIndex < 0 || squadIndex >= SquadColours.Length)
            {
                colour = default;
                return false;
            }

            colour = SquadColours[squadIndex];
            return true;
        }

        void Start()
        {
            int squadIndex;
            IAgentState state = GetComponentInParent<IAgentState>();
            if (state != null)
            {
                // The placeholder body is shared by every agent type, so ignore anything that is not a squad Saboteur.
                if (state.Type != AgentType.Saboteur || !state.Identity.IsInSquad)
                    return;

                squadIndex = state.Identity.SquadIndex;
            }
            else if (useFallbackSquadIndex)
            {
                squadIndex = fallbackSquadIndex;
            }
            else
            {
                return;
            }

            if (!TryGetColour(squadIndex, out Color colour))
                return;

            Apply(colour);
            IsTinted = true;
            AppliedColour = colour;
        }

        void Apply(Color colour)
        {
            var block = new MaterialPropertyBlock();
            if (targets != null && targets.Length > 0)
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    Target target = targets[i];
                    if (target.renderer == null)
                        continue;

                    // Keep whatever else is already on the slot and change only the base colour.
                    target.renderer.GetPropertyBlock(block, target.materialIndex);
                    block.SetColor(BaseColorId, colour);
                    target.renderer.SetPropertyBlock(block, target.materialIndex);
                }

                return;
            }

            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                renderer.GetPropertyBlock(block);
                block.SetColor(BaseColorId, colour);
                renderer.SetPropertyBlock(block);
            }
        }
    }
}
