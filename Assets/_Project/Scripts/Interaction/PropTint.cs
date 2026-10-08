using UnityEngine;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// Recolours one prop's renderer without creating a material for it, so props that
    /// share a material keep sharing it.
    /// </summary>
    static class PropTint
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static MaterialPropertyBlock s_block;

        public static void Set(Renderer renderer, Color colour)
        {
            if (renderer == null)
                return;

            s_block ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(s_block);
            s_block.SetColor(BaseColor, colour);
            renderer.SetPropertyBlock(s_block);
        }

        /// <summary>Back to the material's own colour.</summary>
        public static void Clear(Renderer renderer)
        {
            if (renderer != null)
                renderer.SetPropertyBlock(null);
        }
    }
}
