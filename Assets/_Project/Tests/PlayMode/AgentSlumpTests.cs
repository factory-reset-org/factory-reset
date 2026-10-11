using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.AI.Core;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.Animation;

namespace ToyFactory.Tests
{
    /// <summary>
    /// The powered-down slumps on the real bodies: knocked out, each robot slumps (its main
    /// pivot leans forward) with dizzy stars above its head, and after the reboot every pivot
    /// is back at rest and the stars are gone; a scrapped Saboteur slumps with no stars; and
    /// against a wall the lean shrinks so nothing pokes through.
    /// </summary>
    public sealed class AgentSlumpTests
    {
        readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
                if (created != null)
                    Object.Destroy(created);
            _created.Clear();
        }

        static WaitForEndOfFrame EndOfFrame() => new WaitForEndOfFrame();

        // The model, its type and the pivot that leans, with the lean in the open (degrees).
        static readonly object[] Robots =
        {
            new object[] { "TrackerToy", AgentType.Tracker, "TrackerToy_Root/Body_Pivot", 15f },
            new object[] { "SaboteurBot", AgentType.Saboteur, "SaboteurBot_Root/Body_Pivot", 30f },
            new object[] { "GuardBot", AgentType.Guard, "GuardBot_Root/Torso_Pivot", 25f },
        };

        // Degrees the pivot pitches forward (+X) from rest (0).
        static float Pitch(Transform pivot) => Mathf.DeltaAngle(0f, pivot.localEulerAngles.x);

        [UnityTest]
        public IEnumerator KnockedOutEachRobotSlumpsThenIsBackAtRestAfterTheReboot(
            [ValueSource(nameof(Robots))] object[] robot)
        {
            string model = (string)robot[0];
            AgentBodyFixture.Floor(_created);
            AgentController agent = AgentBodyFixture.Body(model, (AgentType)robot[1], new StateNameBrain(), _created);
            Transform pivot = agent.transform.Find(model + "/" + (string)robot[2]);
            var knockdown = agent.GetComponent<AgentKnockdown>();
            var pose = agent.GetComponent<AgentDownPose>();
            Assert.IsNotNull(pose, model + " has a down pose.");
            yield return new WaitForSeconds(0.2f);

            agent.Disable(1.5f);
            yield return new WaitForSeconds(pose.DownSeconds + 0.1f);
            yield return EndOfFrame();
            Assert.AreEqual(1f, pose.Amount, 1e-3f, "Fully down.");
            Assert.AreEqual((float)robot[3], Pitch(pivot), 1f, "Leans forward as far as it should in the open.");
            Assert.Less(Vector3.Angle(agent.transform.Find(model).up, Vector3.up), 0.1f, "Slumps where it stands: no tip-over.");
            Assert.IsTrue(knockdown.StarsShowing, "Dizzy stars while it is out.");
            Transform smoke = agent.transform.Find("HeadSmoke");
            if (model == "GuardBot")
                Assert.Greater(smoke.GetComponent<ParticleSystem>().particleCount, 0, "Smoke puffs from the Guard's head.");
            else
                Assert.IsNull(smoke, "Only the Guard smokes.");

            yield return new WaitForSeconds(1.5f - pose.DownSeconds - 0.1f + pose.StandSeconds + 0.15f);
            yield return EndOfFrame();
            Assert.IsFalse(agent.IsDisabled);
            Assert.IsFalse(knockdown.IsDown, "Back up.");
            Assert.AreEqual(0f, pose.Amount);
            Assert.IsFalse(knockdown.StarsShowing, "No stars once it is up.");
            (float degrees, float metres) = agent.GetComponent<RestPoseClip>().Deviation();
            Assert.Less(degrees, 0.01f, "Every pivot back at rest.");
            Assert.Less(metres, 1e-4f);
        }

        [UnityTest]
        public IEnumerator StarsShowOnlyWhileDownAndNeverOnAScrap()
        {
            AgentBodyFixture.Floor(_created);
            AgentController tracker = AgentBodyFixture.Body("TrackerToy", AgentType.Tracker, new StateNameBrain(), _created);
            AgentController saboteur = AgentBodyFixture.Body("SaboteurBot", AgentType.Saboteur, new StateNameBrain(), _created,
                new Vector3(3f, 0f, 0f));
            var trackerDown = tracker.GetComponent<AgentKnockdown>();
            var saboteurDown = saboteur.GetComponent<AgentKnockdown>();
            yield return new WaitForSeconds(0.2f);
            Assert.IsFalse(trackerDown.StarsShowing, "None while it is up.");

            tracker.Disable(2f);
            saboteur.Scrap();
            yield return EndOfFrame();
            Assert.IsFalse(trackerDown.StarsShowing, "Not until the explosion has cleared.");

            bool saboteurStars = false;
            float until = Time.time + 1.1f;   // the Saboteur's slump takes 0.95 s; it sinks from 2.15 s
            while (Time.time < until)
            {
                yield return EndOfFrame();
                saboteurStars |= saboteurDown.StarsShowing;
            }
            Assert.IsTrue(trackerDown.StarsShowing, "Knocked out: stars.");
            Vector3 stars = trackerDown.StarsCentre - tracker.transform.position;
            Assert.That(stars.y, Is.InRange(1.25f, 1.6f), "Above the top of the Tracker (its antenna stands 1.39 m).");
            Assert.Less(new Vector2(stars.x, stars.z).magnitude, 0.7f, "Over the body.");
            Assert.IsFalse(saboteurStars, "Scrapped: never.");
            Assert.IsTrue(saboteurDown.IsDown, "But it slumps the same way.");
            Assert.AreEqual(1f, saboteur.GetComponent<AgentDownPose>().Amount, 0.05f);
        }

        [UnityTest]
        public IEnumerator AgainstAWallTheGuardSlumpsLessAndNothingPokesThrough()
        {
            AgentBodyFixture.Floor(_created);
            // A wall whose face is 0.62 m in front: just clear of the 0.55 m capsule.
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _created.Add(wall);
            wall.transform.position = new Vector3(0f, 2f, 0.62f + 0.1f);
            wall.transform.localScale = new Vector3(4f, 4f, 0.2f);
            AgentController agent = AgentBodyFixture.Body("GuardBot", AgentType.Guard, new StateNameBrain(), _created);
            Transform torso = agent.transform.Find("GuardBot/GuardBot_Root/Torso_Pivot");
            yield return new WaitForSeconds(0.2f);

            agent.Disable(3f);
            yield return new WaitForSeconds(0.8f);
            yield return EndOfFrame();
            Assert.Less(Pitch(torso), 10f, "The slump shrinks to fit (25° in the open).");

            float front = float.NegativeInfinity;
            foreach (MeshFilter mesh in agent.GetComponentsInChildren<MeshFilter>())
                foreach (Vector3 vertex in mesh.sharedMesh.vertices)
                    front = Mathf.Max(front, mesh.transform.TransformPoint(vertex).z);
            Assert.Less(front, 0.62f + 0.03f, "Nothing pokes through the wall.");
        }
    }
}
