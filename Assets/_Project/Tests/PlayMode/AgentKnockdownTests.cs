using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.Animation;

namespace ToyFactory.Tests
{
    /// <summary>
    /// Hit effects on a small body built in code: sparks and a squash per hit, and on going
    /// down an explosion, one comic word, a tip-over, the lights going out, and getting back up
    /// with the lights flickering on; a scrapped agent sinks away and is switched off.
    /// </summary>
    public sealed class AgentKnockdownTests
    {
        readonly List<Object> _created = new List<Object>();

        sealed class IdleBrain : IAgentBrain
        {
            public AgentIntent Tick(in AgentContext ctx) => new AgentIntent { DebugState = "Patrol" };
            public void OnGraphChanged(IReadOnlyList<Vector2Int> changedCells) { }
            public void OnStunned(float duration) { }
            public void OnDestroyed() { }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
                if (created != null)
                    Object.Destroy(created);
            _created.Clear();
        }

        static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        static IEnumerator Seconds(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
                yield return null;
        }

        Sprite Word(string name)
        {
            Sprite sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            sprite.name = name;
            _created.Add(sprite);
            return sprite;
        }

        // A body like the real ones: agent components on the root, a model with a body part
        // and a light part under it.
        AgentController Body(out Transform model, int hitPoints = 2, float knockOut = 1f, bool scrap = false)
        {
            var root = new GameObject("Body");
            _created.Add(root);
            root.SetActive(false);
            var capsule = root.AddComponent<CharacterController>();
            capsule.height = 1.6f;
            capsule.radius = 0.5f;
            AgentController agent = root.AddComponent<AgentController>();
            SetField(agent, "hitPoints", hitPoints);
            SetField(agent, "knockOutSeconds", knockOut);
            SetField(agent, "scrapWhenDown", scrap);

            model = new GameObject("Model").transform;
            model.SetParent(root.transform, false);
            var torso = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            torso.SetParent(model, false);
            torso.localPosition = Vector3.up * 0.8f;
            var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
            eye.name = "Eye";
            eye.SetParent(model, false);
            eye.localPosition = Vector3.up * 1.4f;
            eye.localScale = Vector3.one * 0.2f;

            AgentKnockdown knockdown = root.AddComponent<AgentKnockdown>();
            SetField(knockdown, "model", model);
            SetField(knockdown, "knockOutWord", Word("KnockOut"));
            SetField(knockdown, "scrapWord", Word("Scrapped"));
            SetField(knockdown, "scrapLingerSeconds", 0.2f);
            SetField(knockdown, "scrapSinkSeconds", 0.2f);

            var lightMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _created.Add(lightMaterial);
            AgentLights lights = root.AddComponent<AgentLights>();
            SetField(lights, "lights", new Renderer[] { eye.GetComponent<Renderer>() });
            SetField(lights, "lightMaterial", lightMaterial);

            root.SetActive(true);
            agent.Initialise(new AgentIdentity(AgentType.Guard, 0), new IdleBrain(), new WorldBlackboard());
            return agent;
        }

        static Transform Pivot(string name, Transform parent, Vector3 at)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = at;
            return pivot;
        }

        // A legged body like the Captain's: the same pivots, no meshes, no Animator.
        AgentController KneelingBody(out Transform model, out AgentKneel kneel, out Transform rig, out Transform frontHip)
        {
            var root = new GameObject("Captain");
            _created.Add(root);
            root.SetActive(false);
            var capsule = root.AddComponent<CharacterController>();
            capsule.height = 3.2f;
            capsule.radius = 0.55f;
            AgentController agent = root.AddComponent<AgentController>();
            SetField(agent, "hitPoints", 1);
            SetField(agent, "knockOutSeconds", 1f);

            model = new GameObject("Model").transform;
            model.SetParent(root.transform, false);
            rig = Pivot("CaptainBot_Root", model, Vector3.zero);
            frontHip = Pivot("Leg_L_Pivot", rig, new Vector3(-0.285f, 1.08f, 0f));
            Transform frontKnee = Pivot("Knee_L_Pivot", frontHip, new Vector3(0f, -0.44f, 0f));
            Transform backHip = Pivot("Leg_R_Pivot", rig, new Vector3(0.285f, 1.08f, 0f));
            Transform backKnee = Pivot("Knee_R_Pivot", backHip, new Vector3(0f, -0.44f, 0f));
            Transform torso = Pivot("Torso_Pivot", rig, new Vector3(0f, 1.04f, 0f));

            AgentKnockdown knockdown = root.AddComponent<AgentKnockdown>();
            SetField(knockdown, "model", model);
            SetField(knockdown, "knockOutWord", Word("KnockOut"));
            kneel = root.AddComponent<AgentKneel>();
            SetField(kneel, "root", rig);
            SetField(kneel, "frontHip", frontHip);
            SetField(kneel, "frontKnee", frontKnee);
            SetField(kneel, "frontAnkle", Pivot("Ankle_L_Pivot", frontKnee, new Vector3(0f, -0.4f, 0f)));
            SetField(kneel, "backHip", backHip);
            SetField(kneel, "backKnee", backKnee);
            SetField(kneel, "backAnkle", Pivot("Ankle_R_Pivot", backKnee, new Vector3(0f, -0.4f, 0f)));
            SetField(kneel, "torso", torso);
            SetField(kneel, "head", Pivot("Head_Pivot", torso, new Vector3(0f, 1.08f, 0f)));
            SetField(kneel, "frontArm", Pivot("CannonArm_L_Pivot", torso, new Vector3(-0.72f, 0.91f, 0f)));
            SetField(kneel, "backArm", Pivot("CannonArm_R_Pivot", torso, new Vector3(0.72f, 0.91f, 0f)));

            root.SetActive(true);
            agent.Initialise(new AgentIdentity(AgentType.Captain, 0), new IdleBrain(), new WorldBlackboard());
            return agent;
        }

