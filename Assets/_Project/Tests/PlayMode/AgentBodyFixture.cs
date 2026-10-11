using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Tests
{
    /// <summary>
    /// Stands in for the Animator on a real body: writes every clip-keyed pivot back to its
    /// rest pose each frame, as the clips do, so what is layered on top in LateUpdate never
    /// builds up and "back to rest" is exact. The wheels, the treads and the key are turned in
    /// code, not by clips, so they are left alone.
    /// </summary>
    public sealed class RestPoseClip : MonoBehaviour
    {
        readonly List<(Transform pivot, Vector3 position, Quaternion rotation)> _rest =
            new List<(Transform, Vector3, Quaternion)>();

        public void Capture(Transform model)
        {
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (n.EndsWith("_Pivot") && !n.StartsWith("Wheel") && !n.StartsWith("Tread") && !n.StartsWith("Windup"))
                    _rest.Add((t, t.localPosition, t.localRotation));
            }
        }

        /// <summary>Largest angle (degrees) and offset (m) of any keyed pivot from its rest pose.</summary>
        public (float degrees, float metres) Deviation()
        {
            float degrees = 0f, metres = 0f;
            foreach ((Transform pivot, Vector3 position, Quaternion rotation) in _rest)
            {
                degrees = Mathf.Max(degrees, Quaternion.Angle(pivot.localRotation, rotation));
                metres = Mathf.Max(metres, Vector3.Distance(pivot.localPosition, position));
            }
            return (degrees, metres);
        }

        void Update()
        {
            foreach ((Transform pivot, Vector3 position, Quaternion rotation) in _rest)
                pivot.SetLocalPositionAndRotation(position, rotation);
        }
    }

    /// <summary>A brain that stands still and reports the state name it is given.</summary>
    public sealed class StateNameBrain : IAgentBrain
    {
        public string State = "Patrol";
        public AgentIntent Tick(in AgentContext ctx) => new AgentIntent { DebugState = State };
        public void OnGraphChanged(IReadOnlyList<Vector2Int> changedCells) { }
        public void OnStunned(float duration) { }
        public void OnDestroyed() { }
    }

    /// <summary>The four agent bodies from their prefabs, on a floor, with the Animator replaced by <see cref="RestPoseClip"/>.</summary>
    public static class AgentBodyFixture
    {
        public static GameObject Floor(List<Object> created)
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = new Vector3(3f, 1f, 3f);
            created.Add(floor);
            return floor;
        }

        public static AgentController Body(string model, AgentType type, StateNameBrain brain, List<Object> created,
            Vector3 position = default)
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/Agents/Agent_{model}.prefab");
            GameObject body = Object.Instantiate(prefab, position, Quaternion.identity);
            created.Add(body);
            Transform rig = body.transform.Find(model);
            rig.GetComponentInChildren<Animator>().enabled = false;
            body.AddComponent<RestPoseClip>().Capture(rig);
            AgentController agent = body.GetComponent<AgentController>();
            agent.Initialise(new AgentIdentity(type, 0), brain, new WorldBlackboard());
            return agent;
#else
            throw new System.NotSupportedException("Loads the body prefabs from the AssetDatabase.");
#endif
        }
    }
}
