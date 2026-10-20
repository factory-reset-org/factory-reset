using NUnit.Framework;
using ToyFactory.AI.Agents.Saboteur;
using UnityEditor;

namespace ToyFactory.Tests.EditMode
{
    public class SaboteurCurveSettingsTests
    {
        const string CurveFolder = "Assets/_Project/Scripts/AI/Agents/Saboteur/Curves/";
        const float Tolerance = 1e-5f;

        [TestCase("IdentityLinear", SaboteurCurveKind.Linear, 0.5f, 0.5f)]
        [TestCase("QuadraticFalloff", SaboteurCurveKind.Power, 0.5f, 0.25f)]
        [TestCase("AmmoDeficit", SaboteurCurveKind.Logistic, 0.7f, 0.5f)]
        [TestCase("Inverse", SaboteurCurveKind.Inverse, 0.5f, 0.5f)]
        public void CurveAssetEvaluatesItsConfiguredFormula(string assetName,
            SaboteurCurveKind expectedKind, float input, float expected)
        {
            SaboteurCurveSettings settings = Load(assetName);

            Assert.AreEqual(expectedKind, settings.Kind);
            Assert.AreEqual(expected, settings.Evaluate(input), Tolerance);
        }

        [Test]
        public void AmmoDeficitAssetFavoursLowRemainingAmmo()
        {
            SaboteurCurveSettings settings = Load("AmmoDeficit");

            Assert.Greater(settings.Evaluate(1f - 0.1f), settings.Evaluate(1f - 0.9f));
        }

        static SaboteurCurveSettings Load(string assetName)
        {
            string path = CurveFolder + assetName + ".asset";
            SaboteurCurveSettings settings = AssetDatabase.LoadAssetAtPath<SaboteurCurveSettings>(path);
            Assert.IsNotNull(settings, "Missing Saboteur curve asset: " + path);
            return settings;
        }
    }
}
