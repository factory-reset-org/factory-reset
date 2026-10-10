using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using ToyFactory.Player;
using ToyFactory.Runtime.Effects;
using Object = UnityEngine.Object;

namespace ToyFactory.Interaction.EditorTools
{
    /// <summary>
    /// Builds what the prop effects need and wires them up: the comic word sprites, the belt
    /// chevrons, the field material, the explosion and word prefabs, and the
    /// <c>Resources/PropEffects</c> prefab the props load. It also gives the wind-up toy and
    /// the Player their new look and numbers. Run it from
    /// <b>Factory Reset / Props / Build Prop Effects</b>, after <b>Build Blaster Assets</b>.
    /// It can be run again at any time and rewrites the same assets in place.
    /// </summary>
    public static class PropEffectsBuilder
    {
        const string WordFolder = "Assets/_Project/Textures/FX/Words";
        const string TextureFolder = "Assets/_Project/Textures/FX";
        const string MaterialFolder = "Assets/_Project/Materials/FX";
        const string PrefabFolder = "Assets/_Project/Prefabs/FX";
        const string ResourcesFolder = "Assets/_Project/Resources";
        const string GlowMaterialPath = MaterialFolder + "/M_FX_Glow.mat";
        const string SparkPrefabPath = PrefabFolder + "/ImpactBurst.prefab";
        const string ExplosionPrefabPath = PrefabFolder + "/ExplosionBurst.prefab";
        const string WordPrefabPath = PrefabFolder + "/ComicWord.prefab";
        const string EffectsPrefabPath = ResourcesFolder + "/PropEffects.prefab";
        const string ToyPrefabPath = "Assets/_Project/Prefabs/Props/WindUpToy.prefab";
        const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player/Player.prefab";

        // 256 px wide words are 1.6 m wide at full size.
        const float WordPixelsPerUnit = 160f;

        [MenuItem("Factory Reset/Props/Build Prop Effects")]
        public static void Build()
        {
            var glow = AssetDatabase.LoadAssetAtPath<Material>(GlowMaterialPath);
            var spark = AssetDatabase.LoadAssetAtPath<GameObject>(SparkPrefabPath);
            if (glow == null || spark == null)
            {
                Debug.LogError("Run Factory Reset / Blaster / Build Blaster Assets first: the glow material and spark prefab are missing.");
                return;
            }

            EnsureFolder(MaterialFolder);
            EnsureFolder(PrefabFolder);
            EnsureFolder(ResourcesFolder);

            Sprite[] words = ImportWords();
            Material field = Additive(MaterialFolder + "/M_FX_Field.mat", glow.GetTexture("_BaseMap") as Texture2D, 0.6f);
            Material belt = BuildBeltMaterial();

            BuildExplosionPrefab(glow);
            BuildWordPrefab();
            BuildEffectsPrefab(words, glow, field, belt);
            AssetDatabase.SaveAssets();

            WireToy();
            WirePlayer();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Prop effects built (" + words.Length + " words). If the open scene shows as modified, do not save it.");
        }

        // ---- Words --------------------------------------------------------------------

