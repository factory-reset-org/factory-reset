using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.Runtime.Visuals;

namespace ToyFactory.Tests
{
    /// <summary>
    /// The presentation scene's tools: the wireframe, the arithmetic behind a LOD group, and the showcase
    /// controller (the line-up and turntable views, the spin, the wireframe and the level hold), all on small
    /// models built here so no scene or animation clip is needed.
    /// </summary>
    public sealed class ModelShowcaseTests
    {
        GameObject _root;
        ModelShowcase _show;
        Transform _pedestal;
        Camera _lineupCamera;
        Camera _turntableCamera;
        Transform _lineupModel;
        GameObject _plain;
        GameObject _withLevels;

        // ---- The wireframe

        [Test]
        public void ACubeHasTwelveEdgesOnceTheFlatDiagonalsAreLeftOut()
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                Mesh wire = WireMesh.Build(cube.GetComponent<MeshFilter>().sharedMesh);

                Assert.AreEqual(12, WireMesh.EdgeCount(wire), "The cube's own edges: its vertices are split per face, but they are matched by position.");
                Assert.AreEqual(MeshTopology.Lines, wire.GetTopology(0));
            }
            finally
            {
                Object.DestroyImmediate(cube);
            }
        }

        [Test]
        public void KeepingTheFlatDiagonalsAddsOnePerFace()
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                Mesh wire = WireMesh.Build(cube.GetComponent<MeshFilter>().sharedMesh, hideCoplanarEdges: false);

                Assert.AreEqual(18, WireMesh.EdgeCount(wire), "12 edges and one diagonal on each of the 6 faces.");
            }
            finally
            {
                Object.DestroyImmediate(cube);
            }
        }

        [Test]
        public void AFlatQuadOfTwoTrianglesIsFourEdges()
        {
            var quad = new Mesh
            {
                vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(1f, 1f, 0f), new Vector3(0f, 1f, 0f) },
                triangles = new[] { 0, 2, 1, 0, 3, 2 }
            };

            Assert.AreEqual(4, WireMesh.EdgeCount(WireMesh.Build(quad)));
            Object.DestroyImmediate(quad);
        }

        [Test]
        public void AnEdgeOnTheOutlineOfASingleTriangleIsKept()
        {
            var triangle = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 }
            };

            Assert.AreEqual(3, WireMesh.EdgeCount(WireMesh.Build(triangle)));
            Object.DestroyImmediate(triangle);
        }

        [Test]
        public void NothingToDrawGivesNoMesh()
        {
            Assert.IsNull(WireMesh.Build(null));
            Assert.IsNull(WireMesh.Build(new Mesh()));
        }

        // ---- The LOD arithmetic

        [Test]
        public void ScreenHeightFallsWithDistanceAndRisesWithSize()
        {
            float near = LodMath.ScreenRelativeHeight(2f, 5f, 60f);
            float far = LodMath.ScreenRelativeHeight(2f, 20f, 60f);
            float bigger = LodMath.ScreenRelativeHeight(4f, 20f, 60f);

            Assert.AreEqual(4f * far, near, 0.0001f, "Four times the distance, a quarter of the height.");
            Assert.AreEqual(2f * far, bigger, 0.0001f);
            Assert.AreEqual(2f / (2f * 10f * Mathf.Tan(Mathf.Deg2Rad * 30f)), LodMath.ScreenRelativeHeight(2f, 10f, 60f), 0.0001f);
        }

        [TestCase(0.50f, 0)]
        [TestCase(0.20f, 0)]
        [TestCase(0.19f, 1)]
        [TestCase(0.03f, 1)]
        [TestCase(0.029f, LodMath.Culled)]
        [TestCase(0f, LodMath.Culled)]
        public void ALevelIsChosenByTheHeightItsGroupCovers(float height, int expected)
        {
            Assert.AreEqual(expected, LodMath.ActiveLevel(height, new[] { 0.20f, 0.03f }));
        }

        [Test]
        public void AnOrthographicCameraIgnoresDistance()
        {
            Assert.AreEqual(0.25f, LodMath.ScreenRelativeHeightOrthographic(1.65f, 3.3f), 0.0001f);
        }

        // ---- The showcase controller

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Showcase test");
            _root.SetActive(false);

            _lineupCamera = new GameObject("Lineup camera").AddComponent<Camera>();
            _lineupCamera.transform.SetParent(_root.transform);
            _turntableCamera = new GameObject("Turntable camera").AddComponent<Camera>();
            _turntableCamera.transform.SetParent(_root.transform);
            _turntableCamera.fieldOfView = 30f;
            _pedestal = new GameObject("Pedestal").transform;
            _pedestal.SetParent(_root.transform);
            _pedestal.position = new Vector3(0f, 2f, -60f);

            GameObject lineup = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lineup.name = "Lineup cube";
            lineup.transform.SetParent(_root.transform);
            lineup.transform.position = new Vector3(3f, 0.5f, 0f);
            _lineupModel = lineup.transform;

            _plain = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _plain.name = "Plain";
            _plain.transform.localScale = new Vector3(1f, 2f, 1f);
            _plain.transform.SetParent(_root.transform);

            // A model with two levels: the cube, and a plane for the far level.
            _withLevels = new GameObject("Levels");
            GameObject near = GameObject.CreatePrimitive(PrimitiveType.Cube);
            near.name = "Near";
            near.transform.SetParent(_withLevels.transform, false);
            GameObject far = GameObject.CreatePrimitive(PrimitiveType.Quad);
            far.name = "Far";
            far.transform.SetParent(_withLevels.transform, false);
            LODGroup group = _withLevels.AddComponent<LODGroup>();
            group.SetLODs(new[]
            {
                new LOD(0.20f, new[] { near.GetComponent<Renderer>() }),
                new LOD(0.03f, new[] { far.GetComponent<Renderer>() })
            });
            group.RecalculateBounds();
            _withLevels.transform.SetParent(_root.transform);

            _show = _root.AddComponent<ModelShowcase>();
            Set("entries", new[]
            {
                new ModelShowcase.Entry { label = "Plain", prefab = _plain },
                new ModelShowcase.Entry { label = "Levels", prefab = _withLevels }
            });
            Set("lineup", new[] { _lineupModel });
            Set("lineupCamera", _lineupCamera);
            Set("turntableCamera", _turntableCamera);
            Set("pedestal", _pedestal);
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_root);
        }

        void Set(string field, object value) =>
            typeof(ModelShowcase).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_show, value);

        IEnumerator Begin()
        {
            _root.SetActive(true);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ItStartsOnTheLineupAtNeutralRotationWithNothingSpinning()
        {
            yield return Begin();

            Assert.AreEqual(ModelShowcase.View.Lineup, _show.CurrentView);
            Assert.IsTrue(_lineupCamera.gameObject.activeSelf);
            Assert.IsFalse(_turntableCamera.gameObject.activeSelf);
            Assert.IsFalse(_show.Spin);
            Assert.IsFalse(_show.Wireframe);
            Assert.AreEqual(Quaternion.identity, _lineupModel.localRotation);
        }

        [UnityTest]
        public IEnumerator SpinningTheLineupTurnsItAndStoppingPutsItBackAtNeutral()
        {
            yield return Begin();

            _show.ToggleSpin();
            _show.Advance(1f);
            Assert.AreNotEqual(Quaternion.identity, _lineupModel.localRotation);
            Assert.Greater(Quaternion.Angle(Quaternion.identity, _lineupModel.localRotation), 10f);

            _show.ToggleSpin();
            Assert.AreEqual(Quaternion.identity, _lineupModel.localRotation, "Stopped: the line-up matches the design document again.");
        }

        [UnityTest]
        public IEnumerator TheTurntableShowsOneModelAndHidesTheLineup()
        {
            yield return Begin();

            _show.ShowTurntable(0);
            yield return null;

            Assert.AreEqual(ModelShowcase.View.Turntable, _show.CurrentView);
            Assert.IsNotNull(_show.Current);
            Assert.AreEqual("Plain", _show.Current.name);
            Assert.IsFalse(_lineupModel.gameObject.activeSelf);
            Assert.IsFalse(_lineupCamera.gameObject.activeSelf);
            Assert.IsTrue(_turntableCamera.gameObject.activeSelf);
            Assert.IsTrue(_show.Spin, "The turntable turns from the start.");

            _show.ShowLineup();
            yield return null;
            Assert.IsNull(_show.Current);
            Assert.IsTrue(_lineupModel.gameObject.activeSelf);
            Assert.IsTrue(_lineupCamera.gameObject.activeSelf);
            Assert.AreEqual(0, _pedestal.childCount, "The turntable model is gone.");
        }

        [UnityTest]
        public IEnumerator TheModelStandsOnThePedestalAndTheCameraLooksAtIt()
        {
            yield return Begin();

            _show.ShowTurntable(0);
            yield return null;

            Bounds bounds = _show.Current.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
            Assert.AreEqual(_pedestal.position.y, bounds.min.y, 0.001f, "The lowest point sits on the pedestal.");

            Vector3 toModel = (bounds.center - _turntableCamera.transform.position).normalized;
            Assert.Greater(Vector3.Dot(_turntableCamera.transform.forward, toModel), 0.99f, "The camera looks at the model.");
            Assert.Greater(_turntableCamera.transform.position.z, bounds.max.z, "From the +Z side, where the models face.");
        }

        [UnityTest]
        public IEnumerator PickingPastTheEndsWrapsRound()
        {
            yield return Begin();

            _show.ShowTurntable(2);
            Assert.AreEqual(0, _show.Index);
            _show.ShowTurntable(-1);
            Assert.AreEqual(1, _show.Index);
            Assert.AreEqual(2, _show.EntryCount);
        }

        [UnityTest]
        public IEnumerator TheWireframeHidesTheSolidMeshAndShowsLinesThenGivesItBack()
        {
            yield return Begin();
            _show.ShowTurntable(0);
            yield return null;

            _show.SetWireframe(true);
            MeshRenderer solid = _show.Current.GetComponentsInChildren<MeshRenderer>().First(r => !WireframeView.IsWire(r));
            MeshRenderer wire = _show.Current.GetComponentsInChildren<MeshRenderer>().First(WireframeView.IsWire);
            Assert.IsTrue(solid.forceRenderingOff);
            Assert.IsTrue(wire.enabled);
            Assert.AreEqual(MeshTopology.Lines, wire.GetComponent<MeshFilter>().sharedMesh.GetTopology(0));

            _show.SetWireframe(false);
            Assert.IsFalse(solid.forceRenderingOff);
            Assert.IsFalse(wire.enabled);
        }

        [UnityTest]
        public IEnumerator TheWireframeFollowsToTheNextModelAndToTheLineup()
        {
            yield return Begin();
            _show.SetWireframe(true);
            Assert.IsTrue(_lineupModel.GetComponent<WireframeView>().IsOn, "The line-up draws wires too.");

            _show.ShowTurntable(1);
            yield return null;
            Assert.IsTrue(_show.Current.GetComponent<WireframeView>().IsOn, "The next model comes up as a wireframe.");

            _show.SetWireframe(false);
            _show.ShowLineup();
            Assert.IsFalse(_lineupModel.GetComponent<WireframeView>().IsOn);
        }

        [UnityTest]
        public IEnumerator TheTriangleCountIgnoresTheWireframeLines()
        {
            yield return Begin();
            _show.ShowTurntable(0);
            yield return null;
            int solid = _show.CurrentTriangles;

            _show.SetWireframe(true);

            Assert.AreEqual(12, solid, "A cube has 12 triangles.");
            Assert.AreEqual(solid, _show.CurrentTriangles);
            Assert.AreEqual(solid, ModelShowcase.CountTriangles(_show.Current.GetComponentsInChildren<MeshFilter>(true)));
        }

        [UnityTest]
        public IEnumerator TheLevelHoldCyclesAutomaticEachLevelThenCulled()
        {
            yield return Begin();
            _show.ShowTurntable(1);
            yield return null;

            Assert.AreEqual(2, _show.LevelCount);
            CollectionAssert.AreEqual(new[] { 12, 2 }, _show.LevelTriangles.ToArray(), "The cube, then the quad's two triangles.");
            Assert.AreEqual(-1, _show.ForcedLevel);

            _show.CycleLevel();
            Assert.AreEqual(0, _show.ForcedLevel);
            Assert.AreEqual(0, _show.ShownLevel());
            Assert.AreEqual(12, _show.CurrentTriangles);

            _show.CycleLevel();
            Assert.AreEqual(1, _show.ShownLevel());
            Assert.AreEqual(2, _show.CurrentTriangles);

            _show.CycleLevel();
            Assert.AreEqual(LodMath.Culled, _show.ShownLevel());
            Assert.AreEqual(0, _show.CurrentTriangles);

            _show.CycleLevel();
            Assert.AreEqual(-1, _show.ForcedLevel);
        }

        [UnityTest]
        public IEnumerator AModelWithoutLevelsIgnoresTheLevelKey()
        {
            yield return Begin();
            _show.ShowTurntable(0);
            yield return null;

            _show.CycleLevel();

            Assert.AreEqual(0, _show.LevelCount);
            Assert.AreEqual(-1, _show.ForcedLevel);
            Assert.AreEqual(0, _show.ShownLevel());
        }

        [UnityTest]
        public IEnumerator MovingTheCameraBackMakesTheAutomaticLevelDrop()
        {
            yield return Begin();
            _show.ShowTurntable(1);
            yield return null;
            Assert.AreEqual(0, _show.ShownLevel(), "Close up: the near level.");

            _show.Zoom(12f);
            Assert.AreEqual(1, _show.ShownLevel(), "Far away: the far level.");

            _show.Zoom(1000f);
            Assert.AreEqual(1, _show.ShownLevel(), "Zoom stops at its limit.");
        }
    }
}
