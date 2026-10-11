using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ToyFactory.AI.Core;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.Effects;
using ToyFactory.Runtime.Pooling;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace ToyFactory.Tests
{
    /// <summary>
    /// GV evidence for Docs/OptimisationLog.md: three numbers for the three techniques S2 owns.
    /// Run each on its own (category Evidence, explicit). The tables go to the test output.
    /// <list type="bullet">
    /// <item>Pooling shot bolts and impact bursts against creating and destroying them.</item>
    /// <item>The pressure plate's non-allocating, throttled overlap check over 20 deliveries.</item>
    /// <item>What the physics step costs in the real level.</item>
    /// </list>
    /// </summary>
    [Category("Evidence")]
    public sealed class PropOptimisationEvidenceTests
    {
        const int Cycles = 300;
        const string BoltPath = "Assets/_Project/Prefabs/FX/ShotBolt.prefab";
        const string BurstPath = "Assets/_Project/Prefabs/FX/ImpactBurst.prefab";

        readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
                if (created != null)
                    Object.Destroy(created);
            _created.Clear();
        }

        static long Allocated => GC.GetAllocatedBytesForCurrentThread();

        // ---- 1. Pool against Instantiate / Destroy ------------------------------------

        [UnityTest]
        public IEnumerator Evidence_PoolAgainstInstantiate()
        {
#if UNITY_EDITOR
            var bolt = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(BoltPath).GetComponent<ShotBolt>();
            var burst = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(BurstPath).GetComponent<ImpactBurst>();
            Assert.IsNotNull(bolt);
            Assert.IsNotNull(burst);

            var root = new GameObject("PoolEvidence");
            _created.Add(root);

            // Warm both paths once so the first-use costs are in neither number.
            Object.Destroy(Object.Instantiate(bolt, root.transform).gameObject);
            yield return null;

            // Create and destroy: what firing would cost without a pool.
            GC.Collect();
            long bytes = Allocated;
            long native = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            int collections = GC.CollectionCount(0);
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < Cycles; i++)
            {
                ShotBolt b = Object.Instantiate(bolt, root.transform);
                ImpactBurst s = Object.Instantiate(burst, root.transform);
                Object.Destroy(b.gameObject);
                Object.Destroy(s.gameObject);
            }
            watch.Stop();
            long instantiateBytes = Allocated - bytes;
            // Destroy is deferred to the end of the frame, so every copy is still alive here.
            long instantiateNative = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() - native;
            int instantiateCollections = GC.CollectionCount(0) - collections;
            double instantiateMs = watch.Elapsed.TotalMilliseconds;
            yield return null;

            // The pools the blaster uses: Get and Release only.
            var bolts = new ObjectPool<ShotBolt>(bolt, root.transform);
            var bursts = new ObjectPool<ImpactBurst>(burst, root.transform);
            bolts.Prewarm(12);
            bursts.Prewarm(8);
            yield return null;

            GC.Collect();
            bytes = Allocated;
            native = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            collections = GC.CollectionCount(0);
            watch.Restart();
            for (int i = 0; i < Cycles; i++)
            {
                ShotBolt b = bolts.Get();
                ImpactBurst s = bursts.Get();
                bolts.Release(b);
                bursts.Release(s);
            }
            watch.Stop();
            long poolBytes = Allocated - bytes;
            long poolNative = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() - native;
            int poolCollections = GC.CollectionCount(0) - collections;
            double poolMs = watch.Elapsed.TotalMilliseconds;

            var report = new StringBuilder();
            report.AppendLine($"Pool against Instantiate/Destroy ({Cycles} bolt + burst pairs, one frame)");
            report.AppendLine("| Method | Managed bytes allocated | Gen-0 collections | Native memory held at the end of the loop (bytes) | Time (ms) | Copies created |");
            report.AppendLine("| --- | --- | --- | --- | --- | --- |");
            report.AppendLine($"| Instantiate and Destroy | {instantiateBytes:N0} | {instantiateCollections} | {instantiateNative:N0} | {instantiateMs:0.00} | {Cycles * 2} |");
            report.AppendLine($"| ObjectPool Get and Release | {poolBytes:N0} | {poolCollections} | {poolNative:N0} | {poolMs:0.00} | {bolts.Created + bursts.Created} |");
            Debug.Log(report.ToString());
            TestContext.Out.WriteLine(report.ToString());

            // Instantiate allocates in native memory, not the managed heap, so the managed columns are
            // expected to read 0 for both. What the pool saves is the copies and the time.
            Assert.LessOrEqual(poolBytes, instantiateBytes, "The pool never allocates more than create and destroy.");
            Assert.Less(poolMs, instantiateMs, "Get and Release is faster than Instantiate and Destroy.");
            Assert.LessOrEqual(bolts.Created, 12, "The pool made no new copies during the run.");
            Assert.LessOrEqual(poolNative, instantiateNative);
#else
            yield break;
#endif
        }

        // ---- 2. Plate latch: 20 deliveries --------------------------------------------

        [UnityTest]
        public IEnumerator Evidence_PlateLatchOverTwentyDeliveries()
        {
            const int Plates = 20;
            int pushable = LayerMask.NameToLayer("Pushable");
            Assert.GreaterOrEqual(pushable, 0, "The Pushable layer exists.");

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = new Vector3(8f, 1f, 8f);
            _created.Add(floor);

            // The prop scripts are in the default assembly, which tests cannot reference, so they
            // are added by type name. Their task events come through the shared ITask.
            Type plateType = Type.GetType("ToyFactory.Interaction.PressurePlate, Assembly-CSharp");
            Type boxType = Type.GetType("ToyFactory.Interaction.PushableBox, Assembly-CSharp");
            Assert.IsNotNull(plateType, "PressurePlate is in the game assembly.");
            Assert.IsNotNull(boxType, "PushableBox is in the game assembly.");

            int latchedCount = 0;
            var plateObjects = new List<GameObject>();
            var crates = new List<GameObject>();
            for (int i = 0; i < Plates; i++)
            {
                var position = new Vector3(-30f + i * 3f, 0f, 0f);

                var plateObject = new GameObject("Plate_" + i);
                plateObject.transform.position = position;
                plateObject.AddComponent<BoxCollider>().size = new Vector3(1f, 0.1f, 1f);
                var plate = (ITask)plateObject.AddComponent(plateType);
                plate.OnCompleted += () => latchedCount++;
                plateObjects.Add(plateObject);
                _created.Add(plateObject);

                var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                crate.name = "Crate_" + i;
                crate.layer = pushable;
                crate.transform.localScale = Vector3.one * 0.6f;
                crate.transform.position = position + new Vector3(0f, 2f + i * 0.05f, 0f);
                var body = crate.AddComponent<Rigidbody>();
                body.mass = 5f;
                crate.AddComponent(boxType);
                crates.Add(crate);
                _created.Add(crate);
            }
            yield return null;

            // Two seconds of the plates polling with a crate falling onto each, then up to 6 s
            // for them all to latch.
            long start = Allocated;
            float until = Time.time + 6f;
            int frames = 0;
            while (Time.time < until && latchedCount < Plates)
            {
                frames++;
                yield return null;
            }
            long bytes = Allocated - start;
            int latched = latchedCount;

            // The same query with the allocating call, for the same number of checks.
            int checks = Plates * Mathf.CeilToInt(Time.time - (until - 6f)) * 5;
            var mask = LayerMask.GetMask("Pushable");
            long allocatingStart = Allocated;
            for (int i = 0; i < checks; i++)
                Physics.OverlapBox(plateObjects[i % Plates].transform.position + Vector3.up * 0.5f,
                    new Vector3(0.3f, 0.4f, 0.3f), Quaternion.identity, mask, QueryTriggerInteraction.Ignore);
            long allocatingBytes = Allocated - allocatingStart;

            var buffer = new Collider[4];
            long nonAllocStart = Allocated;
            for (int i = 0; i < checks; i++)
                Physics.OverlapBoxNonAlloc(plateObjects[i % Plates].transform.position + Vector3.up * 0.5f,
                    new Vector3(0.3f, 0.4f, 0.3f), buffer, Quaternion.identity, mask, QueryTriggerInteraction.Ignore);
            long nonAllocBytes = Allocated - nonAllocStart;

            var report = new StringBuilder();
            report.AppendLine($"Pressure plate latch ({Plates} plates, one crate delivered to each, {frames} frames)");
            report.AppendLine("| Measure | Value |");
            report.AppendLine("| --- | --- |");
            report.AppendLine($"| Plates latched | {latched} of {Plates} |");
            report.AppendLine($"| Managed bytes allocated over the whole run, all 20 plates polling and latching, test loop included | {bytes:N0} |");
            report.AppendLine($"| {checks} overlap checks with `Physics.OverlapBox` (allocating): bytes | {allocatingBytes:N0} |");
            report.AppendLine($"| {checks} overlap checks with `Physics.OverlapBoxNonAlloc` (the plate's call): bytes | {nonAllocBytes:N0} |");
            Debug.Log(report.ToString());
            TestContext.Out.WriteLine(report.ToString());

            Assert.AreEqual(Plates, latched, "Every plate latched when its crate settled.");
            Assert.Less(nonAllocBytes, Math.Max(1, allocatingBytes));
            yield break;
        }

        // ---- 3. The physics step in the real level ------------------------------------

        [UnityTest]
        public IEnumerator Evidence_PhysicsSimulateInTheLevel()
        {
            SceneManager.LoadScene("Bootstrap");
            float until = Time.realtimeSinceStartup + 30f;
            while (!(ChapterManager.Current != null && ChapterManager.Current.Flow != null && ChapterManager.Current.Flow.HasBegun
                     && AgentSpawner.Instance != null && AgentSpawner.Instance.SpawnedAgents.Count > 0
                     && PlayerState.Current != null))
            {
                Assert.Less(Time.realtimeSinceStartup, until, "Timed out waiting for Bootstrap to load.");
                yield return null;
            }
            yield return new WaitForSeconds(3f);   // warm-up

            var samples = new List<double>();
            int steps = 0;
            float lastFixed = Time.fixedTime;
            using (ProfilerRecorder recorder = ProfilerRecorder.StartNew(ProfilerCategory.Physics, "Physics.Simulate", 15))
            {
                float end = Time.realtimeSinceStartup + 20f;
                while (Time.realtimeSinceStartup < end)
                {
                    yield return null;
                    if (recorder.Valid && recorder.LastValue > 0)
                        samples.Add(recorder.LastValue / 1e6);   // nanoseconds to milliseconds, per frame
                    if (Time.fixedTime != lastFixed)
                    {
                        steps += Mathf.RoundToInt((Time.fixedTime - lastFixed) / Time.fixedDeltaTime);
                        lastFixed = Time.fixedTime;
                    }
                }
            }

            if (samples.Count == 0)
                Assert.Inconclusive("The Physics.Simulate marker returned no samples in this editor.");

            samples.Sort();
            double average = samples.Average();
            double p99 = samples[Mathf.Clamp((int)Math.Ceiling(0.99 * samples.Count) - 1, 0, samples.Count - 1)];
            int rigidbodies = Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude).Length;
            int colliders = Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude).Length;

            var report = new StringBuilder();
            report.AppendLine("Physics.Simulate in the level (Bootstrap, 20 s after a 3 s warm-up, player idle)");
            report.AppendLine("| Measure | Value |");
            report.AppendLine("| --- | --- |");
            report.AppendLine($"| Frames with a physics step sampled | {samples.Count} |");
            report.AppendLine($"| Physics steps run | {steps} |");
            report.AppendLine($"| Average per frame with a step (ms) | {average:0.000} |");
            report.AppendLine($"| p99 (ms) | {p99:0.000} |");
            report.AppendLine($"| Worst (ms) | {samples[samples.Count - 1]:0.000} |");
            report.AppendLine($"| Active rigidbodies / colliders | {rigidbodies} / {colliders} |");
            Debug.Log(report.ToString());
            TestContext.Out.WriteLine(report.ToString());
        }

        [UnityTearDown]
        public IEnumerator UnloadScenes()
        {
            if (SceneManager.sceneCount > 1)
            {
                Scene empty = SceneManager.CreateScene("Empty after evidence");
                SceneManager.SetActiveScene(empty);
                for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    if (scene != empty && scene.isLoaded)
                        yield return SceneManager.UnloadSceneAsync(scene);
                }
                if (GameClock.Current is Object clock && clock == null)
                    GameClock.Publish(null);
                if (PlayerState.Current is Object player && player == null)
                    PlayerState.Publish(null);
            }
        }
    }
}
