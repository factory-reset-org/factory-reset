using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using static ToyFactory.Editor.World.DressingKit;
using static ToyFactory.Editor.World.RoomPieces;

namespace ToyFactory.Editor.World
{
    /// <summary>
    /// What the windows look out on, in <c>Env.unity</c>: a toy-coloured procedural sky with the
    /// sun in it, the Sun light aimed from the south-west so it shines in through the windows, and
    /// a simple backdrop round the factory (grass, a ring road, sheds with coloured roofs, striped
    /// chimneys, lollipop trees, distant hills and puffy clouds). Re-running replaces it.
    /// </summary>
    /// <remarks>
    /// The backdrop is unlit, casts no shadows and is not part of the light bake, so it costs a
    /// few draw calls and nothing else; it is only ever seen through a window. The Sun is a
    /// baked light, like the ceiling panels: its patches through the windows are in the
    /// lightmaps and cost nothing at runtime (moving agents cast no sun shadow).
    /// </remarks>
    public static class OutsideDressing
    {
        // The factory's footprint: x and z 0 to 41.5.
        const float Centre = 20.75f;

        [MenuItem("Tools/Factory Reset/Build Outside")]
        static void RunFromMenu() => Debug.Log(Build());

        /// <summary>Builds (or rebuilds) the outside in the open Env scene and returns a report.</summary>
        public static string Build()
        {
            GameObject level = GameObject.Find("Level");
            if (level == null)
                throw new System.InvalidOperationException("Open Env.unity first: no Level object.");
            GameObject old = GameObject.Find("Level/Dressing/Outside");
            if (old != null)
                Object.DestroyImmediate(old);
            Load();
            Sky();
            Transform root = Group(level.transform.Find("Dressing"), "Outside");

            Ground(root);
            Sheds(root);
            Trees(root);
            Distance(root);

            // Unlit and outside the bake: no lightmap, no shadows, merged per material.
            foreach (Transform section in root)
                MergeStatic(section, "Outside_" + section.name);
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(level.scene);
            return $"Outside built: {root.GetComponentsInChildren<MeshRenderer>(true).Length} renderers, {Triangles(root)} triangles.";
        }

        // ---- Sky and sun ------------------------------------------------------------------------

