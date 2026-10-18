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
        static MaterialPropertyBlock s_empty;

        public static void Set(Renderer renderer, Color colour)
        {
            if (renderer == null)
                return;

            s_block ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(s_block);
            s_block.SetColor(BaseColor, colour);
            renderer.SetPropertyBlock(s_block);
        }

        /// <summary>
        /// Moves each of the renderer's materials part of the way to <paramref name="colour"/>, so a
        /// many-coloured part keeps its pattern and reads as tinted, not repainted.
        /// </summary>
        public static void Blend(Renderer renderer, Color colour, float amount)
        {
            if (renderer == null)
                return;

            s_block ??= new MaterialPropertyBlock();
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Color own = materials[i] != null && materials[i].HasProperty(BaseColor) ? materials[i].GetColor(BaseColor) : Color.white;
                s_block.Clear();
                s_block.SetColor(BaseColor, Color.Lerp(own, colour, amount));
                renderer.SetPropertyBlock(s_block, i);
            }
        }

        /// <summary>Back to the material's own colour.</summary>
        public static void Clear(Renderer renderer)
        {
            if (renderer == null)
                return;

            renderer.SetPropertyBlock(null);
            s_empty ??= new MaterialPropertyBlock();
            int count = renderer.sharedMaterials.Length;
            for (int i = 0; i < count; i++)
                renderer.SetPropertyBlock(s_empty, i);
        }
    }
}
