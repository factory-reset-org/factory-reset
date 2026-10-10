using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.Runtime.Effects;
using Object = UnityEngine.Object;

namespace ToyFactory.Tests
{
    public sealed class PropEffectsTests
    {
        const float Tolerance = 1e-3f;

        readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            // Immediately, so the next test does not find the last one's PropEffects still current.
            foreach (Object created in _created)
                if (created != null)
                    Object.DestroyImmediate(created);
            _created.Clear();
        }

        T Track<T>(T item) where T : Object
        {
            _created.Add(item);
            return item;
        }

        static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        static Material GlowMaterial() => new Material(Shader.Find("Sprites/Default"));

        static Sprite WordSprite(string name)
        {
            Sprite sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            sprite.name = name;
            return sprite;
        }

        // ---- PropGlow ------------------------------------------------------------------

        [Test]
        public void AGlowIsAQuadOfItsOwnThatIsNotTheProps()
        {
            GameObject prop = Track(new GameObject("Prop"));
            prop.transform.localScale = new Vector3(0.4f, 1.2f, 0.4f);

            PropGlow glow = PropGlow.Attach(prop, GlowMaterial(), Color.cyan, 1.5f, 0.6f);

            Assert.IsNotNull(glow);
            Assert.IsNotNull(glow.Quad);
            Assert.AreNotSame(prop.transform, glow.Quad.parent, "Not a child, so the prop's squashed scale cannot deform it.");
            Track(glow.Quad.gameObject);
        }

        [UnityTest]
        public IEnumerator TheGlowFollowsItsPropAndFacesTheCamera()
        {
            var cameraObject = Track(new GameObject("Camera"));
            cameraObject.tag = "MainCamera";
            cameraObject.AddComponent<Camera>();
            cameraObject.transform.rotation = Quaternion.Euler(10f, 40f, 0f);

            GameObject prop = Track(new GameObject("Prop"));
            prop.transform.position = new Vector3(3f, 1f, 5f);
            PropGlow glow = PropGlow.Attach(prop, GlowMaterial(), Color.white, 2f, 0.5f, offset: new Vector3(0f, 0.5f, 0f));
            yield return null;

            Assert.AreEqual(new Vector3(3f, 1.5f, 5f), glow.Quad.position);
            Assert.AreEqual(2f, glow.Quad.localScale.x, Tolerance);
            Assert.AreEqual(0f, Quaternion.Angle(cameraObject.transform.rotation, glow.Quad.rotation), 0.01f);

            prop.transform.position = new Vector3(0f, 0f, 0f);
            yield return null;
            Assert.AreEqual(new Vector3(0f, 0.5f, 0f), glow.Quad.position);
        }

        [UnityTest]
        public IEnumerator ZeroIntensityHidesTheGlowAndItGoesWithItsProp()
        {
            GameObject prop = Track(new GameObject("Prop"));
            PropGlow glow = PropGlow.Attach(prop, GlowMaterial(), Color.white, 1f, 0.8f);
            var renderer = glow.Quad.GetComponent<MeshRenderer>();
            yield return null;
            Assert.IsTrue(renderer.enabled);

            glow.Intensity = 0f;
            yield return null;
            Assert.IsFalse(renderer.enabled);

            glow.Intensity = 1f;
            prop.SetActive(false);
            Assert.IsFalse(glow.Quad.gameObject.activeSelf, "Hidden with its prop.");
            prop.SetActive(true);
            Assert.IsTrue(glow.Quad.gameObject.activeSelf);
        }

        [Test]
        public void AttachingTwiceKeepsOneGlowAndNoMaterialMeansNoGlow()
        {
            GameObject prop = Track(new GameObject("Prop"));
            Material material = GlowMaterial();

            PropGlow first = PropGlow.Attach(prop, material, Color.red, 1f, 0.5f);
            PropGlow second = PropGlow.Attach(prop, material, Color.blue, 2f, 0.5f);

            Assert.AreSame(first, second);
            Assert.AreEqual(Color.blue, second.Colour);
            Assert.AreEqual(1, prop.GetComponents<PropGlow>().Length);
            Assert.IsNull(PropGlow.Attach(Track(new GameObject("Other")), (Material)null, Color.white, 1f, 0.5f));
            Track(first.Quad.gameObject);
        }

        [UnityTest]
        public IEnumerator DestroyingTheGlowTakesItsQuadWithIt()
        {
            GameObject prop = Track(new GameObject("Prop"));
            PropGlow glow = PropGlow.Attach(prop, GlowMaterial(), Color.white, 1f, 0.5f);
            GameObject quad = glow.Quad.gameObject;

            Object.Destroy(prop);
            yield return null;

            Assert.IsTrue(quad == null);
        }

