using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Journey.Cutscenes;
using ToyFactory.Journey.Debugging;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.World;
using ToyFactory.UI;

namespace ToyFactory.Tests
{
    /// <summary>
    /// The fixes from S4's play-through of the level (18 October), each checked in the real scenes: the first
    /// steps are clear, the belts fit the crates, the doors lift, the targets are thick and tint, the final
    /// console faces the way in, the charger glows on its orb, the objective rings lie on the surface under
    /// their target, and a battery pickup scores. Prop types are in the default assembly, so reached by name.
    /// </summary>
    public sealed class LevelPropFixesTests
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

        static IEnumerator Until(Func<bool> condition, float seconds, string what)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > until)
                    Assert.Fail("Timed out waiting for " + what);
                yield return null;
            }
        }

        static IEnumerator LoadLevel()
        {
            AgentSpawner previous = AgentSpawner.Instance;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return Until(() => AgentSpawner.Instance != null && AgentSpawner.Instance != previous
                && ChapterManager.Current != null && ChapterManager.Current.Flow != null && ChapterManager.Current.Flow.HasBegun
                && PlayerState.Current != null, 60f, "the level");
        }

        static IEnumerator StartPlaying()
        {
            GameClock.Current.RequestState(GameState.Playing);
            yield return Until(() => CutsceneDirector.Current != null && CutsceneDirector.Current.IsPlaying, 10f, "the intro");
            CutsceneDirector.Current.Skip();
            yield return Until(() => GameClock.Current.State == GameState.Playing, 10f, "Playing");
        }

        static Type PropType(string name) => Type.GetType("ToyFactory.Interaction." + name + ",Assembly-CSharp");

        static Bounds DrawnBounds(Transform root)
        {
            Bounds bounds = default;
            bool first = true;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled)
                    continue;
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            return bounds;
        }

        [UnityTest]
        public IEnumerator TheFirstStepsAreClear_AndTheBeltsAreWiderThanTheCrates()
        {
            yield return LoadLevel();
            Vector3 spawn = PlayerState.Current.Position;

            foreach (string name in new[] { "Crate_1", "Crate_2", "Crate_3" })
            {
                Vector3 crate = GameObject.Find(name).transform.position;
                float apart = Vector2.Distance(new Vector2(crate.x, crate.z), new Vector2(spawn.x, spawn.z));
                Assert.Greater(apart, 3.5f, name + " is out of the player's first steps (the crate is 1.75 m wide)");
            }

            float crateWidth = GameObject.Find("Crate_1").GetComponent<BoxCollider>().bounds.size.x;
            foreach (string name in new[] { "Belt_Top", "Belt_Bottom" })
            {
                Bounds drawn = DrawnBounds(GameObject.Find(name).transform.Find("Model"));
                float width = Mathf.Min(drawn.size.x, drawn.size.z);
                Assert.AreEqual(2.2f, width, 0.05f, name + " is drawn 2.2 m wide");
                Assert.Greater(width, crateWidth + 0.3f, name + " is wider than a crate, so a crate does not overhang it");
            }
        }

        [UnityTest]
        public IEnumerator DoorsLiftToOpen_ComeDownToClose_AndWaitForWhatIsUnderThem()
        {
            yield return LoadLevel();
            yield return StartPlaying();
            PropTestLevel.RemoveAgents();

            GameObject doorObject = GameObject.Find("Door_1");
            Component door = doorObject.GetComponent(PropType("Door"));
            var isOpen = door.GetType().GetProperty("IsOpen");
            Collider panel = doorObject.GetComponent<Collider>();
            Vector2 footprint = new Vector2(panel.bounds.center.x, panel.bounds.center.z);
            Assert.IsTrue((bool)isOpen.GetValue(door), "door 1 starts open");
            Assert.Greater(panel.bounds.min.y, 4.9f, "an open door is lifted clear of the 5 m doorway");

            door.GetType().GetMethod("Close").Invoke(door, null);
            Assert.IsFalse((bool)isOpen.GetValue(door), "it is not open from the moment it starts to close");
            yield return PropTestLevel.UntilGameTime(() => panel.bounds.min.y < 0.05f, 6f, "the door to come down");
            Assert.AreEqual(footprint.x, panel.bounds.center.x, 0.01f, "it came straight down, not sideways");
            Assert.AreEqual(footprint.y, panel.bounds.center.z, 0.01f);

            door.GetType().GetMethod("Open").Invoke(door, null);
            yield return null;
            Assert.IsFalse((bool)isOpen.GetValue(door), "it counts as open only once fully up");
            yield return PropTestLevel.UntilGameTime(() => (bool)isOpen.GetValue(door), 6f, "the door to lift open");
            Assert.Greater(panel.bounds.min.y, 4.9f);

            // The player stands in the doorway: the door comes down to just above them and waits.
            var controller = ((Component)PlayerState.Current).GetComponent<CharacterController>();
            controller.enabled = false;
            controller.transform.position = new Vector3(panel.bounds.center.x, 0.05f, panel.bounds.center.z);
            controller.enabled = true;
            Physics.SyncTransforms();
            yield return null;
            door.GetType().GetMethod("Close").Invoke(door, null);
            float waitUntil = Time.time + 3f;
            yield return PropTestLevel.UntilGameTime(() => Time.time > waitUntil, 5f, "three seconds");
            Assert.Greater(panel.bounds.min.y, 1.8f, "the door waited over the player instead of coming down on them");
        }

        [UnityTest]
        public IEnumerator SpinningTargetsAreThick_AndAHitTintsThemGreenKeepingTheirRings()
        {
            yield return LoadLevel();
            yield return StartPlaying();
            ChapterJump jump = UnityEngine.Object.FindAnyObjectByType<ChapterJump>();
            jump.JumpTo(2);
            yield return Until(() => !jump.IsJumping, 30f, "the jump to chapter 2");

            GameObject targetObject = GameObject.Find("Target_1");
            Bounds drawn = DrawnBounds(targetObject.transform.Find("Model"));
            float thickness = Mathf.Min(drawn.size.x, Mathf.Min(drawn.size.y, drawn.size.z));
            Assert.GreaterOrEqual(thickness, 0.28f, "the target has its thickness back");

            Component target = targetObject.GetComponent(PropType("SpinningTarget"));
            var body = (Renderer)target.GetType().GetField("body", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
            target.GetType().GetMethod("TakeHit").Invoke(target, null);
            Assert.IsTrue((bool)target.GetType().GetProperty("IsHit").GetValue(target), "the hit landed");

            var block = new MaterialPropertyBlock();
            var green = new Color(0.24f, 0.86f, 0.69f);
            Material[] materials = body.sharedMaterials;
            Assert.Greater(materials.Length, 1, "the face has several coloured rings");
            Color firstShown = default;
            bool allSame = true;
            for (int i = 0; i < materials.Length; i++)
            {
                body.GetPropertyBlock(block, i);
                Color shown = block.GetColor("_BaseColor");
                Color own = materials[i].GetColor("_BaseColor");
                Assert.Less(ColourDistance(shown, green), ColourDistance(own, green), materials[i].name + " moved towards green");
                Assert.Greater(ColourDistance(shown, green), 0.05f, materials[i].name + " is tinted, not painted green");
                if (i == 0)
                    firstShown = shown;
                else if (ColourDistance(shown, firstShown) > 0.02f)
                    allSame = false;
            }
            Assert.IsFalse(allSame, "the rings still differ from each other: the target did not go completely green");
        }

        static float ColourDistance(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);

        [UnityTest]
        public IEnumerator TheFinalConsoleFacesTheWayIn_AndTheChargerGlowsOnItsOrb()
        {
            yield return LoadLevel();

            Component console = UnityEngine.Object.FindAnyObjectByType(PropType("HoldTask")) as Component;
            foreach (Component each in UnityEngine.Object.FindObjectsByType(PropType("HoldTask"), FindObjectsSortMode.None))
            {
                if (each.name == "Console")
                    console = each;
            }
            Assert.AreEqual("Console", console.name);
            Vector3 door4 = GameObject.Find("Door_4").transform.position;
            Vector3 toDoor = door4 - console.transform.position;
            toDoor.y = 0f;
            Assert.Greater(Vector3.Dot(console.transform.forward, toDoor.normalized), 0.9f, "the console faces door 4, where the player comes in");

            // Its prompt is on the new front: a look from the front at chest height finds it.
            Vector3 eye = console.transform.position + console.transform.forward * 1.6f + Vector3.up * 1.1f;
            Assert.IsTrue(Physics.Raycast(eye, (console.transform.position + Vector3.up * 0.3f - eye).normalized, out RaycastHit hit, 3f,
                ~0, QueryTriggerInteraction.Ignore), "something is in front of the player");
            Assert.AreEqual(console.transform, hit.collider.transform, "and it is the console");
            Assert.AreEqual("work it", (string)console.GetType().GetProperty("PromptText").GetValue(console), "its prompt");

            Component charger = UnityEngine.Object.FindAnyObjectByType(PropType("Charger")) as Component;
            yield return null;
            yield return null;
            // The glow component sits on the prop; its quad is a separate object placed at the prop plus the offset.
            Type glowType = Type.GetType("ToyFactory.Runtime.Effects.PropGlow, ToyFactory.Runtime");
            Component glowOnCharger = charger.GetComponent(glowType);
            Assert.IsNotNull(glowOnCharger, "the charger has a glow");
            Transform nearest = (Transform)glowType.GetProperty("Quad").GetValue(glowOnCharger);
            Assert.IsNotNull(nearest, "its quad is built");
            float top = DrawnBounds(charger.transform).max.y;
            Assert.AreEqual(top - 0.12f, nearest.position.y, 0.1f, "the glow sits on the orb at the top of the charger, not above it");
        }

        [UnityTest]
        public IEnumerator ObjectiveRingsLieOnTheSurfaceUnderTheirTarget()
        {
            yield return LoadLevel();
            yield return StartPlaying();
            PropTestLevel.RemoveAgents();

            // Floor task: the conveyor lever's ring stays on the floor.
            Component lever = UnityEngine.Object.FindAnyObjectByType(PropType("ConveyorLever")) as Component;
            Assert.AreEqual(RingPlacement.Lift, RingPlacement.OnSurfaceBelow(lever.transform.position).y, 0.03f,
                "a floor task's ring is on the floor");

            // The yellow beacon follows the rule, and the Captain's lookup gives the target's real position.
            ObjectiveBeacon beacon = UnityEngine.Object.FindAnyObjectByType<ObjectiveBeacon>();
            Assert.IsNotNull(beacon, "the objective beacon");
            yield return Until(() => ChapterManager.Current.CurrentBeaconTarget.HasValue, 5f, "a beacon target");
            yield return null;
            yield return null;
            BeaconTarget current = ChapterManager.Current.CurrentBeaconTarget.Value;
            Assert.AreEqual(RingPlacement.OnSurfaceBelow(current.Position).y, beacon.transform.position.y, 0.02f,
                "the yellow ring is at the same height the Captain's ring would use");
            Assert.IsTrue(ObjectiveTargetWriter.TryGetWorldPosition(current.Id, out Vector3 real), "the writer knows the target's real position");
            Assert.Less(Vector3.Distance(real, current.Position), 0.01f);

            // Chapter 4: the power cores float over their racks, so their rings lie on the rack tops.
            ChapterJump jump = UnityEngine.Object.FindAnyObjectByType<ChapterJump>();
            jump.JumpTo(4);
            yield return Until(() => !jump.IsJumping, 30f, "the jump to chapter 4");
            int cores = 0;
            foreach (Component core in UnityEngine.Object.FindObjectsByType(PropType("PowerCore"), FindObjectsSortMode.None))
            {
                cores++;
                Vector3 ring = RingPlacement.OnSurfaceBelow(core.transform.position);
                Assert.Greater(core.transform.position.y, 3f, core.name + " floats high");
                Assert.AreEqual(2.85f, ring.y, 0.1f, core.name + "'s ring is on its rack top, not on the floor inside the rack");
            }
            Assert.AreEqual(3, cores);

            yield return Until(() => ChapterManager.Current.CurrentBeaconTarget.HasValue, 5f, "a chapter 4 beacon target");
            yield return null;
            yield return null;
            current = ChapterManager.Current.CurrentBeaconTarget.Value;
            Assert.AreEqual(RingPlacement.OnSurfaceBelow(current.Position).y, beacon.transform.position.y, 0.02f,
                "in chapter 4 the yellow ring follows the same rule");
        }

        [UnityTest]
        public IEnumerator ABatteryPickupScoresFiftyPoints()
        {
            yield return LoadLevel();
            yield return StartPlaying();
            PropTestLevel.RemoveAgents();
            yield return Until(() => ScoreManager.Current != null, 5f, "the score");

            Type pickupType = PropType("BatteryPickup");
            Component pickup = UnityEngine.Object.FindAnyObjectByType(pickupType) as Component;
            Assert.IsNotNull(pickup, "a battery pickup in the level");
            var battery = ((Component)PlayerState.Current).GetComponent(Type.GetType("ToyFactory.Player.PlayerBattery, Assembly-CSharp"));
            int before = ScoreManager.Current.Score;
            bool taken = (bool)pickupType.GetMethod("Collect", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(pickup, new object[] { battery });
            Assert.IsTrue(taken, "the player had room for a spare cell");
            Assert.GreaterOrEqual(ScoreManager.Current.Score - before, 50, "a battery pickup scores 50");
        }
    }
}
