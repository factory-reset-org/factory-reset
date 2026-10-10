using UnityEngine;

namespace ToyFactory.Runtime.Effects
{
    /// <summary>
    /// A unit quad for glows and other flat effects, built in code so nothing depends on a
    /// built-in mesh being available in a player build. Its front faces -Z, like Unity's Quad.
    /// </summary>
    public static class GlowQuad
    {
        static Mesh s_mesh;

        public static Mesh Mesh
        {
            get
            {
                if (s_mesh == null)
                    s_mesh = Build();
                return s_mesh;
            }
        }

        static Mesh Build()
        {
            var mesh = new Mesh { name = "GlowQuad", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f),
            };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();
            return mesh;
        }

        // Domain reload is off, so the mesh from the last play session may have been destroyed.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => s_mesh = null;
    }
}
