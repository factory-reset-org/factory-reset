using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// The final materials of the S3 models: Art Bible plastics instead of the greybox greys, the Saboteur
    /// tint and the Guard's tread texture on the slots they belong to, and no faces left lying on top of
    /// each other where two colours meet (they would flicker once the zones had different colours).
    /// </summary>
    public class CharacterMaterialTests
    {
        const string MaterialFolder = "Assets/_Project/Materials/Characters/";
        const string TreadTexture = "Assets/_Project/Textures/Characters/T_Char_GuardTread.png";

        static readonly string[] ModelPrefabs =
        {
            "Assets/_Project/Prefabs/Characters/TrackerToy.prefab",
            "Assets/_Project/Prefabs/Characters/SaboteurBot.prefab",
            "Assets/_Project/Prefabs/Characters/GuardBot.prefab",
            "Assets/_Project/Prefabs/Characters/CaptainBot.prefab",
            "Assets/_Project/Prefabs/Characters/Unit047.prefab",
            "Assets/_Project/Prefabs/Props/Keycard_Root.prefab"
        };

        static GameObject Load(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(go, path);
            return go;
        }

        static IEnumerable<Renderer> Renderers(GameObject go) => go.GetComponentsInChildren<Renderer>(true);

        static Material[] AllCharacterMaterials() =>
            AssetDatabase.FindAssets("t:Material", new[] { MaterialFolder.TrimEnd('/') })
                .Select(guid => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid))).ToArray();

        // ---- Plastics

        [Test]
        public void NoModelRendererStillUsesAGreyboxMaterial()
        {
            foreach (string path in ModelPrefabs)
            {
                foreach (Renderer renderer in Renderers(Load(path)))
                {
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        Assert.IsNotNull(material, $"{path} {renderer.name} has an empty slot.");
                        StringAssert.DoesNotContain("Greybox", material.name, $"{path} {renderer.name}");
                        StringAssert.StartsWith(MaterialFolder, AssetDatabase.GetAssetPath(material), $"{path} {renderer.name} uses {material.name}");
                    }
                }
            }
        }

        [Test]
        public void EveryRendererHasOneMaterialPerSubmesh()
        {
            foreach (string path in ModelPrefabs)
            {
                foreach (MeshRenderer renderer in Load(path).GetComponentsInChildren<MeshRenderer>(true))
                {
                    Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    Assert.AreEqual(mesh.subMeshCount, renderer.sharedMaterials.Length, $"{path} {renderer.name}");
                }
            }
        }

        [Test]
        public void CharacterMaterialsArePlasticOnTheUniversalLitShader()
        {
            Material[] materials = AllCharacterMaterials();
            Assert.GreaterOrEqual(materials.Length, 15, "Every colour of the palette has a material.");
            foreach (Material material in materials)
            {
                Assert.AreEqual("Universal Render Pipeline/Lit", material.shader.name, material.name);
                Assert.AreEqual(0f, material.GetFloat("_Metallic"), material.name);
                Assert.That(material.GetFloat("_Smoothness"), Is.InRange(0.7f, 0.9f), material.name);
            }
        }

        [TestCase("M_Char_Red", "#FF5A4E")]
        [TestCase("M_Char_Gold", "#FFC933")]
        [TestCase("M_Char_Blue", "#3A6CF4")]
        [TestCase("M_Char_Steel", "#B8C4D6")]
        [TestCase("M_Char_Black", "#1D1A2A")]
        [TestCase("M_Char_SaboteurBody", "#8F6BFF")]
        public void PaletteColoursMatchTheReferenceSheets(string name, string hex)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + name + ".mat");
            Assert.IsNotNull(material, name);
            ColorUtility.TryParseHtmlString(hex, out Color expected);
            Color actual = material.GetColor("_BaseColor");
            Assert.AreEqual(expected.r, actual.r, 0.005f, name);
            Assert.AreEqual(expected.g, actual.g, 0.005f, name);
            Assert.AreEqual(expected.b, actual.b, 0.005f, name);
        }

        // ---- Saboteur tint

        static IEnumerable<string> TintTargets(GameObject prefab, out Renderer body)
        {
            body = null;
            var names = new List<string>();
            foreach (MonoBehaviour behaviour in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour.GetType().Name != "SaboteurTint")
                    continue;

                SerializedProperty targets = new SerializedObject(behaviour).FindProperty("targets");
                for (int i = 0; i < targets.arraySize; i++)
                {
                    SerializedProperty element = targets.GetArrayElementAtIndex(i);
                    var renderer = (Renderer)element.FindPropertyRelative("renderer").objectReferenceValue;
                    names.Add(renderer.name + ":" + element.FindPropertyRelative("materialIndex").intValue);
                    body = renderer;
                }
            }

            return names;
        }

        [TestCase("Assets/_Project/Prefabs/Characters/SaboteurBot.prefab")]
        [TestCase("Assets/_Project/Prefabs/Agents/Agent_SaboteurBot.prefab")]
        public void SaboteurTintLandsOnTheBodyColourSlotAndNothingElse(string path)
        {
            GameObject prefab = Load(path);
            string[] targets = TintTargets(prefab, out Renderer body).ToArray();

            CollectionAssert.AreEqual(new[] { "Body:0" }, targets, "The tint writes the body's first slot only.");
            Assert.AreEqual("M_Char_SaboteurBody", body.sharedMaterials[0].name);

            // No other slot uses the body colour, so the tint cannot reach a part that should keep its own colour.
            foreach (Renderer renderer in Renderers(prefab))
            {
                for (int i = 0; i < renderer.sharedMaterials.Length; i++)
                {
                    bool isBodySlot = renderer == body && i == 0;
                    if (!isBodySlot)
                        Assert.AreNotEqual("M_Char_SaboteurBody", renderer.sharedMaterials[i].name, $"{renderer.name} slot {i}");
                }
            }
        }

        // ---- Guard treads

        [Test]
        public void GuardTreadsHaveTheirOwnTexturedMaterial()
        {
            var tread = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "M_Char_GuardTread.mat");
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TreadTexture);
            Assert.IsNotNull(texture);
            Assert.AreSame(texture, tread.GetTexture("_BaseMap"));

            GameObject guard = Load("Assets/_Project/Prefabs/Characters/GuardBot.prefab");
            foreach (string name in new[] { "Tread_L", "Tread_R" })
            {
                Renderer renderer = Renderers(guard).First(r => r.name == name);
                Assert.AreSame(tread, renderer.sharedMaterials[0], name);
            }

            // Only the treads: S4 scrolls this material's texture, and nothing else should move with it.
            foreach (Renderer renderer in Renderers(guard))
            {
                for (int i = 0; i < renderer.sharedMaterials.Length; i++)
                {
                    if (renderer.name != "Tread_L" && renderer.name != "Tread_R")
                        Assert.AreNotSame(tread, renderer.sharedMaterials[i], $"{renderer.name} slot {i}");
                }
            }
        }

        [Test]
        public void TreadTextureTilesIsPowerOfTwoAndCompressedBc7()
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TreadTexture);
            Assert.IsTrue(Mathf.IsPowerOfTwo(texture.width) && Mathf.IsPowerOfTwo(texture.height), $"{texture.width} x {texture.height}");

            var importer = (TextureImporter)AssetImporter.GetAtPath(TreadTexture);
            Assert.AreEqual(TextureWrapMode.Repeat, importer.wrapMode, "A scrolled texture has to repeat.");
            TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings("Standalone");
            Assert.IsTrue(standalone.overridden);
            Assert.AreEqual(TextureImporterFormat.BC7, standalone.format);
            Assert.IsTrue(importer.mipmapEnabled);
        }

        [Test]
        public void TreadTextureIsSeamlessWhereItRepeats()
        {
            var path = TreadTexture;
            var source = new Texture2D(2, 2);
            source.LoadImage(System.IO.File.ReadAllBytes(path));
            try
            {
                // Neighbouring pixels across the repeat edge differ about as much as neighbours inside the texture.
                float edge = 0f, inside = 0f;
                int n = source.height;
                for (int y = 0; y < n; y++)
                {
                    edge += Mathf.Abs(source.GetPixel(0, y).grayscale - source.GetPixel(source.width - 1, y).grayscale);
                    inside += Mathf.Abs(source.GetPixel(source.width / 2, y).grayscale - source.GetPixel(source.width / 2 + 1, y).grayscale);
                }

                Assert.LessOrEqual(edge / n, inside / n + 0.03f, "The left and right edges meet without a step.");
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [TestCase("Tread_L")]
        [TestCase("Tread_R")]
        public void TreadTextureRunsAlongTheBeltSoScrollingItMovesTheTread(string name)
        {
            GameObject guard = Load("Assets/_Project/Prefabs/Characters/GuardBot.prefab");
            Mesh mesh = Renderers(guard).OfType<MeshRenderer>().First(r => r.name == name).GetComponent<MeshFilter>().sharedMesh;
            Vector3[] v = mesh.vertices;
            Vector2[] uv = mesh.uv;
            int[] idx = mesh.GetIndices(0);

            // How much of the belt body's area has its U axis along the belt (the body's local Z).
            float along = 0f, total = 0f;
            for (int t = 0; t < idx.Length; t += 3)
            {
                int a = idx[t], b = idx[t + 1], c = idx[t + 2];
                Vector3 e1 = v[b] - v[a], e2 = v[c] - v[a];
                Vector2 d1 = uv[b] - uv[a], d2 = uv[c] - uv[a];
                float det = d1.x * d2.y - d2.x * d1.y;
                float area = Vector3.Cross(e1, e2).magnitude * 0.5f;
                if (Mathf.Abs(det) < 1e-9f || area < 1e-7f)
                    continue;

                Vector3 perU = ((e1 * d2.y - e2 * d1.y) / det).normalized;
                along += area * Mathf.Abs(perU.z);
                total += area;
            }

            Assert.GreaterOrEqual(along / total, 0.7f, $"{name}: U runs along the belt on {along / total:P0} of the body.");
        }

        // ---- Unit 047's eyes

        [Test]
        public void UnitEyesGlowThroughEmissionAndNothingElseDoes()
        {
            var eyes = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "M_Char_Unit047Eyes.mat");
            Assert.IsTrue(eyes.IsKeywordEnabled("_EMISSION"), "The keyword survives builds.");
            Color emission = eyes.GetColor("_EmissionColor");
            Assert.Greater(emission.maxColorComponent, 1f, "An HDR value, so it blooms.");

            GameObject unit = Load("Assets/_Project/Prefabs/Characters/Unit047.prefab");
            foreach (Renderer renderer in Renderers(unit))
            {
                bool usesEyes = renderer.sharedMaterials.Any(m => m == eyes);
                Assert.AreEqual(renderer.name == "Eyes", usesEyes, renderer.name);
            }

            foreach (Material material in AllCharacterMaterials().Where(m => m != eyes))
                Assert.IsFalse(material.IsKeywordEnabled("_EMISSION"), material.name);
        }

        // ---- Budgets and shape are unchanged

        [TestCase("TrackerToy", 3000)]
        [TestCase("SaboteurBot", 4000)]
        [TestCase("GuardBot", 4000)]
        [TestCase("CaptainBot", 4500)]
        [TestCase("Unit047", 3500)]
        public void TriangleCountsStayUnderTheArtBibleBudgets(string model, int budget)
        {
            GameObject prefab = Load($"Assets/_Project/Prefabs/Characters/{model}.prefab");
            int triangles = prefab.GetComponentsInChildren<MeshFilter>(true).Sum(f => f.sharedMesh.triangles.Length / 3);

            Assert.Less(triangles, budget);
        }

        // ---- No coplanar overlay faces

        // Faces of different material slots that face the same way, lie in one plane and overlap would
        // flicker once the slots have different colours. The greybox greys hid them.
        static int CoplanarOverlaps(Mesh mesh)
        {
            Vector3[] verts = mesh.vertices;
            var tris = new List<(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 n)>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                int[] idx = mesh.GetIndices(s);
                for (int t = 0; t < idx.Length; t += 3)
                {
                    Vector3 a = verts[idx[t]], b = verts[idx[t + 1]], c = verts[idx[t + 2]];
                    Vector3 cross = Vector3.Cross(b - a, c - a);
                    if (cross.magnitude > 1e-9f)
                        tris.Add((s, a, b, c, cross.normalized));
                }
            }

            int hits = 0;
            for (int i = 0; i < tris.Count; i++)
            {
                for (int j = i + 1; j < tris.Count; j++)
                {
                    var A = tris[i];
                    var B = tris[j];
                    if (A.sub == B.sub || Vector3.Dot(A.n, B.n) < 0.9995f)
                        continue;
                    if (Mathf.Abs(Vector3.Dot(A.n, B.a - A.a)) > 0.0008f || Mathf.Abs(Vector3.Dot(A.n, B.b - A.a)) > 0.0008f
                        || Mathf.Abs(Vector3.Dot(A.n, B.c - A.a)) > 0.0008f)
                        continue;

                    var an = new Vector3(Mathf.Abs(A.n.x), Mathf.Abs(A.n.y), Mathf.Abs(A.n.z));
                    int drop = an.x > an.y && an.x > an.z ? 0 : an.y > an.z ? 1 : 2;
                    Vector2 P(Vector3 v) => drop == 0 ? new Vector2(v.y, v.z) : drop == 1 ? new Vector2(v.x, v.z) : new Vector2(v.x, v.y);
                    if (Inside(P((A.a + A.b + A.c) / 3f), P(B.a), P(B.b), P(B.c)) || Inside(P((B.a + B.b + B.c) / 3f), P(A.a), P(A.b), P(A.c)))
                        hits++;
                }
            }

            return hits;
        }

        static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Side(p, a, b), d2 = Side(p, b, c), d3 = Side(p, c, a);
            return !((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0));
        }

        static float Side(Vector2 p1, Vector2 p2, Vector2 p3) => (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);

        [Test]
        public void NoFacesLieOnTopOfEachOtherAcrossMaterialSlots()
        {
            // The Guard's antenna stem is three coincident rods of one colour: its slots share a material.
            var allowed = new HashSet<string> { "GuardBot/Antenna_Stem" };
            foreach (string path in ModelPrefabs)
            {
                string model = System.IO.Path.GetFileNameWithoutExtension(path);
                foreach (MeshRenderer renderer in Load(path).GetComponentsInChildren<MeshRenderer>(true))
                {
                    Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    if (mesh.subMeshCount < 2 || allowed.Contains(model + "/" + renderer.name))
                        continue;

                    Assert.AreEqual(0, CoplanarOverlaps(mesh), $"{model}/{renderer.name} has faces of two materials in one plane.");
                }
            }
        }

        [Test]
        public void GuardAntennaStemUsesOneColourBecauseItsRodsCoincide()
        {
            GameObject guard = Load("Assets/_Project/Prefabs/Characters/GuardBot.prefab");
            Renderer stem = Renderers(guard).First(r => r.name == "Antenna_Stem");

            Assert.AreEqual(1, stem.sharedMaterials.Select(m => m.name).Distinct().Count());
        }
    }
}
