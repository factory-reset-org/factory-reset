using UnityEngine;
using ToyFactory.AI.Agents.Mock;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Perception;
using ToyFactory.Runtime.Movement;

namespace ToyFactory.Runtime.Debugging
{
    /// <summary>
    /// Test-scene helper that drives an <see cref="AgentPathFollower"/> with a
    /// <see cref="MockPathProvider"/> built from scene markers, so the movement pipeline
    /// can be watched in Play mode before AgentController exists. Not used in the game.
    /// </summary>
    [RequireComponent(typeof(AgentPathFollower))]
    public sealed class MockPathRunner : MonoBehaviour
    {
        [Tooltip("Scene objects marking the route, in walking order. The last one loops back to the first.")]
        [SerializeField] Transform[] waypoints = new Transform[0];

        [Tooltip("Movement speed in metres per second.")]
        [SerializeField, Min(0f)] float speed = MockPathProvider.DefaultSpeed;

        readonly WorldBlackboard _blackboard = new WorldBlackboard();

        AgentPathFollower _follower;
        IAgentBrain _brain;

        void Start()
        {
            _follower = GetComponent<AgentPathFollower>();

            if (waypoints.Length == 0)
            {
                Debug.LogWarning($"{nameof(MockPathRunner)} on {name} has no waypoints; the agent will stay still.", this);
                enabled = false;
                return;
            }

            var positions = new Vector3[waypoints.Length];
            for (int i = 0; i < waypoints.Length; i++)
                positions[i] = waypoints[i].position;

            _brain = new MockPathProvider(positions, speed);
        }

        void Update()
        {
            // No grid yet, so the cell is left at zero; the mock brain does not use it.
            var context = new AgentContext(Vector2Int.zero, transform.position, transform.forward,
                Time.time, _blackboard, new SensorSnapshot());

            AgentIntent intent = _brain.Tick(context);

            // A null path means "keep following the current path".
            if (intent.Path != null)
                _follower.SetPath(intent.Path, intent.DesiredSpeed);
        }

        // Draws the route in the Scene view so it is visible while editing and playing.
        void OnDrawGizmos()
        {
            if (waypoints == null || waypoints.Length == 0)
                return;

            Gizmos.color = Color.cyan;
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] == null)
                    continue;

                Gizmos.DrawWireSphere(waypoints[i].position, 0.3f);

                Transform next = waypoints[(i + 1) % waypoints.Length];
                if (next != null)
                    Gizmos.DrawLine(waypoints[i].position, next.position);
            }
        }
    }
}
