using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Guard;

namespace ToyFactory.Tests.EditMode
{
    public class GuardTuningTests
    {
        [Test]
        public void NewTuning_StartsAtTheDefaultLambda()
        {
            var tuning = ScriptableObject.CreateInstance<GuardTuning>();

            Assert.AreEqual(GuardBrain.Lambda, tuning.Lambda);
            Object.DestroyImmediate(tuning);
        }

        [Test]
        public void NegativeLambda_IsClampedToZero()
        {
            var tuning = ScriptableObject.CreateInstance<GuardTuning>();
            var serialized = new UnityEditor.SerializedObject(tuning);
            serialized.FindProperty("lambda").floatValue = -2f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.AreEqual(0f, tuning.Lambda);
            Object.DestroyImmediate(tuning);
        }

        [Test]
        public void CurrentLambda_ReadsTheSharedAssetOrTheDefault()
        {
            // The asset in Resources ships with lambda 3, the plan's value.
            Assert.AreEqual(3f, GuardTuning.CurrentLambda());
        }
    }
}
