using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ToyFactory.Editor.Materials
{
    /// <summary>
    /// Gives every S3 model its final materials: Art Bible plastics (Metallic 0, Smoothness 0.7 to 0.9) in
    /// the colours of each character's reference sheet, replacing the three grey "Greybox" materials the
    /// models were exported with. Re-runnable: it updates the materials and the prefab variants in place,
    /// so GUIDs and every reference to them survive.
    /// </summary>
    /// <remarks>
    /// <para><b>How colours reach the meshes.</b> Each model has three colour zones, set as material slots
    /// in its FBX: <c>Greybox_Light</c> (the main body colour), <c>Greybox_Mid</c> (secondary panels and
    /// trim) and <c>Greybox_Dark</c> (joints, tyres, frames). The FBX files are not touched. The builder
    /// reads each part's original slots from the FBX, so it gives the same result however often it runs,
    /// and sets the materials on the character prefab variants, where a few parts differ from their
    /// zone's colour (the Tracker's gold key, the Guard's cyan visor, the Saboteur's steel arms).</para>
    /// <para><b>Saboteur.</b> The body's first slot is <see cref="Key.SaboteurBody"/>, which
    /// <c>SaboteurTint</c> overrides per squad letter with a MaterialPropertyBlock; every other part keeps
    /// one colour for all four.</para>
    /// <para><b>Guard treads.</b> Their dark zone has its own material with a tileable tread texture, so
    /// S4 can scroll <c>_BaseMap</c> with the body's speed.</para>
    /// </remarks>
    public static class CharacterMaterialBuilder
    {
        /// <summary>Where the character materials live.</summary>
        public const string MaterialFolder = "Assets/_Project/Materials/Characters";

        /// <summary>Where the character textures live.</summary>
        public const string TextureFolder = "Assets/_Project/Textures/Characters";

        /// <summary>The Guard's tileable tread texture.</summary>
        public const string TreadTexturePath = TextureFolder + "/T_Char_GuardTread.png";

        /// <summary>Size of the tread texture in pixels (power of two).</summary>
        public const int TreadTextureSize = 128;

        /// <summary>The character materials.</summary>
        public enum Key
        {
            Red, Cream, PinkSoft, Pink, Gold, Black, Blue, Navy, White, Grey, Steel,
            Cyan, VisorCyan, VisorBlue, SaboteurBody, GuardTread, Unit047Eyes
        }

        readonly struct Spec
        {
            public readonly string Name;
            public readonly Color Colour;
            public readonly float Smoothness;

            public Spec(string name, string hex, float smoothness)
            {
                Name = name;
                ColorUtility.TryParseHtmlString(hex, out Colour);
                Smoothness = smoothness;
            }
        }

        // Art Bible: plastic Metallic 0, Smoothness 0.7 to 0.85; colours from the reference sheets.
        static readonly Dictionary<Key, Spec> Specs = new Dictionary<Key, Spec>
        {
            { Key.Red, new Spec("M_Char_Red", "#FF5A4E", 0.8f) },
            { Key.Cream, new Spec("M_Char_Cream", "#FFF1D6", 0.8f) },
            { Key.PinkSoft, new Spec("M_Char_PinkSoft", "#FF9FC6", 0.8f) },
            { Key.Pink, new Spec("M_Char_Pink", "#FF5CA8", 0.8f) },
            { Key.Gold, new Spec("M_Char_Gold", "#FFC933", 0.8f) },
            { Key.Black, new Spec("M_Char_Black", "#1D1A2A", 0.75f) },
            { Key.Blue, new Spec("M_Char_Blue", "#3A6CF4", 0.8f) },
            { Key.Navy, new Spec("M_Char_Navy", "#22306A", 0.8f) },
            { Key.White, new Spec("M_Char_White", "#F4F1FF", 0.75f) },
            { Key.Grey, new Spec("M_Char_Grey", "#5B6B8C", 0.7f) },
            { Key.Steel, new Spec("M_Char_Steel", "#B8C4D6", 0.8f) },
            { Key.Cyan, new Spec("M_Char_Cyan", "#62D8FF", 0.8f) },
            { Key.VisorCyan, new Spec("M_Char_VisorCyan", "#62D8FF", 0.9f) },
            { Key.VisorBlue, new Spec("M_Char_VisorBlue", "#4466FF", 0.9f) },
            { Key.SaboteurBody, new Spec("M_Char_SaboteurBody", "#8F6BFF", 0.8f) },
            { Key.GuardTread, new Spec("M_Char_GuardTread", "#FFFFFF", 0.7f) },
            { Key.Unit047Eyes, new Spec("M_Char_Unit047Eyes", "#BFF3FF", 0.9f) }
        };

        /// <summary>One model: its prefab, its FBX and what each of its three zones becomes.</summary>
        readonly struct Model
        {
            public readonly string Name;
            public readonly string PrefabPath;
            public readonly string FbxPath;
            public readonly Key Light, Mid, Dark;

            public Model(string name, string prefabPath, string fbxPath, Key light, Key mid, Key dark)
            {
                Name = name;
                PrefabPath = prefabPath;
                FbxPath = fbxPath;
                Light = light;
                Mid = mid;
                Dark = dark;
            }
        }

        static readonly Model[] Models =
        {
            new Model("TrackerToy", "Assets/_Project/Prefabs/Characters/TrackerToy.prefab", "Assets/_Project/Models/TrackerToy/TrackerToy.fbx", Key.Red, Key.Cream, Key.Black),
            new Model("SaboteurBot", "Assets/_Project/Prefabs/Characters/SaboteurBot.prefab", "Assets/_Project/Models/SaboteurBot/SaboteurBot.fbx", Key.SaboteurBody, Key.Pink, Key.Black),
            new Model("GuardBot", "Assets/_Project/Prefabs/Characters/GuardBot.prefab", "Assets/_Project/Models/GuardBot/GuardBot.fbx", Key.Blue, Key.Gold, Key.Black),
            new Model("CaptainBot", "Assets/_Project/Prefabs/Characters/CaptainBot.prefab", "Assets/_Project/Models/CaptainBot/CaptainBot.fbx", Key.White, Key.Gold, Key.Black),
            new Model("Unit047", "Assets/_Project/Prefabs/Characters/Unit047.prefab", "Assets/_Project/Models/Unit047/Unit047.fbx", Key.Cyan, Key.White, Key.Navy),
            new Model("Keycard", "Assets/_Project/Prefabs/Props/Keycard_Root.prefab", "Assets/_Project/Models/Props/Keycard.fbx", Key.Gold, Key.Steel, Key.Black)
        };

        // Parts whose colour differs from their zone's. The keys are in the part's own slot order (the
        // order of its original Light / Mid / Dark materials), which the checks in the tests pin down.
        static readonly Dictionary<string, Key[]> Overrides = BuildOverrides();

        static Dictionary<string, Key[]> BuildOverrides()
        {
            var o = new Dictionary<string, Key[]>();
            void Add(string model, string part, params Key[] keys) => o[model + "/" + part] = keys;

            // Tracker: gold wind-up key, black antenna stem, pink inside the ears, cream snout with a black nose.
            Add("TrackerToy", "Key_Bar", Key.Gold);
            Add("TrackerToy", "Key_Shaft", Key.Gold);
            Add("TrackerToy", "Antenna_Stem", Key.Black);
            Add("TrackerToy", "Antenna_Bulb", Key.Cream);
            Add("TrackerToy", "Ear_L", Key.Red, Key.PinkSoft);
            Add("TrackerToy", "Ear_R", Key.Red, Key.PinkSoft);
            Add("TrackerToy", "Muzzle", Key.Cream, Key.Black);

            // Saboteur: steel arms and joints, a black goggle strap, pale lenses (S4's glow sits over them).
            Add("SaboteurBot", "Body", Key.SaboteurBody, Key.Black);
            Add("SaboteurBot", "Goggle_Rim_L", Key.Black, Key.White);
            Add("SaboteurBot", "Goggle_Rim_R", Key.Black, Key.White);
            Add("SaboteurBot", "Neck_Post", Key.Steel);
            Add("SaboteurBot", "ShoulderBall_L", Key.Steel);
            Add("SaboteurBot", "ShoulderBall_R", Key.Steel);
            Add("SaboteurBot", "ElbowBall_L", Key.Steel);
            Add("SaboteurBot", "ElbowBall_R", Key.Steel);
            Add("SaboteurBot", "UpperArm_L", Key.Steel);
            Add("SaboteurBot", "UpperArm_R", Key.Steel);
            Add("SaboteurBot", "Forearm_L", Key.Steel, Key.Black);
            Add("SaboteurBot", "Forearm_R", Key.Steel, Key.Black);
            Add("SaboteurBot", "Battery", Key.Steel, Key.Gold);

            // Guard: navy hip, steel balls and cannon with a red ring, cyan visor, yellow-tipped antenna,
            // and treads with their own textured material.
            Add("GuardBot", "Hip", Key.Navy, Key.Black);
            Add("GuardBot", "ShoulderBall_L", Key.Steel);
            Add("GuardBot", "ShoulderBall_R", Key.Steel);
            Add("GuardBot", "Cannon_Arm", Key.Steel, Key.Black, Key.Red);
            Add("GuardBot", "Cannon_Barrel", Key.Steel, Key.Black, Key.Red);
            Add("GuardBot", "Shield_Arm", Key.Steel, Key.Black, Key.Steel);
            Add("GuardBot", "Antenna_Bulb", Key.Gold, Key.Black);
            Add("GuardBot", "Antenna_Stem", Key.Black, Key.Black, Key.Black);   // three coincident rods in the model: one colour
            Add("GuardBot", "Visor", Key.Black, Key.VisorCyan);
            Add("GuardBot", "Head", Key.Blue, Key.Grey, Key.Black);
            Add("GuardBot", "Neck", Key.Black, Key.Steel);
            Add("GuardBot", "Tread_L", Key.GuardTread, Key.Grey, Key.Gold);
            Add("GuardBot", "Tread_R", Key.GuardTread, Key.Grey, Key.Gold);

            // Captain: navy legs and hat band, black boots and brim, gold shoulders, steel cannons with a pink ring, blue visor.
            Add("CaptainBot", "Boot_L", Key.Black, Key.Black, Key.Steel);
            Add("CaptainBot", "Boot_R", Key.Black, Key.Black, Key.Steel);
            Add("CaptainBot", "UpperLeg_L", Key.Navy, Key.Navy);
            Add("CaptainBot", "UpperLeg_R", Key.Navy, Key.Navy);
            Add("CaptainBot", "LowerLeg_L", Key.Navy, Key.Navy);
            Add("CaptainBot", "LowerLeg_R", Key.Navy, Key.Navy);
            Add("CaptainBot", "Arm_L", Key.White, Key.Black, Key.Steel);
            Add("CaptainBot", "Arm_R", Key.White, Key.Black, Key.Steel);
            Add("CaptainBot", "Cannon_L", Key.Steel, Key.Steel, Key.Pink);
            Add("CaptainBot", "Cannon_R", Key.Steel, Key.Steel, Key.Pink);
            Add("CaptainBot", "ShoulderBall_L", Key.Gold);
            Add("CaptainBot", "ShoulderBall_R", Key.Gold);
            Add("CaptainBot", "Epaulette_L", Key.Gold, Key.Gold);
            Add("CaptainBot", "Epaulette_R", Key.Gold, Key.Gold);
            Add("CaptainBot", "Hat_Band", Key.Navy, Key.Gold);
            Add("CaptainBot", "Hat_Brim", Key.Black);
            Add("CaptainBot", "Hat_Crown", Key.White, Key.White);
            Add("CaptainBot", "Head", Key.White, Key.VisorBlue, Key.Black);
            Add("CaptainBot", "Neck", Key.Black, Key.Steel);
            Add("CaptainBot", "Visor", Key.Black, Key.VisorBlue);

            // Unit 047: cyan body (its speaker colour), gold key, red DEFECTIVE sticker, glowing eyes.
            Add("Unit047", "Key_Bar", Key.Gold);
            Add("Unit047", "Key_Shaft", Key.Gold);
            Add("Unit047", "Sticker", Key.Red);
            Add("Unit047", "Chest_Tag", Key.White);
            Add("Unit047", "Antenna_Bulb", Key.Gold);
            Add("Unit047", "Antenna_Stem", Key.Black);
            Add("Unit047", "Visor", Key.Black);
            Add("Unit047", "Eyes", Key.Unit047Eyes);
            Add("Unit047", "Leg_L", Key.Grey);
            Add("Unit047", "Leg_R", Key.Grey);
            Add("Unit047", "Foot_L", Key.Black);
            Add("Unit047", "Foot_R", Key.Black);
            Add("Unit047", "ShoulderBall_L", Key.Steel);
            Add("Unit047", "ShoulderBall_R", Key.Steel);
            Add("Unit047", "Neck", Key.Steel);

            return o;
        }

        /// <summary>The material asset path of a key.</summary>
        public static string PathOf(Key key) => MaterialFolder + "/" + Specs[key].Name + ".mat";

        /// <summary>The model names the builder covers.</summary>
        public static IEnumerable<string> ModelNames()
        {
            foreach (Model model in Models)
                yield return model.Name;
        }

        [MenuItem("Factory Reset/Materials/Build Character Materials")]
        public static void Build()
        {
            EnsureFolder("Assets/_Project/Materials", "Characters");
            EnsureFolder("Assets/_Project/Textures", "Characters");

            Texture2D tread = EnsureTreadTexture();
            var materials = new Dictionary<Key, Material>();
            foreach (KeyValuePair<Key, Spec> entry in Specs)
                materials[entry.Key] = EnsureMaterial(entry.Key, entry.Value, tread);
            AssetDatabase.SaveAssets();

            foreach (Model model in Models)
                ApplyToPrefab(model, materials);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Character materials built.");
        }

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name))
                AssetDatabase.CreateFolder(parent, name);
        }

        // ---- Materials

        static Material EnsureMaterial(Key key, Spec spec, Texture2D tread)
        {
            string path = PathOf(key);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = spec.Name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = Shader.Find("Universal Render Pipeline/Lit");
            material.SetColor("_BaseColor", spec.Colour);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", spec.Smoothness);
            material.SetTexture("_BaseMap", key == Key.GuardTread ? tread : null);

            if (key == Key.Unit047Eyes)
            {
                // Emission on, so the keyword survives builds; S4 animates _EmissionColor for on and off.
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", new Color(0.9f, 1.84f, 2f, 1f));   // cyan at HDR intensity 2
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        // ---- The tread texture

        // A tileable tread: ribs across the belt (along U) with a bolt row between them. Every term is
        // periodic over the texture, so it repeats without a seam in both directions.
        static Texture2D EnsureTreadTexture()
        {
            int size = TreadTextureSize;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var dark = new Color(0.165f, 0.145f, 0.25f);
            var light = new Color(0.36f, 0.32f, 0.5f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Eight ribs across the texture: a soft ridge with a flat dark gap.
                    float phase = (x / (float)size) * 8f;
                    float ridge = Mathf.Clamp01(0.5f + 0.5f * Mathf.Cos(phase * 2f * Mathf.PI));
                    ridge = Mathf.SmoothStep(0.15f, 0.85f, ridge);

                    // Four small bolts per rib along V, centred on each ridge.
                    float bx = Mathf.Abs(Mathf.Repeat(phase, 1f) - 0.5f) * 2f;   // 0 on the ridge centre
                    float by = Mathf.Abs(Mathf.Repeat((y / (float)size) * 4f, 1f) - 0.5f) * 2f;
                    float bolt = Mathf.Clamp01(1f - Mathf.Sqrt(bx * bx * 0.6f + by * by) * 3.2f);

                    Color c = Color.Lerp(dark, light, ridge * 0.8f + bolt * 0.45f);
                    c.a = 1f;
                    texture.SetPixel(x, y, c);
                }
            }

            texture.Apply();
            File.WriteAllBytes(TreadTexturePath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(TreadTexturePath, ImportAssetOptions.ForceUpdate);

            // The same settings as S1's tiling textures: BC7 on Standalone, repeat, trilinear, 8x anisotropic.
            var importer = (TextureImporter)AssetImporter.GetAtPath(TreadTexturePath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaIsTransparency = false;
            importer.maxTextureSize = 256;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Standalone",
                overridden = true,
                maxTextureSize = 256,
                format = TextureImporterFormat.BC7,
                textureCompression = TextureImporterCompression.Compressed,
                compressionQuality = 50
            });
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(TreadTexturePath);
        }

        // ---- Prefabs

        static void ApplyToPrefab(Model model, Dictionary<Key, Material> materials)
        {
            // The original slots, read from the FBX, are what decides a zone, so the result does not
            // depend on what the prefab holds now.
            var original = new Dictionary<string, string[]>();
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(model.FbxPath);
            if (fbx == null)
                throw new InvalidOperationException("Missing model " + model.FbxPath);
            foreach (Renderer renderer in fbx.GetComponentsInChildren<Renderer>(true))
            {
                var names = new string[renderer.sharedMaterials.Length];
                for (int i = 0; i < names.Length; i++)
                    names[i] = renderer.sharedMaterials[i] != null ? renderer.sharedMaterials[i].name : string.Empty;
                original[renderer.name] = names;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(model.PrefabPath);
            try
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!original.TryGetValue(renderer.name, out string[] zones))
                        continue;

                    renderer.sharedMaterials = MaterialsFor(model, renderer.name, zones, materials);
                }

                PrefabUtility.SaveAsPrefabAsset(root, model.PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static Material[] MaterialsFor(Model model, string part, string[] zones, Dictionary<Key, Material> materials)
        {
            var result = new Material[zones.Length];
            if (Overrides.TryGetValue(model.Name + "/" + part, out Key[] keys))
            {
                if (keys.Length != zones.Length)
                    throw new InvalidOperationException($"{model.Name}/{part} has {zones.Length} slots but its rule gives {keys.Length}.");
                for (int i = 0; i < keys.Length; i++)
                    result[i] = materials[keys[i]];
                return result;
            }

            for (int i = 0; i < zones.Length; i++)
                result[i] = materials[ZoneKey(model, zones[i])];
            return result;
        }

        static Key ZoneKey(Model model, string zone)
        {
            switch (zone)
            {
                case "Greybox_Light": return model.Light;
                case "Greybox_Mid": return model.Mid;
                case "Greybox_Dark": return model.Dark;
                case "Greybox_Eyes": return Key.Unit047Eyes;
                default: throw new InvalidOperationException("Unknown zone material " + zone);
            }
        }

        /// <summary>The rule for a part, or null when it simply follows its zones. For tests.</summary>
        public static Key[] OverrideFor(string model, string part) =>
            Overrides.TryGetValue(model + "/" + part, out Key[] keys) ? keys : null;
    }
}