        // ---- ComicWord -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator AWordPopsRisesAndFadesThenFinishesOnce()
        {
            var go = Track(new GameObject("Word"));
            var word = go.AddComponent<ComicWord>();
            int finished = 0;
            word.Finished += _ => finished++;

            word.Show(WordSprite("Word_ding"), new Vector3(1f, 2f, 3f));
            Assert.IsTrue(word.IsShowing);
            Assert.AreEqual("Word_ding", word.Sprite.name);

            float until = Time.realtimeSinceStartup + 3f;
            float highest = 0f;
            while (word.IsShowing && Time.realtimeSinceStartup < until)
            {
                highest = Mathf.Max(highest, go.transform.position.y);
                yield return null;
            }
            for (int i = 0; i < 3; i++)
                yield return null;

            Assert.AreEqual(1, finished);
            Assert.Greater(highest, 2.05f, "It drifted up.");
        }

        [Test]
        public void AWordIsSizedToItsWidth()
        {
            var go = Track(new GameObject("Word"));
            var word = go.AddComponent<ComicWord>();
            Sprite sprite = WordSprite("Word_x");

            word.Show(sprite, Vector3.zero, 2f);

            // The sprite is 0.04 m wide at 100 pixels per unit; the word is 1.3 m wide at scale 1,
            // so at scale 2 it is 2.6 m when fully grown, and starts at 60% of that.
            float width = sprite.bounds.size.x * go.transform.localScale.x;
            Assert.AreEqual(2.6f * 0.6f * 1.15f, width, 0.05f);
        }

        // ---- PropEffects ---------------------------------------------------------------

        PropEffects NewEffects(ImpactBurst spark = null, ComicWord wordPrefab = null, params string[] wordKeys)
        {
            GameObject host = Track(new GameObject("PropEffects"));
            var effects = host.AddComponent<PropEffects>();
            if (spark != null)
                SetField(effects, "sparkPrefab", spark);
            if (wordPrefab != null)
                SetField(effects, "wordPrefab", wordPrefab);
            var sprites = new List<Sprite>();
            foreach (string key in wordKeys)
                sprites.Add(WordSprite("Word_" + key));
            SetField(effects, "words", sprites.ToArray());
            return effects;
        }

        [Test]
        public void TheEffectsKnowTheirWordsByTheEndOfTheSpritesName()
        {
            PropEffects effects = NewEffects(null, null, "ding", "kaboom");

            Assert.IsTrue(effects.HasWord("ding"));
            Assert.IsTrue(effects.HasWord("kaboom"));
            Assert.IsFalse(effects.HasWord("clunk"));
            Assert.IsFalse(effects.HasWord(null));
        }

        [Test]
        public void SparksAndWordsComeFromPoolsAndAreReused()
        {
            var sparkTemplate = Track(new GameObject("Spark"));
            sparkTemplate.SetActive(false);
            ImpactBurst spark = sparkTemplate.AddComponent<ImpactBurst>();
            var wordTemplate = Track(new GameObject("WordTemplate"));
            wordTemplate.SetActive(false);
            ComicWord wordPrefab = wordTemplate.AddComponent<ComicWord>();
            PropEffects effects = NewEffects(spark, wordPrefab, "ding");

            for (int i = 0; i < 20; i++)
            {
                effects.PlaySpark(Vector3.zero, Vector3.up, Color.white);
                effects.PlayWord("ding", Vector3.zero);
            }
            effects.PlayWord("nothing", Vector3.zero);

            // Twelve sparks and six words were made up front, and 20 plays only made more when
            // every one was still out, which is what happens here (nothing finishes in a frame).
            int bursts = effects.GetComponentsInChildren<ImpactBurst>(true).Length;
            int words = effects.GetComponentsInChildren<ComicWord>(true).Length;
            Assert.AreEqual(20, bursts);
            Assert.AreEqual(20, words);
        }

        [Test]
        public void WithNoPrefabsTheCallsDoNothing()
        {
            PropEffects effects = NewEffects();

            Assert.DoesNotThrow(() =>
            {
                effects.PlaySpark(Vector3.zero, Vector3.up, Color.white);
                effects.PlayExplosion(Vector3.zero, Color.white);
                effects.PlayWord("ding", Vector3.zero);
            });
            Assert.AreEqual(0, effects.transform.childCount);
        }
    }
}
