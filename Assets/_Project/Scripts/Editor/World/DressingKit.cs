using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using ToyFactory.Runtime.World;

namespace ToyFactory.Editor.World
{
    /// <summary>
    /// Building blocks for the room dressing builders: rounded boxes, turned (lathe) shapes and
    /// flat quads saved as mesh assets and reused by size, the environment materials, and parts
    /// placed with the right static flags. Everything follows the smooth plastic style
    /// (<c>Docs/ArtBible.md</c>): rounded edges, smooth normals, palette colours only.
    /// </summary>
    public static class DressingKit
    {
        public const string MeshFolder = "Assets/_Project/Prefabs/Environment/Meshes";
        public const string MaterialFolder = "Assets/_Project/Materials/Environment";
        public const string TextureFolder = "Assets/_Project/Textures/Environment";

        // Palette (Docs/ArtBible.md).
        public static readonly Color Tomato = Hex("FF5A4E");
        public static readonly Color Sun = Hex("FFC933");
        public static readonly Color Mint = Hex("3DDBB0");
        public static readonly Color Cobalt = Hex("3A6CF4");
        public static readonly Color Bubble = Hex("FF5CA8");
        public static readonly Color Orange = Hex("FF9F1C");
        public static readonly Color Grape = Hex("8F6BFF");
        public static readonly Color Plum = Hex("1E1638");
        public static readonly Color Ink = Hex("FFF8EC");
        public static readonly Color White = Hex("E8ECF2");
        public static readonly Color Grey = Hex("5B6B8C");

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color colour);
            return colour;
        }

        static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        // ---- Meshes ------------------------------------------------------------------------

        /// <summary>A rounded box of <paramref name="size"/> with the level's rounding rule, saved once and reused.</summary>
        public static Mesh RoundBox(Vector3 size)
        {
            size = new Vector3(Round(size.x), Round(size.y), Round(size.z));
            float radius = RoundedBoxMesh.RecommendedRadius(size);
            string name = $"RoundBox_{F(size.x)}x{F(size.y)}x{F(size.z)}_r{F(radius)}";
            return LoadOrCreate(name, () => RoundedBoxMesh.Create(size, radius, RoundedBoxMesh.RecommendedSegments(size, radius)));
        }

        static float Round(float value) => Mathf.Round(value * 1000f) / 1000f;

        /// <summary>
        /// A shape turned round the Y axis from a profile of (radius, height) points, bottom to
        /// top, with smooth normals, saved once as <c>Lathe_&lt;name&gt;</c>. A radius of 0 closes the shape.
        /// </summary>
        public static Mesh Lathe(string name, Vector2[] profile, int segments)
        {
            return LoadOrCreate("Lathe_" + name, () =>
            {
                int rings = profile.Length;
                var vertices = new List<Vector3>();
                var normals = new List<Vector3>();
                var uvs = new List<Vector2>();
                var triangles = new List<int>();
                float length = 0f;
                var along = new float[rings];
                for (int i = 1; i < rings; i++)
                    along[i] = length += Vector2.Distance(profile[i], profile[i - 1]);

                for (int s = 0; s <= segments; s++)
                {
                    float angle = s * Mathf.PI * 2f / segments;
                    var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    for (int i = 0; i < rings; i++)
                    {
                        Vector2 p = profile[i];
                        Vector2 before = profile[Mathf.Max(0, i - 1)], after = profile[Mathf.Min(rings - 1, i + 1)];
                        Vector2 tangent = (after - before).normalized;
                        var normal2 = new Vector2(tangent.y, -tangent.x);   // outward for a bottom-to-top profile
                        vertices.Add(radial * p.x + Vector3.up * p.y);
                        normals.Add((radial * normal2.x + Vector3.up * normal2.y).normalized);
                        uvs.Add(new Vector2(s / (float)segments * Mathf.PI * 2f * Mathf.Max(0.05f, p.x), along[i]));
                    }
                }
                for (int s = 0; s < segments; s++)
                    for (int i = 0; i < rings - 1; i++)
                    {
                        int a = s * rings + i, b = a + 1, c = a + rings, d = c + 1;
                        triangles.Add(a); triangles.Add(b); triangles.Add(c);
                        triangles.Add(c); triangles.Add(b); triangles.Add(d);
                    }
                var mesh = new Mesh { name = "Lathe_" + name };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                return mesh;
            });
        }

        /// <summary>
        /// A capped cylinder with rounded rims, standing on y = 0 (pipes, posts, legs). Thin ones get
        /// fewer sides (8 under 5 cm, 12 under 15 cm, else 16): a rod or cord reads round with 8.
        /// </summary>
        public static Mesh Cylinder(float radius, float height, int sides = 0)
        {
            int segments = sides > 0 ? sides : radius < 0.05f ? 8 : radius < 0.15f ? 12 : 16;
            float edge = Mathf.Min(radius * 0.35f, height * 0.25f, 0.05f);
            var profile = new List<Vector2> { new Vector2(0f, 0f) };
            AddArc(profile, new Vector2(radius - edge, edge), edge, -90f, 0f);
            AddArc(profile, new Vector2(radius - edge, height - edge), edge, 0f, 90f);
            profile.Add(new Vector2(0f, height));
            return Lathe($"Cyl_{F(radius)}x{F(height)}" + (sides > 0 ? $"_s{sides}" : ""), profile.ToArray(), segments);
        }

        /// <summary>A flat disc ring on y = 0 with a rounded top (floor paint rings, pads).</summary>
        public static Mesh Ring(float inner, float outer, float thickness, int segments = 40)
        {
            var profile = new[]
            {
                new Vector2(inner, thickness), new Vector2(inner, 0f),
                new Vector2(outer, 0f), new Vector2(outer, thickness * 0.6f), new Vector2(outer - thickness, thickness),
                new Vector2(inner, thickness)
            };
            return Lathe($"Ring_{F(inner)}-{F(outer)}" + (segments != 40 ? $"_s{segments}" : ""), profile, segments);
        }

        static void AddArc(List<Vector2> profile, Vector2 centre, float radius, float fromDegrees, float toDegrees, int steps = 3)
        {
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Lerp(fromDegrees, toDegrees, i / (float)steps) * Mathf.Deg2Rad;
                profile.Add(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
            }
        }

        /// <summary>A flat quad facing local +Z (seen from +Z), centred, showing <paramref name="uv"/> of its texture.</summary>
        public static Mesh Quad(string name, Vector2 size, Rect uv)
        {
            return LoadOrCreate("Quad_" + name, () =>
            {
                Vector2 h = size * 0.5f;
                var mesh = new Mesh { name = "Quad_" + name };
                mesh.vertices = new[] { new Vector3(h.x, -h.y, 0f), new Vector3(-h.x, -h.y, 0f), new Vector3(-h.x, h.y, 0f), new Vector3(h.x, h.y, 0f) };
                mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
                mesh.uv = new[] { new Vector2(uv.xMin, uv.yMin), new Vector2(uv.xMax, uv.yMin), new Vector2(uv.xMax, uv.yMax), new Vector2(uv.xMin, uv.yMax) };
                // Clockwise as seen from +Z (Unity's front faces), so the quad is visible from +Z.
                mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                return mesh;
            }, forceRebuild: true);
        }

        /// <summary>
        /// A smooth sphere of radius 0.5 (scale it to size) with 12 segments and 7 rings, about
        /// 170 triangles: Unity's built-in sphere has 768, too many for the dozens of small balls,
        /// joints and toy parts in a room. Smooth normals keep it round at the sizes it is used.
        /// </summary>
        public static Mesh Sphere()
        {
            const int rings = 7;
            var profile = new Vector2[rings + 1];
            for (int i = 0; i <= rings; i++)
            {
                float a = Mathf.Lerp(-90f, 90f, i / (float)rings) * Mathf.Deg2Rad;
                profile[i] = new Vector2(Mathf.Cos(a) * 0.5f, Mathf.Sin(a) * 0.5f);
            }
            profile[0].x = profile[rings].x = 0f;
            return Lathe("Sphere12x7", profile, 12);
        }

        /// <summary>A mesh made by <paramref name="create"/>, saved as <c>&lt;name&gt;</c> and rebuilt on every call.</summary>
        public static Mesh Custom(string name, System.Func<Mesh> create) => LoadOrCreate(name, create, forceRebuild: true);

        static Mesh LoadOrCreate(string name, System.Func<Mesh> create, bool forceRebuild = false)
        {
            string path = $"{MeshFolder}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null && !forceRebuild)
                return existing;
            Mesh mesh = create();
            Unwrapping.GenerateSecondaryUVSet(mesh);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                existing.name = name;
                existing.vertices = existing.vertices;   // CopySerialized leaves the old geometry on the GPU: re-upload it
                Object.DestroyImmediate(mesh);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            mesh.name = name;
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>Merges several meshes, each with its own material, into one saved mesh with a submesh per material.</summary>
        public static Mesh Combine(string name, IReadOnlyList<(Mesh mesh, Matrix4x4 transform, int material)> parts, int materialCount)
        {
            return LoadOrCreate("Combined_" + name, () =>
            {
                var perMaterial = new List<CombineInstance>[materialCount];
                for (int i = 0; i < materialCount; i++)
                    perMaterial[i] = new List<CombineInstance>();
                foreach (var part in parts)
                    perMaterial[part.material].Add(new CombineInstance { mesh = part.mesh, transform = part.transform });
                var submeshes = new CombineInstance[materialCount];
                for (int i = 0; i < materialCount; i++)
                {
                    var merged = new Mesh();
                    merged.CombineMeshes(perMaterial[i].ToArray(), true, true);
                    submeshes[i] = new CombineInstance { mesh = merged, transform = Matrix4x4.identity };
                }
                var mesh = new Mesh { name = "Combined_" + name };
                mesh.CombineMeshes(submeshes, false, false);
                foreach (var s in submeshes)
                    Object.DestroyImmediate(s.mesh);
                mesh.RecalculateBounds();
                return mesh;
            }, forceRebuild: true);
        }

        // ---- Materials ---------------------------------------------------------------------

        /// <summary>An existing environment material, <c>M_Env_&lt;name&gt;</c>.</summary>
        public static Material Mat(string name)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/M_Env_{name}.mat");
            if (material == null)
                throw new System.InvalidOperationException($"Missing material M_Env_{name}.");
            return material;
        }

        /// <summary>
        /// Creates or updates <c>M_Env_&lt;name&gt;</c>: URP Lit plastic (metallic 0) in a palette colour,
        /// optionally textured and emissive. Emissive materials are baked (they light the room).
        /// </summary>
        public static Material Plastic(string name, Color colour, float smoothness, Texture2D texture = null,
            Color? emission = null, Texture2D emissionMap = null)
        {
            string path = $"{MaterialFolder}/M_Env_{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_Env_" + name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", smoothness);
            material.SetTexture("_BaseMap", texture);
            if (emission.HasValue)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission.Value);
                material.SetTexture("_EmissionMap", emissionMap);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        // ---- Placing parts -----------------------------------------------------------------

        /// <summary>An empty group under <paramref name="parent"/>.</summary>
        public static Transform Group(Transform parent, string name, Vector3 position = default)
        {
            var group = new GameObject(name);
            group.transform.SetParent(parent, false);
            group.transform.position = position;
            return group.transform;
        }

        /// <summary>
        /// A rendered part at a world position. Static parts are batched, occlude and bake into the
        /// lightmap (small ones at a lower lightmap scale); moving parts are left non-static and lit
        /// by probes. With <paramref name="solid"/> it gets a box collider fitted to its mesh.
        /// </summary>
        public static GameObject Part(Transform parent, string name, Mesh mesh, Material[] materials, Vector3 position,
            Quaternion rotation, bool solid = false, bool moving = false, Vector3? scale = null)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.transform.SetPositionAndRotation(position, rotation);
            if (scale.HasValue)
                part.transform.localScale = scale.Value;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            if (solid)
            {
                var box = part.AddComponent<BoxCollider>();
                box.center = mesh.bounds.center;
                box.size = mesh.bounds.size;
            }

            if (moving)
            {
                GameObjectUtility.SetStaticEditorFlags(part, 0);
                renderer.receiveGI = ReceiveGI.LightProbes;
            }
            else
            {
                GameObjectUtility.SetStaticEditorFlags(part, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic |
                    StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic |
                    (solid ? StaticEditorFlags.OccluderStatic : 0));
                Vector3 size = Vector3.Scale(mesh.bounds.size, part.transform.lossyScale);
                float largest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                var serialized = new SerializedObject(renderer);
                serialized.FindProperty("m_ScaleInLightmap").floatValue = largest < 0.6f ? 0.2f : largest < 2f ? 0.5f : 1f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            return part;
        }

        public static GameObject Part(Transform parent, string name, Mesh mesh, Material material, Vector3 position,
            Quaternion rotation, bool solid = false, bool moving = false, Vector3? scale = null) =>
            Part(parent, name, mesh, new[] { material }, position, rotation, solid, moving, scale);

        /// <summary>A rounded box part centred at <paramref name="centre"/>.</summary>
        public static GameObject Box(Transform parent, string name, Vector3 size, Material material, Vector3 centre,
            bool solid = false, Quaternion? rotation = null, bool moving = false) =>
            Part(parent, name, RoundBox(size), material, centre, rotation ?? Quaternion.identity, solid, moving);

        /// <summary>An invisible box collider (one solid volume for a whole prop).</summary>
        public static GameObject Blocker(Transform parent, string name, Vector3 centre, Vector3 size)
        {
            var blocker = new GameObject(name);
            blocker.transform.SetParent(parent, false);
            blocker.transform.position = centre;
            blocker.AddComponent<BoxCollider>().size = size;
            return blocker;
        }

        /// <summary>
        /// Merges the static parts under <paramref name="section"/> into one renderer per material,
        /// so a room of hundreds of small parts costs a few dozen draw calls instead of one each.
        /// Moving parts (not static), renderers a <see cref="DressingBlinker"/> switches, and
        /// anything outside the section are left alone; colliders stay where they were. The merged
        /// meshes are saved as <c>Merged_&lt;prefix&gt;_&lt;material&gt;</c> with fresh lightmap UVs.
        /// Returns how many renderers were merged away.
        /// </summary>
        public static int MergeStatic(Transform section, string prefix)
        {
            var blinking = new HashSet<Renderer>();
            foreach (DressingBlinker blinker in section.GetComponentsInChildren<DressingBlinker>(true))
            {
                SerializedProperty lights = new SerializedObject(blinker).FindProperty("lights");
                for (int i = 0; i < lights.arraySize; i++)
                    if (lights.GetArrayElementAtIndex(i).objectReferenceValue is Renderer r)
                        blinking.Add(r);
            }

            var groups = new Dictionary<Material, List<CombineInstance>>();
            var merged = new List<MeshRenderer>();
            foreach (MeshRenderer renderer in section.GetComponentsInChildren<MeshRenderer>(true))
            {
                var flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
                var filter = renderer.GetComponent<MeshFilter>();
                if ((flags & StaticEditorFlags.BatchingStatic) == 0 || blinking.Contains(renderer) || filter == null || filter.sharedMesh == null)
                    continue;
                Mesh mesh = filter.sharedMesh;
                Material[] materials = renderer.sharedMaterials;
                for (int s = 0; s < mesh.subMeshCount && s < materials.Length; s++)
                {
                    if (!groups.TryGetValue(materials[s], out List<CombineInstance> list))
                        groups[materials[s]] = list = new List<CombineInstance>();
                    list.Add(new CombineInstance { mesh = mesh, subMeshIndex = s, transform = section.worldToLocalMatrix * renderer.localToWorldMatrix });
                }
                merged.Add(renderer);
            }
            if (merged.Count < 2)
                return 0;

            foreach (KeyValuePair<Material, List<CombineInstance>> group in groups)
            {
                string name = $"Merged_{prefix}_{group.Key.name.Replace("M_Env_", "")}";
                List<CombineInstance> parts = group.Value;
                Mesh mesh = Custom(name, () =>
                {
                    var combined = new Mesh { name = name };
                    int vertices = 0;
                    foreach (CombineInstance part in parts)
                        vertices += part.mesh.vertexCount;
                    if (vertices > 65000)
                        combined.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    combined.CombineMeshes(parts.ToArray(), true, true);
                    combined.RecalculateBounds();
                    return combined;
                });
                var holder = new GameObject(name);
                holder.transform.SetParent(section, false);
                holder.AddComponent<MeshFilter>().sharedMesh = mesh;
                holder.AddComponent<MeshRenderer>().sharedMaterial = group.Key;
                GameObjectUtility.SetStaticEditorFlags(holder, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic |
                    StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
                var serialized = new SerializedObject(holder.GetComponent<MeshRenderer>());
                serialized.FindProperty("m_ScaleInLightmap").floatValue = 0.5f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            // The originals: drop their renderer and mesh, and the object itself if nothing else is left on it.
            foreach (MeshRenderer renderer in merged)
            {
                GameObject owner = renderer.gameObject;
                Object.DestroyImmediate(renderer);
                Object.DestroyImmediate(owner.GetComponent<MeshFilter>());
                if (owner.transform.childCount == 0 && owner.GetComponents<Component>().Length == 1)
                    Object.DestroyImmediate(owner);
            }
            return merged.Count;
        }

        /// <summary>Total triangles under <paramref name="root"/>, for the report.</summary>
        public static int Triangles(Transform root)
        {
            int total = 0;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null)
                    total += filter.sharedMesh.triangles.Length / 3;
            return total;
        }
    }
}
