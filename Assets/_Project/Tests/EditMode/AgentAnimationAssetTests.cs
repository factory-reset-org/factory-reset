using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// Checks the generated animation assets against S3's frozen models: every curve drives a
    /// pivot that exists, every clip loops cleanly, and every controller blends the five
    /// clips over Speed and TurnRate. A renamed model node fails here, not silently in game.
    /// </summary>
    public class AgentAnimationAssetTests
    {
        static readonly string[] Models = { "TrackerToy", "SaboteurBot", "GuardBot", "CaptainBot" };
        static readonly string[] Clips = { "Idle", "Walk", "Run", "LeanLeft", "LeanRight" };

        static AnimationClip Clip(string model, string clip) =>
            AssetDatabase.LoadAssetAtPath<AnimationClip>($"Assets/_Project/Animations/{model}/{model}_{clip}.anim");

        [TestCaseSource(nameof(Models))]
        public void EveryCurveDrivesAPivotOfTheModel(string model)
        {
            var modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/Characters/{model}.prefab");
            Assert.IsNotNull(modelPrefab);
            foreach (string name in Clips)
            {
                AnimationClip clip = Clip(model, name);
                Assert.IsNotNull(clip, $"{model}_{name}");
                EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
                Assert.Greater(bindings.Length, 0);
                foreach (EditorCurveBinding binding in bindings)
                    Assert.IsNotNull(AnimationUtility.GetAnimatedObject(modelPrefab, binding),
                        $"{model}_{name}: \"{binding.path}\" is not in the model.");
            }
        }

        [TestCaseSource(nameof(Models))]
        public void EveryClipLoopsWithoutAJump(string model)
        {
            foreach (string name in Clips)
            {
                AnimationClip clip = Clip(model, name);
                Assert.IsTrue(clip.isLooping, $"{model}_{name} loops");
                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                {
                    AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                    float first = curve.keys[0].value, last = curve.keys[curve.length - 1].value;
                    Assert.AreEqual(first, last, 1e-3f, $"{model}_{name} {binding.path} {binding.propertyName}");
                }
            }
        }

        [TestCaseSource(nameof(Models))]
        public void ControllerBlendsFiveClipsOverSpeedAndTurnRate(string model)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>($"Assets/_Project/Animations/{model}/{model}.controller");
            Assert.IsNotNull(controller);
            CollectionAssert.AreEquivalent(new[] { "Speed", "TurnRate", "IsAttacking", "IsDead" },
                System.Array.ConvertAll(controller.parameters, p => p.name));

            var tree = controller.layers[0].stateMachine.defaultState.motion as BlendTree;
            Assert.IsNotNull(tree, "The default state is the locomotion blend tree.");
            Assert.AreEqual(BlendTreeType.FreeformCartesian2D, tree.blendType);
            Assert.AreEqual("Speed", tree.blendParameter);
            Assert.AreEqual("TurnRate", tree.blendParameterY);
            Assert.AreEqual(5, tree.children.Length);
            Assert.AreEqual(Vector2.zero, tree.children[0].position, "Idle at rest.");
            Assert.Less(tree.children[3].position.y, 0f, "Lean left on a left turn (negative TurnRate).");
            Assert.Greater(tree.children[4].position.y, 0f);
        }

        [TestCase("GuardBot", true)]
        [TestCase("CaptainBot", true)]
        [TestCase("TrackerToy", false)]
        [TestCase("SaboteurBot", false)]
        public void ShootersHaveAnAimOverrideLayerStartingAtWeightZero(string model, bool shoots)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>($"Assets/_Project/Animations/{model}/{model}.controller");
            AnimatorControllerLayer[] layers = controller.layers;
            if (!shoots)
            {
                Assert.AreEqual(1, layers.Length, "Agents that do not shoot have locomotion only.");
                return;
            }

            Assert.AreEqual(2, layers.Length);
            AnimatorControllerLayer aim = layers[1];
            Assert.AreEqual("Aim", aim.name);
            Assert.AreEqual(AnimatorLayerBlendingMode.Override, aim.blendingMode);
            Assert.AreEqual(0f, aim.defaultWeight, "The bridge fades it in only while attacking.");
            var clip = aim.stateMachine.defaultState.motion as AnimationClip;
            Assert.IsNotNull(clip);

            var modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/Characters/{model}.prefab");
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                Assert.IsNotNull(AnimationUtility.GetAnimatedObject(modelPrefab, binding), binding.path);
        }

        [TestCase("TrackerToy", 3, 7f, false, false)]
        [TestCase("SaboteurBot", 2, 0f, true, false)]
        [TestCase("GuardBot", 4, 8f, false, true)]
        [TestCase("CaptainBot", 6, 6f, false, true)]
        public void BodyCarriesItsToughnessHitboxLayerAndWeapon(string model, int hitPoints, float knockOut, bool scrap, bool armed)
        {
            var body = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/Agents/Agent_{model}.prefab");
            Assert.AreEqual(LayerMask.NameToLayer("Agents"), body.layer, "The capsule hitbox is on the Agents layer.");

            var settings = new SerializedObject(body.GetComponent("AgentController"));
            Assert.AreEqual(hitPoints, settings.FindProperty("hitPoints").intValue);
            Assert.AreEqual(knockOut, settings.FindProperty("knockOutSeconds").floatValue);
            Assert.AreEqual(scrap, settings.FindProperty("scrapWhenDown").boolValue);

            Assert.IsNull(body.GetComponent("AgentFallApart"), "The old fall-apart is gone.");
            var knockdown = body.GetComponent("AgentKnockdown");
            Assert.IsNotNull(knockdown, "Tips over with a small explosion when downed.");
            var knockdownSettings = new SerializedObject(knockdown);
            foreach (string field in new[] { "model", "sparkMaterial", "knockOutWord", "scrapWord" })
                Assert.IsNotNull(knockdownSettings.FindProperty(field).objectReferenceValue, field);
            var lights = body.GetComponent("AgentLights");
            Assert.IsNotNull(lights, "Its lights go out when downed.");
            var lightSettings = new SerializedObject(lights);
            Assert.IsNotNull(lightSettings.FindProperty("lightMaterial").objectReferenceValue);
            SerializedProperty parts = lightSettings.FindProperty("lights");
            Assert.Greater(parts.arraySize, 0);
            for (int i = 0; i < parts.arraySize; i++)
                Assert.IsNotNull(parts.GetArrayElementAtIndex(i).objectReferenceValue, "Light part " + i);
            var icon = body.GetComponent("AlertIcon");
            Assert.IsNotNull(icon, "Shows \"?\" / \"!\".");
            Assert.Greater(new SerializedObject(icon).FindProperty("height").floatValue,
                body.GetComponent<CharacterController>().height, "The icon floats above the head.");

            Component weapon = body.GetComponent("AgentWeapon");
            Assert.AreEqual(armed, weapon != null);
            if (armed)
            {
                SerializedProperty barrels = new SerializedObject(weapon).FindProperty("barrels");
                Assert.Greater(barrels.arraySize, 0);
                for (int i = 0; i < barrels.arraySize; i++)
                    Assert.IsNotNull(barrels.GetArrayElementAtIndex(i).objectReferenceValue, "Every barrel is set.");
            }
        }

        [TestCaseSource(nameof(Models))]
        public void BodyPrefabNestsTheModelWithItsControllerAndAFeetPivotCapsule(string model)
        {
            var body = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/Agents/Agent_{model}.prefab");
            Assert.IsNotNull(body);
            var capsule = body.GetComponent<CharacterController>();
            Assert.IsNotNull(capsule);
            Assert.AreEqual(capsule.height * 0.5f, capsule.center.y, 1e-4f, "The pivot is at the feet.");
            Assert.LessOrEqual(capsule.radius, 0.55f, "Fits the grid's 0.55 m clearance.");

            Transform modelChild = body.transform.Find(model);
            Assert.IsNotNull(modelChild, "S3's model is nested, not copied.");
            Assert.IsTrue(PrefabUtility.IsAnyPrefabInstanceRoot(modelChild.gameObject));
            var animator = modelChild.GetComponent<Animator>();
            Assert.IsNotNull(animator);
            Assert.IsFalse(animator.applyRootMotion, "The path follower moves the body.");
            Assert.AreEqual($"{model}", animator.runtimeAnimatorController.name);
        }
    }
}
