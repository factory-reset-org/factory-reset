using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ToyFactory.Player;
using ToyFactory.Runtime.Effects;
using Object = UnityEngine.Object;

namespace ToyFactory.Player.EditorTools
{
    /// <summary>
    /// Builds everything the blaster's look needs, and hooks it up to the Player prefab: the
    /// two glow textures, the additive and gun materials, the bolt and spark prefabs, and the
    /// gun in view. Run it from <b>Factory Reset / Blaster / Build Blaster Assets</b>. It can
    /// be run again at any time: it rewrites the same assets in place, keeping their ids.
    /// </summary>
    /// <remarks>
    /// This folder has no assembly definition, so it can see the Player scripts, which live in
    /// the default assembly and which an assembly definition could not reference.
    /// </remarks>
    public static class BlasterAssetBuilder
    {
        const string TextureFolder = "Assets/_Project/Textures/FX";
        const string FxMaterialFolder = "Assets/_Project/Materials/FX";
        const string GunMaterialFolder = "Assets/_Project/Materials/Greybox";
        const string FxPrefabFolder = "Assets/_Project/Prefabs/FX";
        const string PlayerPrefabFolder = "Assets/_Project/Prefabs/Player";
        const string PlayerPrefabPath = PlayerPrefabFolder + "/Player.prefab";
        const string GunPrefabPath = PlayerPrefabFolder + "/BlasterViewModel.prefab";
        const string BoltPrefabPath = FxPrefabFolder + "/ShotBolt.prefab";
        const string BurstPrefabPath = FxPrefabFolder + "/ImpactBurst.prefab";
        const string ViewModelLayerName = "ViewModel";
        const string OverlayCameraName = "ViewModelCamera";

        // Where the gun sits in the view, in the camera's space: right, down and forward.
        static readonly Vector3 GunPosition = new Vector3(0.34f, -0.31f, 0.74f);

        [MenuItem("Factory Reset/Blaster/Build Blaster Assets")]
        public static void Build()
        {
            int viewLayer = EnsureLayer(ViewModelLayerName);

            EnsureFolder(TextureFolder);
            EnsureFolder(FxMaterialFolder);
            EnsureFolder(FxPrefabFolder);

            // Bright in the middle, 0.7 a quarter of the way out, nothing at the edge.
            Texture2D glowTexture = WriteTexture(TextureFolder + "/T_FX_Glow.png", (u, v) =>
            {
                float r = Mathf.Min(1f, Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f);
                return r < 0.25f ? Mathf.Lerp(1f, 0.7f, r / 0.25f) : Mathf.Lerp(0.7f, 0f, (r - 0.25f) / 0.75f);
            });
            Texture2D streakTexture = WriteTexture(TextureFolder + "/T_FX_Streak.png",
                (u, v) => Mathf.Pow(1f - Mathf.Abs(2f * v - 1f), 1.5f));

            Material streak = Additive(FxMaterialFolder + "/M_FX_Streak.mat", streakTexture, 2.5f);
            Material glow = Additive(FxMaterialFolder + "/M_FX_Glow.mat", glowTexture, 2f);

            var materials = new GunMaterials
            {
                Orange = Lit("M_Gun_Orange", "#ff9f1c", 0.6f, 0f),
                Teal = Lit("M_Gun_Teal", "#3ddbb0", 0.6f, 0f),
                Dark = Lit("M_Gun_Dark", "#2b2345", 0.6f, 0f),
                Steel = Lit("M_Gun_Steel", "#b8c4d6", 0.7f, 0.6f),
                Cell = Cell("M_Gun_Cell"),
            };

            BuildBoltPrefab(streak, glow);
            BuildBurstPrefab(glow);
            BuildGunPrefab(materials, glow, viewLayer);
            AssetDatabase.SaveAssets();

            WirePlayerPrefab(viewLayer);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Blaster assets built and the Player prefab wired. If the open scene shows as modified, do not save it.");
        }

        struct GunMaterials
        {
            public Material Orange, Teal, Dark, Steel, Cell;
        }

        // ---- Textures and materials ---------------------------------------------------

