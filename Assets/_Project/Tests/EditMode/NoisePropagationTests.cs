using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Perception;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.Tests.EditMode
{
    public class NoisePropagationTests
    {
        const float Tolerance = 1e-3f;

        // 20 m x 10 m open floor, 0.5 m cells.
        static readonly Vector2Int Source = new Vector2Int(8, 10);

        GridGraph _grid;
        NoisePropagation _noise;

        [SetUp]
        public void SetUp()
        {
            _grid = new GridGraph(40, 20, Vector3.zero);
            _noise = new NoisePropagation(_grid);
        }

        // A wall along x = column, with a door of the given id on rows 9..11 (or no gap).
        void Wall(int column, int? doorId = null, bool closed = true)
        {
            for (int y = 0; y < _grid.Height; y++)
            {
                var cell = new Vector2Int(column, y);
                if (doorId.HasValue && y >= 9 && y <= 11)
                    _grid.SetDoor(cell, doorId, closed);
                else
                    _grid.SetWalkable(cell, false);
            }
        }

        static float Expected(float sourceLevel, float metres, int closedDoors = 0) =>
            sourceLevel - NoisePropagation.LossPerMetre * metres - NoisePropagation.ClosedDoorLoss * closedDoors;

        // ---- Open floor -----------------------------------------------------------------

        [Test]
        public void TheSourceHasTheFullLevel()
        {
            _noise.Propagate(Source, 100f);

            Assert.AreEqual(100f, _noise.Level(Source), Tolerance);
            Assert.AreEqual(Source, _noise.Source);
        }

        [Test]
        public void OpenFloorLosesFourPerMetre()
        {
            _noise.Propagate(Source, 100f);

            Assert.AreEqual(Expected(100f, 5f), _noise.Level(Source + new Vector2Int(10, 0)), Tolerance);
            Assert.AreEqual(Expected(100f, 3f * 0.5f * Mathf.Sqrt(2f)), _noise.Level(Source + new Vector2Int(3, 3)), Tolerance);
        }

        [Test]
        public void DistanceIsMeasuredAlongTheGridNotAsTheCrowFlies()
        {
            _noise.Propagate(Source, 100f);

            // 4 across and 1 up: 3 straight steps and 1 diagonal.
            float gridMetres = (3f + Mathf.Sqrt(2f)) * GridGraph.CellSize;
            Assert.AreEqual(Expected(100f, gridMetres), _noise.Level(Source + new Vector2Int(4, 1)), Tolerance);
        }

        // ---- Doors ----------------------------------------------------------------------

        [Test]
        public void AClosedDoorCostsThirtyFive()
        {
            Wall(10, doorId: 1);
            _noise.Propagate(Source, 100f);

            // 4 steps = 2 m, through the closed door.
            Assert.AreEqual(Expected(100f, 2f, closedDoors: 1), _noise.Level(new Vector2Int(12, 10)), Tolerance);
        }

        [Test]
        public void AnOpenDoorCostsNothingExtra()
        {
            Wall(10, doorId: 1, closed: false);
            _noise.Propagate(Source, 100f);

            Assert.AreEqual(Expected(100f, 2f), _noise.Level(new Vector2Int(12, 10)), Tolerance);
        }

        [Test]
        public void ADoorSeveralCellsDeepCostsThirtyFiveOnce()
        {
            Wall(10, doorId: 1);
            Wall(11, doorId: 1);
            _noise.Propagate(Source, 100f);

            Assert.AreEqual(Expected(100f, 2.5f, closedDoors: 1), _noise.Level(new Vector2Int(13, 10)), Tolerance);
        }

        [Test]
        public void EachClosedDoorCrossedCostsThirtyFive()
        {
            Wall(10, doorId: 1);
            Wall(15, doorId: 2);
            _noise.Propagate(Source, 100f);

            Assert.AreEqual(Expected(100f, 4.5f, closedDoors: 2), _noise.Level(new Vector2Int(17, 10)), Tolerance);
        }

        [Test]
        public void ADoorSlamIsHeardOnBothSidesWithoutThePenalty()
        {
            Wall(10, doorId: 1);
            var door = new Vector2Int(10, 10);
            _noise.Propagate(door, 60f);

            Assert.AreEqual(Expected(60f, 1f), _noise.Level(new Vector2Int(12, 10)), Tolerance);
            Assert.AreEqual(Expected(60f, 1f), _noise.Level(new Vector2Int(8, 10)), Tolerance);
        }

        // ---- Walls and boxes ------------------------------------------------------------

        [Test]
        public void AWallWithNoGapBlocksTheNoise()
        {
            Wall(10);
            _noise.Propagate(Source, 100f);

            Assert.AreEqual(0f, _noise.Level(new Vector2Int(12, 10)));
            Assert.IsFalse(_noise.IsHeard(new Vector2Int(12, 10)));
        }

        [Test]
        public void AListenerInACellTheNoiseSkipsHearsItsLoudestNeighbour()
        {
            var clearance = new Vector2Int(12, 10);   // e.g. an agent standing in a box's clearance cell
            _grid.SetWalkable(clearance, false);
            _noise.Propagate(Source, 100f);

            Assert.AreEqual(0f, _noise.Level(clearance));
            Assert.AreEqual(Expected(100f, 1.5f), _noise.LevelNear(clearance), Tolerance);
        }

        [Test]
        public void LevelNearDoesNotHearThroughAWall()
        {
            Wall(10);
            _noise.Propagate(Source, 100f);

            Assert.AreEqual(0f, _noise.LevelNear(new Vector2Int(11, 10)), "The wall cells were never reached either.");
        }

        [Test]
        public void BoxesBlockTheNoiseToo()
        {
            for (int y = 0; y < _grid.Height; y++)
                _grid.AddBlocker(new Vector2Int(10, y));
            _noise.Propagate(Source, 100f);

            Assert.AreEqual(0f, _noise.Level(new Vector2Int(12, 10)));
        }

        [Test]
        public void NoiseGoesRoundCornersAndLosesLevelForTheLongerPath()
        {
            for (int y = 0; y < 15; y++)   // wall with a gap at the top
                _grid.SetWalkable(new Vector2Int(10, y), false);
            var listener = new Vector2Int(12, 10);
            _noise.Propagate(Source, 100f);

            // The level matches the shortest walking route, found independently by A*.
            PathResult route = new AStarSearch(_grid).FindPath(Source, listener, BaseCostModel.Instance);
            float metres = 0f;
            for (int i = 1; i < route.Cells.Count; i++)
                metres += BaseCostModel.Instance.StepCost(route.Cells[i - 1], route.Cells[i]) * GridGraph.CellSize;

            Assert.AreEqual(Expected(100f, metres), _noise.Level(listener), Tolerance);
            Assert.Less(_noise.Level(listener), Expected(100f, 2f), "Quieter than straight through.");
        }

        // ---- Threshold and bound --------------------------------------------------------

        [Test]
        public void NoiseFadesOutAtTheHearingThreshold()
        {
            _noise.Propagate(Source, 25f);   // footsteps: 3.75 m

            Assert.AreEqual(11f, _noise.Level(Source + new Vector2Int(7, 0)), Tolerance);   // 3.5 m
            Assert.AreEqual(0f, _noise.Level(Source + new Vector2Int(8, 0)));              // 4 m would be 9
        }

        [Test]
        public void ALevelExactlyAtTheThresholdIsNotHeard()
        {
            _noise.Propagate(Source, 14f);   // one step leaves 12, two leave exactly 10

            Assert.AreEqual(12f, _noise.Level(Source + new Vector2Int(1, 0)), Tolerance);
            Assert.AreEqual(0f, _noise.Level(Source + new Vector2Int(2, 0)));
        }

        [Test]
        public void ANoiseNoLouderThanTheThresholdIsHeardNowhere()
        {
            _noise.Propagate(Source, NoisePropagation.HearingThreshold);

            Assert.AreEqual(0f, _noise.Level(Source));
            Assert.AreEqual(0, _noise.CellsReached);
        }

        [Test]
        public void OnlyCellsThatCanHearTheNoiseAreExpanded()
        {
            _noise.Propagate(Source, 25f);

            int heard = 0;
            for (int y = 0; y < _grid.Height; y++)
                for (int x = 0; x < _grid.Width; x++)
                    if (_noise.IsHeard(new Vector2Int(x, y)))
                        heard++;

            Assert.AreEqual(heard, _noise.CellsReached);
            float radiusCells = NoisePropagation.AudibleRadius(25f) / GridGraph.CellSize;
            Assert.LessOrEqual(_noise.CellsReached, Mathf.CeilToInt(Mathf.PI * radiusCells * radiusCells));
        }

        [Test]
        public void AudibleRadiusMatchesThePlan()
        {
            Assert.AreEqual(22.5f, NoisePropagation.AudibleRadius(100f), Tolerance);
            Assert.AreEqual(3.75f, NoisePropagation.AudibleRadius(25f), Tolerance);
            Assert.AreEqual(0f, NoisePropagation.AudibleRadius(5f));
        }

        // ---- Edge cases -----------------------------------------------------------------

        [Test]
        public void ASourceInsideAWallSpreadsFromTheNearestOpenCell()
        {
            Wall(10);
            _noise.Propagate(new Vector2Int(10, 10), 60f);

            Assert.AreNotEqual(new Vector2Int(10, 10), _noise.Source);
            Assert.IsTrue(_noise.IsHeard(new Vector2Int(9, 10)) || _noise.IsHeard(new Vector2Int(11, 10)));
        }

        [Test]
        public void ASourceOutsideTheGridIsHeardNowhere()
        {
            Assert.DoesNotThrow(() => _noise.Propagate(new Vector2Int(-20, -20), 100f));

            Assert.AreEqual(0f, _noise.Level(Vector2Int.zero));
            Assert.AreEqual(0f, _noise.Level(new Vector2Int(-20, -20)));
        }

        [Test]
        public void NothingIsHeardBeforeTheFirstNoise()
        {
            Assert.AreEqual(0f, _noise.Level(Source));
            Assert.IsFalse(_noise.HasPropagated);
        }

        [Test]
        public void ANewNoiseReplacesTheLastOne()
        {
            _noise.Propagate(new Vector2Int(2, 2), 100f);
            _noise.Propagate(new Vector2Int(37, 17), 25f);

            Assert.AreEqual(0f, _noise.Level(new Vector2Int(2, 2)));
            Assert.AreEqual(25f, _noise.Level(new Vector2Int(37, 17)), Tolerance);
        }

        [Test]
        public void RecordsTheGraphVersionItWasPropagatedOn()
        {
            _grid.SetWalkable(new Vector2Int(0, 0), false);
            _noise.Propagate(Source, 50f);

            Assert.AreEqual(_grid.Version, _noise.GraphVersion);
        }

        [Test]
        public void ReusedPropagationAllocatesNothing()
        {
            Wall(10, doorId: 1);
            _noise.Propagate(Source, 100f);   // warm up

            int allocated = GcAllocations.Count(() =>
            {
                for (int i = 0; i < 100; i++)
                    _noise.Propagate(Source, 100f);
            });

            Assert.AreEqual(0, allocated);
        }

        [Test]
        public void ConstructorAndBadLevelsAreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new NoisePropagation(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => _noise.Propagate(Source, float.NaN));
        }

        // ---- SensorSnapshot.Loudest -----------------------------------------------------

        [Test]
        public void TheLouderNoiseWinsAndHearingNothingAlwaysLoses()
        {
            var quiet = new SensorSnapshot(Vector3.zero, 20f, 1, 1f);
            var loud = new SensorSnapshot(Vector3.one, 60f, 2, 0.5f);

            Assert.AreEqual(2, SensorSnapshot.Loudest(quiet, loud).NoiseSourceId);
            Assert.AreEqual(2, SensorSnapshot.Loudest(loud, quiet).NoiseSourceId);
            Assert.AreEqual(1, SensorSnapshot.Loudest(default, quiet).NoiseSourceId);
            Assert.AreEqual(1, SensorSnapshot.Loudest(quiet, default).NoiseSourceId);
            Assert.IsFalse(SensorSnapshot.Loudest(default, default).HasNoise);
        }

        [Test]
        public void AnEquallyLoudNoiseKeepsTheNewerOne()
        {
            var older = new SensorSnapshot(Vector3.zero, 40f, 1, 1f);
            var newer = new SensorSnapshot(Vector3.one, 40f, 2, 2f);

            Assert.AreEqual(2, SensorSnapshot.Loudest(older, newer).NoiseSourceId);
            Assert.AreEqual(2, SensorSnapshot.Loudest(newer, older).NoiseSourceId);
        }
    }
}
