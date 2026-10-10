using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.Animation;
using ToyFactory.Runtime.Movement;

namespace ToyFactory.Editor.Animation
{
    /// <summary>
    /// Builds the agents' animation assets from <see cref="AgentMotionLibrary"/>: one looping
    /// clip per locomotion pose, one Animator Controller per agent with a 2D Blend Tree over
    /// Speed and TurnRate, and one body prefab per agent that nests S3's model prefab.
    /// Run it again after changing a number; clips, controllers and prefabs are updated in
    /// place, so their GUIDs (and every reference to them) stay the same.
    /// </summary>
    /// <remarks>
    /// Every pivot path is checked against the model before anything is written, so a renamed
    /// node is reported instead of producing a clip that silently animates nothing.
    /// </remarks>
    public static class AgentAnimationBuilder
    {
        const string AnimationsFolder = "Assets/_Project/Animations";
        const string ModelPrefabFolder = "Assets/_Project/Prefabs/Characters";
        const string BodyFolder = "Assets/_Project/Prefabs/Agents";

        // Measured body capsules (decision log, 1 October): radius, height in metres.
        static readonly Dictionary<string, (AgentType type, float radius, float height)> Bodies =
            new Dictionary<string, (AgentType, float, float)>
            {
                { "TrackerToy", (AgentType.Tracker, 0.50f, 1.40f) },
                { "SaboteurBot", (AgentType.Saboteur, 0.45f, 1.60f) },
                { "GuardBot", (AgentType.Guard, 0.55f, 2.45f) },
                { "CaptainBot", (AgentType.Captain, 0.55f, 3.25f) },
            };

        const string ShotLineMaterialPath = BodyFolder + "/AgentShotLine.mat";
        const string LightMaterialPath = BodyFolder + "/AgentLight.mat";
        const string SparkMaterialPath = BodyFolder + "/AgentSpark.mat";
        const string FxTextureFolder = "Assets/_Project/Textures/FX";

        // A part that glows: either one of the model's own meshes (the Tracker's antenna ball),
        // or a lens added inside a visor or goggle. S3's visors and goggles are frames, and the
        // glow belongs inside them, not on the frame, so the lens is a thin primitive placed
        // just in front of the visor plate or inside the goggle tube, under the same pivot.
        sealed class LightPart
        {
            public string Path;          // an existing part, or the lens's parent pivot
            public string LensName;      // null for an existing part
            public PrimitiveType Shape;
            public Vector3 Position;
            public Vector3 Euler;
            public Vector3 Scale;

            public static LightPart Existing(string path) => new LightPart { Path = path };

            public static LightPart Lens(string parent, string name, PrimitiveType shape, Vector3 position, Vector3 euler, Vector3 scale) =>
                new LightPart { Path = parent, LensName = name, Shape = shape, Position = position, Euler = euler, Scale = scale };
        }

        // What glows on each agent, and its colour from the prototype: the Tracker's green
        // antenna ball, the Guard's cyan visor, the Saboteur's green goggles and the Captain's
        // red visor. Lens sizes come from the visor and goggle meshes: the Guard's visor plate
        // is 0.44 x 0.11 x 0.035 m, the Captain's 0.64 x 0.17 x 0.03 m, each goggle rim 0.24 m
        // across and closed at the front (front face 0.41 m forward), so its lens sits on that face.
        static readonly Dictionary<string, (LightPart[] parts, Color colour)> Lights =
            new Dictionary<string, (LightPart[], Color)>
            {
                { "TrackerToy", (new[] { LightPart.Existing("TrackerToy_Root/Body_Pivot/Head_Pivot/Antenna_Bulb") },
                    new Color(0.30f, 0.85f, 0.40f)) },
                { "GuardBot", (new[] { LightPart.Lens("GuardBot_Root/Torso_Pivot/Head_Pivot", "Visor_Lens", PrimitiveType.Cube,
                    new Vector3(0f, 0.435f, 0.272f), Vector3.zero, new Vector3(0.36f, 0.07f, 0.006f)) },
                    new Color(0.38f, 0.85f, 1f)) },
                { "SaboteurBot", (new[]
                    {
                        LightPart.Lens("SaboteurBot_Root/Body_Pivot", "Goggle_Lens_L", PrimitiveType.Cylinder,
                            new Vector3(-0.19f, 1.035f, 0.413f), new Vector3(90f, 0f, 0f), new Vector3(0.16f, 0.004f, 0.16f)),
                        LightPart.Lens("SaboteurBot_Root/Body_Pivot", "Goggle_Lens_R", PrimitiveType.Cylinder,
                            new Vector3(0.19f, 1.035f, 0.413f), new Vector3(90f, 0f, 0f), new Vector3(0.16f, 0.004f, 0.16f)),
                    }, new Color(0.49f, 1f, 0.42f)) },
                { "CaptainBot", (new[] { LightPart.Lens("CaptainBot_Root/Torso_Pivot/Head_Pivot", "Visor_Lens", PrimitiveType.Cube,
                    new Vector3(0f, 0.455f, 0.337f), Vector3.zero, new Vector3(0.54f, 0.11f, 0.006f)) },
                    new Color(1f, 0.16f, 0.24f)) },
            };