        static Sprite[] ImportWords()
        {
            string[] guids = AssetDatabase.FindAssets("Word_ t:Texture2D", new[] { WordFolder });
            var sprites = new System.Collections.Generic.List<Sprite>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = WordPixelsPerUnit;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();

                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null)
                    sprites.Add(sprite);
            }
            return sprites.ToArray();
        }

        // ---- Materials ----------------------------------------------------------------

        static Material LoadOrCreate(string path, string shaderName)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
                throw new System.InvalidOperationException("Shader not found: " + shaderName);

            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }
            return material;
        }

        // Adds its light to what is behind it; "tint" greater than 1 would be picked up by bloom.
        static Material Additive(string path, Texture2D texture, float intensity)
        {
            Material material = LoadOrCreate(path, "Universal Render Pipeline/Particles/Unlit");
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", new Color(intensity, intensity, intensity, 1f));
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 2f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.One);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        // Pale chevrons pointing along u, on a clear ground, repeating, with soft alpha blending.
        static Material BuildBeltMaterial()
        {
            const int Size = 64;
            string path = TextureFolder + "/T_FX_Chevron.png";
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    // A ">" shape: the band follows |v - 0.5| leaning back from the tip at the right.
                    float u = (x + 0.5f) / Size;
                    float v = (y + 0.5f) / Size;
                    float centre = 0.75f - Mathf.Abs(v - 0.5f) * 1.2f;
                    float d = Mathf.Abs(u - centre);
                    float a = d < 0.09f ? 0.55f : 0f;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            Material material = LoadOrCreate(MaterialFolder + "/M_FX_Belt.mat", "Universal Render Pipeline/Particles/Unlit");
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            material.SetColor("_BaseColor", new Color(1f, 0.85f, 0.4f, 1f));
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        // ---- Prefabs ------------------------------------------------------------------

        // A bigger, longer spark burst, for a core going up.
        static void BuildExplosionPrefab(Material glow)
        {
            var root = new GameObject("ExplosionBurst");
            int layer = LayerMask.NameToLayer("Tracer");
            if (layer >= 0)
                root.layer = layer;

            var burst = root.AddComponent<ImpactBurst>();
            var burstSerialized = new SerializedObject(burst);
            burstSerialized.FindProperty("lifetime").floatValue = 1.1f;
            burstSerialized.FindProperty("lift").floatValue = 0f;
            burstSerialized.ApplyModifiedPropertiesWithoutUndo();

            var system = root.GetComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.duration = 0.5f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.gravityModifier = 1.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 48;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)36) });

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.1f;

            ParticleSystem.ColorOverLifetimeModule colour = system.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colour.color = new ParticleSystem.MinMaxGradient(fade);

            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = glow;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            PrefabUtility.SaveAsPrefabAsset(root, ExplosionPrefabPath);
            Object.DestroyImmediate(root);
        }

        static void BuildWordPrefab()
        {
            var root = new GameObject("ComicWord");
            var renderer = root.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = 50;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            root.AddComponent<ComicWord>();
            PrefabUtility.SaveAsPrefabAsset(root, WordPrefabPath);
            Object.DestroyImmediate(root);
        }

        static void BuildEffectsPrefab(Sprite[] words, Material glow, Material field, Material belt)
        {
            var root = new GameObject("PropEffects");
            var effects = root.AddComponent<PropEffects>();

            var serialized = new SerializedObject(effects);
            serialized.FindProperty("sparkPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(SparkPrefabPath).GetComponent<ImpactBurst>();
            serialized.FindProperty("explosionPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(ExplosionPrefabPath).GetComponent<ImpactBurst>();
            serialized.FindProperty("wordPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(WordPrefabPath).GetComponent<ComicWord>();
            serialized.FindProperty("glowMaterial").objectReferenceValue = glow;
            serialized.FindProperty("fieldMaterial").objectReferenceValue = field;
            serialized.FindProperty("beltMaterial").objectReferenceValue = belt;

            SerializedProperty list = serialized.FindProperty("words");
            list.arraySize = words.Length;
            for (int i = 0; i < words.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = words[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, EffectsPrefabPath);
            Object.DestroyImmediate(root);
        }

        // ---- Wiring -------------------------------------------------------------------

        // The toy's body is the parent of its "Body" child, so it can hop and fall over without
        // moving the physics body. Its key is already wired.
        static void WireToy()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(ToyPrefabPath);
            try
            {
                var toy = root.GetComponent<WindUpToy>();
                Transform body = root.transform.Find("Body");
                if (toy == null || body == null)
                {
                    Debug.LogWarning("WindUpToy prefab has no Body child; the toy will not hop or fall over.");
                    return;
                }

                var serialized = new SerializedObject(toy);
                serialized.FindProperty("model").objectReferenceValue = body;
                serialized.FindProperty("walkSpeed").floatValue = 0.9f;
                serialized.FindProperty("lingerSeconds").floatValue = 2.5f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, ToyPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void WirePlayer()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                var thrower = root.GetComponentInChildren<PlayerToyThrower>(true);
                if (thrower == null)
                    return;

                var serialized = new SerializedObject(thrower);
                serialized.FindProperty("throwSpeed").floatValue = 9f;
                serialized.FindProperty("throwLift").floatValue = 3.2f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
