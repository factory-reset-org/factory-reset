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

        [MenuItem("Factory Reset/Animation/Build Agent Animations")]
        public static void BuildAll()
        {
            var report = new StringBuilder();
            foreach (AgentMotionSpec spec in AgentMotionLibrary.All)
                Build(spec, report);
            AssetDatabase.SaveAssets();
            Debug.Log("Agent animations built.\n" + report);
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
            foreach (ClipSpec clipSpec in spec.Clips)
                clips[clipSpec.Name] = WriteClip($"{folder}/{spec.Model}_{clipSpec.Name}.anim", clipSpec, spec, model.transform);

            AnimatorController controller = WriteController($"{folder}/{spec.Model}.controller", spec, clips);
            WriteBody(spec, model, controller);

            report.AppendLine($"{spec.Model}: 5 clips, controller, body prefab");
            return true;
        }

        // ---- Clips ---------------------------------------------------------------------

        static bool CheckPaths(AgentMotionSpec spec, Transform modelRoot)
        {
            bool ok = true;
            foreach (ClipSpec clip in spec.Clips)
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
        static AnimationClip WriteClip(string path, ClipSpec spec, AgentMotionSpec agent, Transform modelRoot)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, path);
            }
            clip.ClearCurves();
            clip.frameRate = 30f;

            foreach (KeyValuePair<string, ChannelSet> pivot in ChannelsOf(agent))
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

        static Dictionary<string, ChannelSet> ChannelsOf(AgentMotionSpec agent)
        {
            var channels = new Dictionary<string, ChannelSet>();
            foreach (ClipSpec clip in agent.Clips)
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

        static AnimatorController WriteController(string path, AgentMotionSpec spec, Dictionary<string, AnimationClip> clips)
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
                var capsule = GetOrAdd<CharacterController>(root);
                capsule.radius = radius;
                capsule.height = height;
                capsule.center = new Vector3(0f, height * 0.5f, 0f);   // pivot at the feet
                GetOrAdd<AgentPathFollower>(root);
                GetOrAdd<AgentController>(root);

                Transform model = root.transform.Find(spec.Model);
                if (model == null)
                    model = ((GameObject)PrefabUtility.InstantiatePrefab(modelPrefab, root.transform)).transform;
                model.localPosition = Vector3.zero;
                model.localRotation = Quaternion.identity;
                Animator animator = GetOrAdd<Animator>(model.gameObject);
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;   // the path follower moves the body

                SetReference(GetOrAdd<AgentAnimatorBridge>(root), "animator", animator);

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

        // Not "??": in the editor a missing component comes back as Unity's fake null.
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