        // Taking hits: hit points, knock-out seconds (the plan's reassemble times) and whether
        // the agent is scrapped instead (the Saboteurs are destroyed for good).
        static readonly Dictionary<string, (int hitPoints, float knockOut, bool scrap)> Toughness =
            new Dictionary<string, (int, float, bool)>
            {
                { "TrackerToy", (3, 7f, false) },
                { "SaboteurBot", (2, 0f, true) },
                { "GuardBot", (4, 8f, false) },
                { "CaptainBot", (6, 6f, false) },
            };

        // Agents that shoot: their cannon meshes and the damage per hit.
        static readonly Dictionary<string, (string[] barrels, float damage)> Weapons =
            new Dictionary<string, (string[], float)>
            {
                { "GuardBot", (new[] { "GuardBot_Root/Torso_Pivot/CannonArm_R_Pivot/Cannon_Barrel" }, 10f) },
                { "CaptainBot", (new[] { "CaptainBot_Root/Torso_Pivot/CannonArm_L_Pivot/Cannon_L",
                                         "CaptainBot_Root/Torso_Pivot/CannonArm_R_Pivot/Cannon_R" }, 15f) },
            };

        [MenuItem("Factory Reset/Animation/Build Agent Animations")]
        public static void BuildAll()
        {
            var report = new StringBuilder();
            foreach (AgentMotionSpec spec in AgentMotionLibrary.All)
                Build(spec, report);
            AssetDatabase.SaveAssets();
            Debug.Log("Agent animations built.\n" + report);
        }

        /// <summary>
        /// Adds or refreshes only the hit effects (knock-down, lights) on the four existing
        /// bodies, without rebuilding their clips and controllers.
        /// </summary>
        [MenuItem("Factory Reset/Animation/Update Agent Hit Effects")]
        public static void UpdateHitEffects()
        {
            foreach (string name in Bodies.Keys)
            {
                string path = $"{BodyFolder}/Agent_{name}.prefab";
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Transform model = root.transform.Find(name);
                    AddHitEffects(root, model, name);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Agent hit effects updated on " + Bodies.Count + " bodies.");
        }

        /// <summary>Builds one agent's clips, controller and body prefab. False if its model is missing a pivot.</summary>
        public static bool Build(AgentMotionSpec spec, StringBuilder report)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelPrefabFolder}/{spec.Model}.prefab");
            if (model == null)
            {
                Debug.LogError($"No model prefab at {ModelPrefabFolder}/{spec.Model}.prefab.");
                return false;
            }
            if (!CheckPaths(spec, model.transform))
                return false;

            string folder = $"{AnimationsFolder}/{spec.Model}";
            EnsureFolder(folder);

            var clips = new Dictionary<string, AnimationClip>();
            Dictionary<string, ChannelSet> locomotion = ChannelsOf(spec.Clips);
            foreach (ClipSpec clipSpec in spec.Clips)
                clips[clipSpec.Name] = WriteClip($"{folder}/{spec.Model}_{clipSpec.Name}.anim", clipSpec, locomotion, model.transform);

            // The aim pose keys only its own pivots: it is an override layer on top of locomotion.
            AnimationClip aim = spec.Aim == null ? null :
                WriteClip($"{folder}/{spec.Model}_{spec.Aim.Name}.anim", spec.Aim, ChannelsOf(new[] { spec.Aim }), model.transform);

            AnimatorController controller = WriteController($"{folder}/{spec.Model}.controller", spec, clips, aim);
            WriteBody(spec, model, controller);

            report.AppendLine($"{spec.Model}: {(aim != null ? 6 : 5)} clips, controller, body prefab");
            return true;
        }

