using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Tests
{
    /// <summary>
    /// The ending's staging on the real Captain body: wherever the fight left it, the Captain
    /// is on its spawn facing the console when the ending starts, and no other cutscene moves it.
    /// </summary>
    public sealed class AgentCutsceneMarkTests
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

        [UnityTest]
        public IEnumerator TheEndingPutsTheCaptainOnItsMarkFacingTheConsole()
        {
            AgentBodyFixture.Floor(_created);
            AgentController captain = AgentBodyFixture.Body("CaptainBot", AgentType.Captain, new StateNameBrain(), _created,
                new Vector3(3f, 0f, 2f));
            var mark = captain.GetComponent<AgentCutsceneMark>();
            Assert.IsNotNull(mark, "The Captain's body has its ending mark.");
            yield return null;

            CutsceneEvents.RaiseCutsceneStarted("ch3");
            yield return null;
            Assert.IsFalse(mark.OnMark, "Other cutscenes leave it where it is.");
            Assert.Less(Vector3.Distance(captain.transform.position, new Vector3(3f, 0f, 2f)), 0.3f);

            CutsceneEvents.RaiseCutsceneStarted("ending");
            yield return null;
            Assert.IsTrue(mark.OnMark);
            Vector3 flat = captain.transform.position;
            flat.y = 0f;
            Assert.Less(Vector3.Distance(flat, new Vector3(10.5f, 0f, 36f)), 0.05f, "On its spawn, north of the console.");
            Vector3 toConsole = new Vector3(10.5f, 0f, 31f) - flat;
            Assert.Less(Vector3.Angle(captain.transform.forward, toConsole), 2f, "Facing the console.");
            CutsceneEvents.RaiseCutsceneEnded("ending");
        }
    }
}
