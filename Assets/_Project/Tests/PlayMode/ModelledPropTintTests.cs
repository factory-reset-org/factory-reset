using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Tests
{
    /// <summary>
    /// The pressure plate, control switches, switch cages and power cores carry S3's models in the
    /// real level, and the part each script tints is a part of that model, not the hidden primitive.
    /// The prop types are in the default assembly, so they are reached by name.
    /// </summary>
    public sealed class ModelledPropTintTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            Scene empty = SceneManager.CreateScene("Empty after " + Guid.NewGuid());
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != empty && scene.isLoaded)
                    yield return SceneManager.UnloadSceneAsync(scene);
            }

            if (GameClock.Current is UnityEngine.Object clock && clock == null)
                GameClock.Publish(null);
            if (PlayerState.Current is UnityEngine.Object player && player == null)
                PlayerState.Publish(null);
        }

        static Renderer Field(Component prop, string name)
        {
            FieldInfo field = prop.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, prop.GetType().Name + " has a " + name + " field");
            return field.GetValue(prop) as Renderer;
        }

        static Component Find(string typeName)
        {
            Type type = Type.GetType("ToyFactory.Interaction." + typeName + ",Assembly-CSharp");
            Assert.IsNotNull(type, typeName + " type");
            var found = UnityEngine.Object.FindAnyObjectByType(type, FindObjectsInactive.Include) as Component;
            Assert.IsNotNull(found, "a " + typeName + " in the level");
            return found;
        }

        [UnityTest]
        public IEnumerator ThePlateSwitchCageAndCoreTintAPartOfTheirModel()
        {
            AgentSpawner previous = AgentSpawner.Instance;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            float until = Time.realtimeSinceStartup + 60f;
            while (!(AgentSpawner.Instance != null && AgentSpawner.Instance != previous
                     && ChapterManager.Current != null && ChapterManager.Current.Flow != null
                     && ChapterManager.Current.Flow.HasBegun && GameObject.Find("PressurePlate") != null))
            {
                if (Time.realtimeSinceStartup > until)
                    Assert.Fail("Timed out waiting for the level");
                yield return null;
            }
            yield return null;

            Component plate = Find("PressurePlate");
            Component sw = Find("ControlSwitch");
            Component cage = Find("SwitchCage");
            Component core = Find("PowerCore");

            foreach (Component prop in new[] { plate, sw, cage, core })
            {
                Transform model = prop.transform.Find("Model");
                Assert.IsNotNull(model, prop.name + " carries a Model child");
                string part = prop.GetType().Name == "SwitchCage" ? "field" : "body";
                Renderer tinted = Field(prop, part);
                Assert.IsNotNull(tinted, prop.name + " has its " + part + " renderer set");
                Assert.IsTrue(tinted.transform.IsChildOf(model), prop.name + " tints a part of the model");
                Assert.IsTrue(tinted.enabled, prop.name + " tints a visible part");
                var own = prop.GetComponent<MeshRenderer>();
                Assert.IsTrue(own == null || !own.enabled, prop.name + "'s own primitive is hidden");
            }

            // Every crate looks as wide as it pushes: the model is not smaller than its collider.
            foreach (string crateName in new[] { "Crate_1", "Crate_2", "Crate_3", "PaintCrate_1", "PaintCrate_2", "PaintCrate_3" })
            {
                GameObject crate = GameObject.Find(crateName);
                Assert.IsNotNull(crate, crateName);
                Bounds shown = default;
                bool first = true;
                foreach (Renderer each in crate.GetComponentsInChildren<Renderer>())
                {
                    if (first) { shown = each.bounds; first = false; }
                    else shown.Encapsulate(each.bounds);
                }
                float pushes = crate.GetComponent<Collider>().bounds.size.x;
                Assert.AreEqual(pushes, shown.size.x, 0.1f, crateName + " is drawn as wide as its collider");
            }

            // The plate is red until a crate rests on it, and the red is on the model's top.
            var block = new MaterialPropertyBlock();
            Field(plate, "body").GetPropertyBlock(block);
            Color waiting = block.GetColor("_BaseColor");
            Assert.Greater(waiting.r, 0.8f, "waiting plate is red");
            Assert.Less(waiting.g, 0.4f, "waiting plate is red");
        }
    }
}