        [UnityTest]
        public IEnumerator TheCaptainKneelsInsteadOfTippingThenStepsBackUp()
        {
            AgentController agent = KneelingBody(out Transform model, out AgentKneel kneel, out Transform rig, out Transform frontHip);
            AgentKnockdown knockdown = agent.GetComponent<AgentKnockdown>();

            agent.TakeHit();   // knocked out for 1 s
            yield return Seconds(0.7f);
            Assert.IsTrue(knockdown.IsDown);
            Assert.AreEqual(0f, knockdown.Tip, "Does not tip over.");
            Assert.Less(Vector3.Angle(model.up, Vector3.up), 0.5f, "The model stays upright.");
            Assert.That(kneel.Amount, Is.EqualTo(1f).Within(0.02f), "On one knee.");
            Assert.That(rig.localPosition.y, Is.EqualTo(-0.45f).Within(0.02f), "Dropped 0.45 m.");
            Assert.Less(Quaternion.Angle(frontHip.localRotation, Quaternion.Euler(-91f, 0f, 0f)), 1f, "Front leg forward.");

            yield return Seconds(0.9f);   // rebooted at 1 s; 0.6 s into the 1.2 s stand-up
            Assert.IsFalse(agent.IsDisabled);
            Assert.IsTrue(knockdown.IsDown, "Still stepping back up.");
            Assert.That(kneel.Amount, Is.InRange(0.01f, 0.99f));

            yield return Seconds(0.9f);
            Assert.IsFalse(knockdown.IsDown, "Standing.");
            Assert.AreEqual(0f, kneel.Amount);
            Assert.Less(rig.localPosition.magnitude, 1e-4f, "Back at its rest height.");
            Assert.Less(Quaternion.Angle(frontHip.localRotation, Quaternion.identity), 0.5f, "Legs back at rest.");
        }

        static string WordShown(AgentController agent) =>
            agent.transform.Find("ComicWord").GetComponent<SpriteRenderer>().sprite.name;

        [UnityTest]
        public IEnumerator AHitThrowsSparksAndSquashesButShowsNoWord()
        {
            AgentController agent = Body(out Transform model, hitPoints: 2);
            AgentKnockdown knockdown = agent.GetComponent<AgentKnockdown>();
            Vector3 rest = model.localScale;

            agent.TakeHit();
            yield return null;

            Assert.AreEqual(6, knockdown.ParticlesEmitted, "A handful of sparks.");
            Assert.IsFalse(knockdown.WordShowing, "Ordinary hits get no comic word.");
            Assert.IsFalse(knockdown.IsDown);
            Assert.AreNotEqual(rest, model.localScale, "Squashed for a moment.");

            yield return Seconds(0.3f);
            Assert.That(Vector3.Distance(rest, model.localScale), Is.LessThan(1e-4f), "And back to its own size.");
        }

        [UnityTest]
        public IEnumerator KnockOutExplodesTipsOverGoesDarkAndGetsBackUp()
        {
            AgentController agent = Body(out Transform model, hitPoints: 1, knockOut: 1f);
            AgentKnockdown knockdown = agent.GetComponent<AgentKnockdown>();
            AgentLights lights = agent.GetComponent<AgentLights>();

            agent.TakeHit();
            yield return null;
            Assert.IsTrue(knockdown.IsDown);
            Assert.Greater(knockdown.ParticlesEmitted, 20, "Sparks from the hit and the explosion.");
            Assert.IsTrue(knockdown.WordShowing);
            Assert.AreEqual("KnockOut", WordShown(agent));

            yield return Seconds(0.5f);
            Assert.That(knockdown.Tip, Is.EqualTo(1f).Within(0.02f), "Lying on its side.");
            Assert.Greater(Vector3.Angle(model.up, Vector3.up), 70f);
            Assert.AreEqual(0f, lights.Level, "Lights out.");

            yield return Seconds(1.3f);   // past the reboot, the stand-up and the flicker
            Assert.IsFalse(agent.IsDisabled);
            Assert.IsFalse(knockdown.IsDown, "Back on its feet.");
            Assert.Less(Vector3.Angle(model.up, Vector3.up), 0.5f);
            Assert.AreEqual(1f, lights.Level, "Lights back on.");
            Assert.IsFalse(knockdown.WordShowing, "The word has gone.");
        }

        [UnityTest]
        public IEnumerator ScrappedAgentShowsItsWordSinksAndIsSwitchedOff()
        {
            AgentController agent = Body(out _, hitPoints: 1, scrap: true);
            AgentLights lights = agent.GetComponent<AgentLights>();

            agent.TakeHit();
            yield return null;
            Assert.IsTrue(agent.IsDead);
            Assert.AreEqual("Scrapped", WordShown(agent));

            yield return Seconds(0.45f);
            Assert.AreEqual(0f, lights.Level, "Lights out for good.");

            yield return Seconds(0.6f);   // tip 0.4 + linger 0.2 + sink 0.2, with a margin
            Assert.IsFalse(agent.gameObject.activeSelf, "Switched off, not destroyed: the spawner still lists it.");
        }
    }
}