        static Texture2D WriteTexture(string path, System.Func<float, float, float> alpha)
        {
            const int Size = 64;
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha((x + 0.5f) / Size, (y + 0.5f) / Size))));
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

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

        // A glow that adds its light to what is behind it, brighter than white so bloom picks it up.
        static Material Additive(string path, Texture2D texture, float intensity)
        {
            Material material = LoadOrCreate(path, "Universal Render Pipeline/Particles/Unlit");
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", new Color(intensity, intensity, intensity, 1f));
            material.SetFloat("_Surface", 1f);   // transparent
            material.SetFloat("_Blend", 2f);     // additive
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

        static Material Lit(string name, string hex, float smoothness, float metallic)
        {
            Material material = LoadOrCreate(GunMaterialFolder + "/" + name + ".mat", "Universal Render Pipeline/Lit");
            ColorUtility.TryParseHtmlString(hex, out Color colour);
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            EditorUtility.SetDirty(material);
            return material;
        }

        // Emission is switched on so the gun can tint it per frame with a property block.
        static Material Cell(string name)
        {
            Material material = Lit(name, "#0a3a33", 0.6f, 0f);
            ColorUtility.TryParseHtmlString("#3ddbb0", out Color teal);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", teal * 2f);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(material);
            return material;
        }

        // ---- Prefabs ------------------------------------------------------------------

        static void BuildBoltPrefab(Material streak, Material glow)
        {
            var root = new GameObject("ShotBolt");
            SetLayer(root, "Tracer");
            var bolt = root.AddComponent<ShotBolt>();   // brings its LineRenderer
            var line = root.GetComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.textureMode = LineTextureMode.Stretch;
            line.alignment = LineAlignment.View;
            line.numCapVertices = 0;
            line.widthMultiplier = 0.09f;
            line.sharedMaterial = streak;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off;
            line.reflectionProbeUsage = ReflectionProbeUsage.Off;

            // The glow round the head: a quad the bolt turns to face the camera.
            GameObject glowQuad = Part(root.transform, "Glow", Resources.GetBuiltinResource<Mesh>("Quad.fbx"),
                Vector3.zero, Vector3.one * 0.9f, Vector3.zero, glow);
            SetLayer(glowQuad, "Tracer");

            var serialized = new SerializedObject(bolt);
            serialized.FindProperty("glow").objectReferenceValue = glowQuad.transform;
            serialized.FindProperty("glowRenderer").objectReferenceValue = glowQuad.GetComponent<Renderer>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Save(root, BoltPrefabPath);
        }

        static void BuildBurstPrefab(Material glow)
        {
            var root = new GameObject("ImpactBurst");
            SetLayer(root, "Tracer");
            root.AddComponent<ImpactBurst>();   // brings its ParticleSystem
            var system = root.GetComponent<ParticleSystem>();

            ParticleSystem.MainModule main = system.main;
            main.duration = 0.5f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.34f);
            main.gravityModifier = 1.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 24;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)6) });

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 65f;
            shape.radius = 0.02f;

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
            Save(root, BurstPrefabPath);
        }

        static void BuildGunPrefab(GunMaterials m, Material glow, int viewLayer)
        {
            var root = new GameObject("BlasterViewModel");
            var gun = root.AddComponent<BlasterViewModel>();
            Transform t = root.transform;

            Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            Mesh cylinder = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
            Mesh quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

            Part(t, "Body", cube, new Vector3(0f, 0f, 0f), new Vector3(0.14f, 0.15f, 0.46f), Vector3.zero, m.Orange);
            Part(t, "Grip", cube, new Vector3(0f, -0.14f, -0.10f), new Vector3(0.09f, 0.22f, 0.12f), new Vector3(-17f, 0f, 0f), m.Dark);
            Part(t, "Barrel", cylinder, new Vector3(0f, 0.01f, 0.36f), new Vector3(0.10f, 0.18f, 0.10f), new Vector3(90f, 0f, 0f), m.Steel);
            for (int i = 0; i < 3; i++)
                Part(t, "Ring_" + (i + 1), cylinder, new Vector3(0f, 0.01f, 0.26f + i * 0.08f), new Vector3(0.13f, 0.01f, 0.13f), new Vector3(90f, 0f, 0f), m.Teal);
            Part(t, "TopPlate", cube, new Vector3(0f, 0.09f, -0.05f), new Vector3(0.18f, 0.03f, 0.20f), Vector3.zero, m.Teal);
            Part(t, "Sight", cube, new Vector3(0f, 0.12f, -0.13f), new Vector3(0.03f, 0.10f, 0.12f), Vector3.zero, m.Dark);
            GameObject cell = Part(t, "Cell", cylinder, new Vector3(0.10f, 0.02f, -0.02f), new Vector3(0.10f, 0.12f, 0.10f), new Vector3(90f, 0f, 0f), m.Cell);
            GameObject flash = Part(t, "MuzzleFlash", quad, new Vector3(0f, 0.01f, 0.60f), Vector3.one * 0.4f, Vector3.zero, glow);
            flash.GetComponent<MeshRenderer>().enabled = false;

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(t, false);
            muzzle.localPosition = new Vector3(0f, 0.01f, 0.58f);

            var serialized = new SerializedObject(gun);
            serialized.FindProperty("muzzle").objectReferenceValue = muzzle;
            serialized.FindProperty("flash").objectReferenceValue = flash.GetComponent<Renderer>();
            serialized.FindProperty("cell").objectReferenceValue = cell.transform;
            serialized.FindProperty("cellRenderer").objectReferenceValue = cell.GetComponent<Renderer>();
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Only the overlay camera draws these.
            foreach (Transform each in root.GetComponentsInChildren<Transform>(true))
                each.gameObject.layer = viewLayer;
            Save(root, GunPrefabPath);
        }

        // A piece of the gun: a built-in mesh with no collider, so it never touches the physics.
        static GameObject Part(Transform parent, string name, Mesh mesh, Vector3 position, Vector3 scale, Vector3 euler, Material material)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localEulerAngles = euler;
            part.transform.localScale = scale;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return part;
        }

        // ---- The Player prefab --------------------------------------------------------

        static void WirePlayerPrefab(int viewLayer)
        {
            var gunPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GunPrefabPath);
            var boltPrefab = AssetDatabase.LoadAssetAtPath<ShotBolt>(BoltPrefabPath);
            var burstPrefab = AssetDatabase.LoadAssetAtPath<ImpactBurst>(BurstPrefabPath);

            GameObject player = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                Camera camera = player.GetComponentInChildren<Camera>(true);
                if (camera == null)
                    throw new System.InvalidOperationException("The Player prefab has no camera.");

                // Running this twice must not leave two guns or two overlay cameras.
                for (int i = camera.transform.childCount - 1; i >= 0; i--)
                {
                    Transform child = camera.transform.GetChild(i);
                    if (child.GetComponent<BlasterViewModel>() != null || child.name == OverlayCameraName)
                        Object.DestroyImmediate(child.gameObject);
                }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(gunPrefab, camera.transform);
                instance.transform.localPosition = GunPosition;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;

                var viewModel = instance.GetComponent<BlasterViewModel>();
                StackViewModelCamera(camera, viewLayer);

                var blaster = player.GetComponent<PlayerBlaster>();
                if (blaster == null)
                    throw new System.InvalidOperationException("The Player prefab has no PlayerBlaster.");
                var shooting = new SerializedObject(blaster);
                shooting.FindProperty("viewModel").objectReferenceValue = viewModel;
                shooting.FindProperty("boltPrefab").objectReferenceValue = boltPrefab;
                shooting.FindProperty("impactPrefab").objectReferenceValue = burstPrefab;
                shooting.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(player);
            }
        }

        // The gun is drawn by a second camera that renders only the ViewModel layer, on top of
        // the first, so it cannot be inside a wall. URP draws it as an overlay on the player's
        // camera, which no longer draws that layer.
        static void StackViewModelCamera(Camera main, int viewLayer)
        {
            main.cullingMask &= ~(1 << viewLayer);

            var cameraObject = new GameObject(OverlayCameraName);
            cameraObject.transform.SetParent(main.transform, false);
            var overlay = cameraObject.AddComponent<Camera>();
            overlay.cullingMask = 1 << viewLayer;
            overlay.clearFlags = CameraClearFlags.Depth;
            overlay.fieldOfView = main.fieldOfView;
            overlay.nearClipPlane = 0.02f;
            overlay.farClipPlane = 5f;
            overlay.allowHDR = main.allowHDR;
            overlay.allowMSAA = main.allowMSAA;

            UniversalAdditionalCameraData overlayData = overlay.GetUniversalAdditionalCameraData();
            overlayData.renderType = CameraRenderType.Overlay;
            overlayData.renderPostProcessing = true;

            UniversalAdditionalCameraData mainData = main.GetUniversalAdditionalCameraData();
            mainData.renderType = CameraRenderType.Base;
            mainData.cameraStack.RemoveAll(c => c == null);
            if (!mainData.cameraStack.Contains(overlay))
                mainData.cameraStack.Add(overlay);
            EditorUtility.SetDirty(mainData);
        }

        // The layer the gun is on: an existing one of that name, or the first free user layer.
        static int EnsureLayer(string name)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing >= 0)
                return existing;

            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue))
                    continue;
                slot.stringValue = name;
                tagManager.ApplyModifiedProperties();
                return i;
            }
            throw new System.InvalidOperationException("No free user layer for " + name + ".");
        }

        // ---- Helpers ------------------------------------------------------------------

        static void Save(GameObject root, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        static void SetLayer(GameObject go, string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0)
                go.layer = layer;
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