        // ---- Clips ---------------------------------------------------------------------

        static bool CheckPaths(AgentMotionSpec spec, Transform modelRoot)
        {
            bool ok = true;
            var all = new List<ClipSpec>(spec.Clips);
            if (spec.Aim != null)
                all.Add(spec.Aim);
            foreach (ClipSpec clip in all)
                foreach (Wave wave in clip.Waves)
                    if (modelRoot.Find(wave.Path) == null)
                    {
                        Debug.LogError($"{spec.Model}: pivot \"{wave.Path}\" (clip {clip.Name}) is not in the model.");
                        ok = false;
                    }
            return ok;
        }

        // Every clip of an agent keys the same channels (rest values where a clip has no
        // wave), so the blend tree always blends like with like and nothing snaps to a
        // default when one clip's weight drops to 0. Waves on the same channel add up.
        static AnimationClip WriteClip(string path, ClipSpec spec, Dictionary<string, ChannelSet> channels, Transform modelRoot)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, path);
            }
            clip.ClearCurves();
            clip.frameRate = 30f;

            foreach (KeyValuePair<string, ChannelSet> pivot in channels)
            {
                Transform node = modelRoot.Find(pivot.Key);
                if (pivot.Value.Rotates)
                    for (int axis = 0; axis < 3; axis++)
                        SetCurve(clip, pivot.Key, "localEulerAnglesRaw." + "xyz"[axis],
                            Sample(spec, pivot.Key, (Channel)axis, 0f));
                if (pivot.Value.Moves)
                {
                    Vector3 rest = node.localPosition;
                    SetCurve(clip, pivot.Key, "m_LocalPosition.x", Constant(rest.x, spec.Length));
                    SetCurve(clip, pivot.Key, "m_LocalPosition.y", Sample(spec, pivot.Key, Channel.PosY, rest.y));
                    SetCurve(clip, pivot.Key, "m_LocalPosition.z", Constant(rest.z, spec.Length));
                }
            }

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        sealed class ChannelSet
        {
            public bool Rotates;
            public bool Moves;
        }

        static Dictionary<string, ChannelSet> ChannelsOf(IEnumerable<ClipSpec> clips)
        {
            var channels = new Dictionary<string, ChannelSet>();
            foreach (ClipSpec clip in clips)
                foreach (Wave wave in clip.Waves)
                {
                    if (!channels.TryGetValue(wave.Path, out ChannelSet set))
                        channels[wave.Path] = set = new ChannelSet();
                    if (wave.Channel == Channel.PosY)
                        set.Moves = true;
                    else
                        set.Rotates = true;
                }
            return channels;
        }

        // Samples the sum of the clip's waves on one channel, 12 keys per cycle of the
        // fastest wave, with the last key equal to the first so the loop is seamless.
        static AnimationCurve Sample(ClipSpec clip, string path, Channel channel, float rest)
        {
            int cycles = 1;
            foreach (Wave wave in clip.Waves)
                if (wave.Path == path && wave.Channel == channel && wave.Amplitude != 0f)
                    cycles = Mathf.Max(cycles, wave.Cycles);

            int keys = Mathf.Max(8, cycles * 12);
            var curve = new AnimationCurve();
            for (int i = 0; i <= keys; i++)
            {
                float u = (float)i / keys;
                float value = rest;
                foreach (Wave wave in clip.Waves)
                    if (wave.Path == path && wave.Channel == channel)
                        value += wave.Offset + wave.Amplitude * Mathf.Sin(2f * Mathf.PI * (wave.Cycles * u + wave.Phase));
                curve.AddKey(new Keyframe(u * clip.Length, value));
            }
            for (int i = 0; i < curve.length; i++)
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            for (int i = 0; i < curve.length; i++)
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            return curve;
        }

        static AnimationCurve Constant(float value, float length) =>
            new AnimationCurve(new Keyframe(0f, value), new Keyframe(length, value));

        static void SetCurve(AnimationClip clip, string path, string property, AnimationCurve curve) =>
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property), curve);

        // ---- Controller ----------------------------------------------------------------

        static AnimatorController WriteController(string path, AgentMotionSpec spec, Dictionary<string, AnimationClip> clips,
            AnimationClip aim)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(path);

            // Rebuild in place: parameters, then the base layer's states and blend tree.
            foreach (AnimatorControllerParameter parameter in controller.parameters)
                controller.RemoveParameter(parameter);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("TurnRate", AnimatorControllerParameterType.Float);
            controller.AddParameter("IsAttacking", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsDead", AnimatorControllerParameterType.Bool);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            foreach (ChildAnimatorState child in machine.states)
            {
                if (child.state.motion is BlendTree oldTree)
                    AssetDatabase.RemoveObjectFromAsset(oldTree);
                machine.RemoveState(child.state);
            }

            AnimatorState locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.FreeformCartesian2D;
            tree.blendParameter = "Speed";
            tree.blendParameterY = "TurnRate";
            tree.AddChild(clips["Idle"], new Vector2(0f, 0f));
            tree.AddChild(clips["Walk"], new Vector2(spec.WalkSpeed, 0f));
            tree.AddChild(clips["Run"], new Vector2(spec.RunSpeed, 0f));
            tree.AddChild(clips["LeanLeft"], new Vector2(spec.RunSpeed, -AgentMotionLibrary.LeanTurnRate));
            tree.AddChild(clips["LeanRight"], new Vector2(spec.RunSpeed, AgentMotionLibrary.LeanTurnRate));
            machine.defaultState = locomotion;

            // Rebuild the Aim layer: an override layer at weight 0 that the animator bridge
            // fades in while the agent attacks. Its weight is driven from code, not by states.
            for (int i = controller.layers.Length - 1; i >= 1; i--)
                controller.RemoveLayer(i);
            if (aim != null)
            {
                controller.AddLayer("Aim");
                AnimatorControllerLayer[] layers = controller.layers;
                AnimatorControllerLayer aimLayer = layers[layers.Length - 1];
                aimLayer.blendingMode = AnimatorLayerBlendingMode.Override;
                aimLayer.defaultWeight = 0f;
                AnimatorState pose = aimLayer.stateMachine.AddState("Aim");
                pose.motion = aim;
                aimLayer.stateMachine.defaultState = pose;
                controller.layers = layers;
            }

            EditorUtility.SetDirty(controller);
            return controller;
        }

        // ---- Body prefab ---------------------------------------------------------------

        // Agent_<Model>: the agent components on the root (capsule, path follower, controller,
        // animation bridge, spinners) and S3's model prefab nested as the only child, with an
        // Animator added as a prefab override. S3's prefab itself is never modified. An
        // existing body is edited in place, so the scenes' references to it keep working.
        static void WriteBody(AgentMotionSpec spec, GameObject modelPrefab, AnimatorController controller)
        {
            (AgentType _, float radius, float height) = Bodies[spec.Model];
            EnsureFolder(BodyFolder);
            string path = $"{BodyFolder}/Agent_{spec.Model}.prefab";

            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
            GameObject root = exists ? PrefabUtility.LoadPrefabContents(path) : new GameObject($"Agent_{spec.Model}");
            try
            {
                // The capsule is also the hitbox. It sits on the Agents layer, so the agents'
                // shots pass through each other, and S2's cover check (which skips capsules)
                // is unaffected.
                int agentsLayer = LayerMask.NameToLayer("Agents");
                if (agentsLayer >= 0)
                    root.layer = agentsLayer;

                var capsule = GetOrAdd<CharacterController>(root);
                capsule.radius = radius;
                capsule.height = height;
                capsule.center = new Vector3(0f, height * 0.5f, 0f);   // pivot at the feet
                GetOrAdd<AgentPathFollower>(root);
                var agent = GetOrAdd<AgentController>(root);
                (int hitPoints, float knockOut, bool scrap) = Toughness[spec.Model];
                var agentSettings = new SerializedObject(agent);
                agentSettings.FindProperty("hitPoints").intValue = hitPoints;
                agentSettings.FindProperty("knockOutSeconds").floatValue = knockOut;
                agentSettings.FindProperty("scrapWhenDown").boolValue = scrap;
                agentSettings.ApplyModifiedPropertiesWithoutUndo();

                Transform model = root.transform.Find(spec.Model);
                if (model == null)
                    model = ((GameObject)PrefabUtility.InstantiatePrefab(modelPrefab, root.transform)).transform;
                model.localPosition = Vector3.zero;
                model.localRotation = Quaternion.identity;
                Animator animator = GetOrAdd<Animator>(model.gameObject);
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;   // the path follower moves the body

                SetReference(GetOrAdd<AgentAnimatorBridge>(root), "animator", animator);

                // Tips over (with a small explosion) when downed, its lights go out and come
                // back, and it shows "?"/"!" above its head.
                AddHitEffects(root, model, spec.Model);
                var icon = new SerializedObject(GetOrAdd<AlertIcon>(root));
                icon.FindProperty("height").floatValue = height + 0.4f;
                icon.ApplyModifiedPropertiesWithoutUndo();

                if (Weapons.TryGetValue(spec.Model, out (string[] barrels, float damage) weapon))
                {
                    var gun = GetOrAdd<AgentWeapon>(root);
                    var gunSettings = new SerializedObject(gun);
                    SerializedProperty barrels = gunSettings.FindProperty("barrels");
                    barrels.arraySize = weapon.barrels.Length;
                    for (int i = 0; i < weapon.barrels.Length; i++)
                        barrels.GetArrayElementAtIndex(i).objectReferenceValue = model.Find(weapon.barrels[i]).GetComponent<Renderer>();
                    gunSettings.FindProperty("damage").floatValue = weapon.damage;
                    gunSettings.FindProperty("lineMaterial").objectReferenceValue = ShotLineMaterial();
                    gunSettings.ApplyModifiedPropertiesWithoutUndo();
                }

                if (spec.Model == "TrackerToy")
                {
                    SetWheels(GetOrAdd<WheelSpinner>(root), model, 0.16f,
                        "TrackerToy_Root/Wheel_L_Front_Pivot", "TrackerToy_Root/Wheel_R_Front_Pivot",
                        "TrackerToy_Root/Wheel_L_Rear_Pivot", "TrackerToy_Root/Wheel_R_Rear_Pivot");
                    SetReference(GetOrAdd<WindUpKeySpinner>(root), "key",
                        model.Find("TrackerToy_Root/Body_Pivot/WindupKey_Pivot"));
                }
                else if (spec.Model == "SaboteurBot")
                {
                    SetWheels(GetOrAdd<WheelSpinner>(root), model, 0.365f, "SaboteurBot_Root/Wheel_Pivot");
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                if (exists)
                    PrefabUtility.UnloadPrefabContents(root);
                else
                    Object.DestroyImmediate(root);
            }
        }

        // Knock-down and lights. Replaces the old fall-apart component if a body still has it.
        static void AddHitEffects(GameObject root, Transform model, string modelName)
        {
            // The fall-apart component's script was deleted; its leftover entry is a missing script.
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);

            var knockdown = new SerializedObject(GetOrAdd<AgentKnockdown>(root));
            knockdown.FindProperty("model").objectReferenceValue = model;
            knockdown.FindProperty("sparkMaterial").objectReferenceValue = SparkMaterial();
            knockdown.FindProperty("knockOutWord").objectReferenceValue = ComicSprite("Comic_KnockOut.png");
            knockdown.FindProperty("scrapWord").objectReferenceValue = ComicSprite("Comic_Scrapped.png");
            knockdown.FindProperty("dormantUntilCaptainWakes").boolValue = modelName == "CaptainBot";
            knockdown.ApplyModifiedPropertiesWithoutUndo();

            // The Captain kneels instead of tipping over, and steps back up.
            if (modelName == "CaptainBot")
                AddKneel(root, model);

            if (!Lights.TryGetValue(modelName, out (LightPart[] parts, Color colour) light))
                return;
            var lights = new SerializedObject(GetOrAdd<AgentLights>(root));
            SerializedProperty renderers = lights.FindProperty("lights");
            renderers.arraySize = light.parts.Length;
            for (int i = 0; i < light.parts.Length; i++)
            {
                Renderer part = LightRenderer(model, light.parts[i]);
                if (part == null)
                    Debug.LogError($"{modelName}: no light part at {light.parts[i].Path}.");
                renderers.GetArrayElementAtIndex(i).objectReferenceValue = part;
            }
            lights.FindProperty("lightMaterial").objectReferenceValue = LightMaterial();
            lights.FindProperty("colour").colorValue = light.colour;
            lights.FindProperty("darkUntilCaptainWakes").boolValue = modelName == "CaptainBot";
            lights.ApplyModifiedPropertiesWithoutUndo();
        }

        static void AddKneel(GameObject root, Transform model)
        {
            const string rig = "CaptainBot_Root";
            const string legL = rig + "/Leg_L_Pivot", legR = rig + "/Leg_R_Pivot";
            const string kneeL = legL + "/Knee_L_Pivot", kneeR = legR + "/Knee_R_Pivot";
            const string torso = rig + "/Torso_Pivot";
            (string field, string path)[] pivots =
            {
                ("root", rig),
                ("frontHip", legL), ("frontKnee", kneeL), ("frontAnkle", kneeL + "/Ankle_L_Pivot"),
                ("backHip", legR), ("backKnee", kneeR), ("backAnkle", kneeR + "/Ankle_R_Pivot"),
                ("torso", torso), ("head", torso + "/Head_Pivot"),
                ("frontArm", torso + "/CannonArm_L_Pivot"), ("backArm", torso + "/CannonArm_R_Pivot"),
            };

            var kneel = new SerializedObject(GetOrAdd<AgentKneel>(root));
            foreach ((string field, string path) in pivots)
            {
                Transform pivot = model.Find(path);
                if (pivot == null)
                    Debug.LogError($"CaptainBot: no pivot at {path} for the kneel.");
                kneel.FindProperty(field).objectReferenceValue = pivot;
            }
            kneel.ApplyModifiedPropertiesWithoutUndo();
        }

        // The renderer of an existing part, or of a lens, added under its pivot the first time
        // (an added object on the body prefab; S3's model prefab is not touched) and placed again
        // on every run, so a changed size takes effect. Lenses have no collider.
        static Renderer LightRenderer(Transform model, LightPart part)
        {
            Transform parent = model.Find(part.Path);
            if (parent == null)
                return null;
            if (part.LensName == null)
                return parent.GetComponent<Renderer>();

            Transform lens = parent.Find(part.LensName);
            if (lens == null)
            {
                GameObject made = GameObject.CreatePrimitive(part.Shape);
                made.name = part.LensName;
                Object.DestroyImmediate(made.GetComponent<Collider>());
                lens = made.transform;
                lens.SetParent(parent, false);
            }
            lens.localPosition = part.Position;
            lens.localEulerAngles = part.Euler;
            lens.localScale = part.Scale;
            var renderer = lens.GetComponent<Renderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.sharedMaterial = LightMaterial();
            return renderer;
        }

        // Lit and emissive; AgentLights copies it once per agent and sets the colour.
        static Material LightMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(LightMaterialPath);
            if (material != null)
                return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "AgentLight" };
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            material.SetColor("_BaseColor", Color.white);
            material.SetColor("_EmissionColor", Color.white);
            material.SetFloat("_Smoothness", 0.7f);
            AssetDatabase.CreateAsset(material, LightMaterialPath);
            return material;
        }

        // Unlit particles, additive, with a soft round dot, coloured per particle.
        static Material SparkMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(SparkMaterialPath);
            if (material != null)
                return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "AgentSpark" };
            material.SetFloat("_Surface", 1f);   // transparent
            material.SetFloat("_Blend", 2f);     // additive
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            var dot = AssetDatabase.LoadAssetAtPath<Texture2D>(FxTextureFolder + "/Spark.png");
            material.SetTexture("_BaseMap", dot);
            AssetDatabase.CreateAsset(material, SparkMaterialPath);
            return material;
        }

        // The comic words are imported as sprites, without mipmaps, keeping their transparency.
        static Sprite ComicSprite(string file)
        {
            string path = FxTextureFolder + "/" + file;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // Not "??": in the editor a missing component comes back as Unity's fake null.
        // Unlit and coloured by the line's vertex colour, so one material serves the red aim
        // line and the yellow tracer. Created once, then reused.
        static Material ShotLineMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(ShotLineMaterialPath);
            if (material != null)
                return material;
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            material = new Material(shader) { name = "AgentShotLine" };
            AssetDatabase.CreateAsset(material, ShotLineMaterialPath);
            return material;
        }

        static T GetOrAdd<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        static void SetWheels(WheelSpinner spinner, Transform model, float radius, params string[] pivots)
        {
            var serialized = new SerializedObject(spinner);
            SerializedProperty wheels = serialized.FindProperty("wheels");
            wheels.arraySize = pivots.Length;
            for (int i = 0; i < pivots.Length; i++)
            {
                SerializedProperty wheel = wheels.GetArrayElementAtIndex(i);
                wheel.FindPropertyRelative("pivot").objectReferenceValue = model.Find(pivots[i]);
                wheel.FindPropertyRelative("radius").floatValue = radius;
                wheel.FindPropertyRelative("localAxis").vector3Value = Vector3.right;   // axles run along X
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetReference(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;
            int slash = folder.LastIndexOf('/');
            string parent = folder.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder.Substring(slash + 1));
        }
    }
}