        static void Sky()
        {
            string path = $"{MaterialFolder}/M_Env_Sky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (sky == null)
            {
                sky = new Material(Shader.Find("Skybox/Procedural")) { name = "M_Env_Sky" };
                AssetDatabase.CreateAsset(sky, path);
            }
            sky.SetFloat("_SunDisk", 2f);
            sky.SetFloat("_SunSize", 0.05f);
            sky.SetFloat("_SunSizeConvergence", 4f);
            sky.SetFloat("_AtmosphereThickness", 0.8f);
            sky.SetColor("_SkyTint", new Color(0.46f, 0.62f, 0.88f));
            sky.SetColor("_GroundColor", new Color(0.62f, 0.72f, 0.56f));
            sky.SetFloat("_Exposure", 1.25f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;

            // The Sun was switched off while the rooms had no openings (the ceiling hid it). Now it
            // shines in through the windows: from the south-west, 35 degrees up, in through the
            // Assembly Floor's west and south windows. Baked like the ceiling panels, so its
            // patches on the floors cost nothing at runtime. Transform.Find also finds it switched off.
            GameObject lighting = GameObject.Find("Lighting");
            Transform sunObject = lighting != null ? lighting.transform.Find("Sun") : null;
            if (sunObject != null)
            {
                sunObject.gameObject.SetActive(true);
                sunObject.rotation = Quaternion.Euler(35f, 45f, 0f);
                var sun = sunObject.GetComponent<Light>();
                sun.lightmapBakeType = LightmapBakeType.Baked;
                sun.shadows = LightShadows.Soft;
                sun.shadowAngle = 0.8f;                 // crisp edges on the window patches
                sun.intensity = 9f;                     // strong enough to read against the bright ceiling panels
                sun.color = new Color(1f, 0.85f, 0.62f); // warm afternoon light
                RenderSettings.sun = sun;
            }
        }

        // ---- The backdrop ---------------------------------------------------------------------

        static Material Unlit(string name, Color colour)
        {
            string path = $"{MaterialFolder}/M_Env_Outside_{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "M_Env_Outside_" + name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", colour);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void Ground(Transform root)
        {
            Transform ground = Group(root, "Ground");
            // Grass to the horizon, under the factory's floors (their tops are at 0).
            Box(ground, "Grass", new Vector3(400f, 0.02f, 400f), Unlit("Grass", Hex("9FC98A")), new Vector3(Centre, -0.03f, Centre));
            // A ring road round the factory, 4 to 10 m out, with white dashes.
            Material road = Unlit("Road", Hex("6D7286")), dash = Unlit("Dash", Hex("EEF1F6"));
            const float inner = -4f, outer = 41.5f + 4f, width = 6f;
            foreach (bool alongX in new[] { true, false })
                foreach (float side in new[] { inner - width * 0.5f, outer + width * 0.5f })
                {
                    float length = outer - inner + 2f * width;
                    Vector3 centre = alongX ? new Vector3(Centre, -0.015f, side) : new Vector3(side, -0.015f, Centre);
                    Box(ground, "Road", alongX ? new Vector3(length, 0.02f, width) : new Vector3(width, 0.02f, length), road, centre);
                    for (float d = -length * 0.5f + 2f; d < length * 0.5f - 1f; d += 4f)
                        Box(ground, "Dash", alongX ? new Vector3(1.8f, 0.02f, 0.2f) : new Vector3(0.2f, 0.02f, 1.8f), dash,
                            centre + (alongX ? Vector3.right : Vector3.forward) * d + Vector3.up * 0.004f);
                }
        }

        // Sheds with coloured gable roofs and striped chimneys, all round the factory.
        static void Sheds(Transform root)
        {
            Transform sheds = Group(root, "Sheds");
            Material wallA = Unlit("ShedWall", Hex("E4E8F0")), wallB = Unlit("ShedWallWarm", Hex("F2E3CC")), door = Unlit("ShedDoor", Hex("8A93AB"));
            Material[] roofs = { Unlit("RoofRed", Tomato), Unlit("RoofBlue", Cobalt), Unlit("RoofMint", Mint), Unlit("RoofYellow", Sun) };
            Material stackRed = Unlit("ChimneyRed", Hex("E8574C")), stackWhite = Unlit("ChimneyWhite", Hex("F4F4F4"));
            // (centre x, centre z, width, depth, height, faces along x)
            (float x, float z, float w, float d, float h, bool alongX)[] list =
            {
                (-34f, 4f, 16f, 10f, 8f, false), (-30f, 26f, 12f, 9f, 6f, false), (-48f, -22f, 20f, 12f, 10f, true),
                (6f, -32f, 18f, 10f, 8f, true), (30f, -38f, 14f, 10f, 7f, true), (-14f, -50f, 22f, 12f, 11f, true),
                (14f, 78f, 18f, 10f, 9f, true), (40f, 74f, 12f, 9f, 6f, true), (76f, 30f, 16f, 10f, 8f, false), (78f, 8f, 12f, 9f, 7f, false),
                (64f, -26f, 20f, 12f, 10f, true), (-20f, 70f, 16f, 10f, 8f, true)
            };
            for (int i = 0; i < list.Length; i++)
            {
                var (x, z, w, d, h, alongX) = list[i];
                Vector3 size = alongX ? new Vector3(w, h, d) : new Vector3(d, h, w);
                Box(sheds, "Shed", size, i % 2 == 0 ? wallA : wallB, new Vector3(x, h * 0.5f, z));
                // Gable roof: two slabs meeting along the ridge.
                float span = (alongX ? d : w) * 0.5f, pitch = 26f;
                float slab = span / Mathf.Cos(pitch * Mathf.Deg2Rad) + 0.4f, rise = span * Mathf.Tan(pitch * Mathf.Deg2Rad);
                foreach (float s in new[] { -1f, 1f })
                {
                    Vector3 offset = alongX ? new Vector3(0f, rise * 0.5f, s * span * 0.5f) : new Vector3(s * span * 0.5f, rise * 0.5f, 0f);
                    Quaternion tilt = alongX ? Quaternion.Euler(-s * pitch, 0f, 0f) : Quaternion.Euler(0f, 0f, s * pitch);
                    Box(sheds, "Roof", alongX ? new Vector3(w + 0.6f, 0.4f, slab) : new Vector3(slab, 0.4f, w + 0.6f), roofs[i % roofs.Length],
                        new Vector3(x, h, z) + offset, rotation: tilt);
                }
                // A big door on the side facing the factory.
                Vector3 toFactory = new Vector3(Centre - x, 0f, Centre - z);
                Vector3 doorSide = alongX ? new Vector3(0f, 0f, Mathf.Sign(toFactory.z) * (d * 0.5f + 0.05f)) : new Vector3(Mathf.Sign(toFactory.x) * (d * 0.5f + 0.05f), 0f, 0f);
                Box(sheds, "Shed_Door", alongX ? new Vector3(w * 0.35f, h * 0.6f, 0.1f) : new Vector3(0.1f, h * 0.6f, w * 0.35f), door,
                    new Vector3(x, h * 0.3f, z) + doorSide);
                // Every third shed has a striped chimney.
                if (i % 3 == 0)
                {
                    var c = new Vector3(x + (alongX ? w * 0.3f : 0f), 0f, z + (alongX ? 0f : w * 0.3f));
                    for (int band = 0; band < 6; band++)
                        Part(sheds, "Chimney", Cylinder(1.1f, 3.2f, 16), band % 2 == 0 ? stackRed : stackWhite, c + Vector3.up * band * 3.2f, Quaternion.identity);
                }
            }
        }

        // Lollipop trees in clumps between the road and the sheds.
        static void Trees(Transform root)
        {
            Transform trees = Group(root, "Trees");
            Material trunk = Unlit("Trunk", Hex("9A6A44"));
            Material[] tops = { Unlit("TreeGreen", Hex("6FBF73")), Unlit("TreeLime", Hex("9ACD5E")), Unlit("TreeDeep", Hex("4FA36A")) };
            var random = new System.Random(5);
            for (int i = 0; i < 46; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f, distance = 18f + (float)random.NextDouble() * 22f;
                var at = new Vector3(Centre + Mathf.Cos(angle) * (Centre + distance), 0f, Centre + Mathf.Sin(angle) * (Centre + distance));
                float size = 3f + (float)random.NextDouble() * 2.5f;
                Part(trees, "Trunk", Cylinder(0.3f, size * 0.7f, 8), trunk, at, Quaternion.identity);
                Part(trees, "Top", M.Ball, tops[i % tops.Length], at + Vector3.up * (size * 0.7f + size * 0.4f), Quaternion.identity, scale: Vector3.one * size);
            }
        }

        // Hills on the horizon and puffy clouds.
        static void Distance(Transform root)
        {
            Transform far = Group(root, "Far");
            Material[] hills = { Unlit("HillNear", Hex("8DBF95")), Unlit("HillFar", Hex("A9CBB8")) };
            var random = new System.Random(11);
            for (int i = 0; i < 14; i++)
            {
                float angle = i * Mathf.PI * 2f / 14f + (float)random.NextDouble() * 0.2f, distance = 150f + (float)random.NextDouble() * 50f;
                var at = new Vector3(Centre + Mathf.Cos(angle) * distance, -6f, Centre + Mathf.Sin(angle) * distance);
                Part(far, "Hill", M.Ball, hills[i % 2], at, Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f),
                    scale: new Vector3(70f + (float)random.NextDouble() * 40f, 30f + (float)random.NextDouble() * 20f, 40f));
            }
            Material cloud = Unlit("Cloud", Hex("FFFFFF"));
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI * 2f / 12f + 0.3f, distance = 95f + (float)random.NextDouble() * 50f;
                var at = new Vector3(Centre + Mathf.Cos(angle) * distance, 38f + (float)random.NextDouble() * 18f, Centre + Mathf.Sin(angle) * distance);
                for (int puff = 0; puff < 3; puff++)
                    Part(far, "Cloud", M.Ball, cloud, at + new Vector3((puff - 1) * 7f, puff == 1 ? 2.5f : 0f, (float)random.NextDouble() * 3f),
                        Quaternion.identity, scale: new Vector3(12f, 6f + (puff == 1 ? 3f : 0f), 9f));
            }
        }
    }
}
