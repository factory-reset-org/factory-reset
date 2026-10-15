using UnityEngine;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.World;

namespace ToyFactory.Runtime.Debugging
{
    /// <summary>
    /// Test-scene helper for <c>Test_SquadClaims.unity</c>: publishes this object's position as the player's
    /// objective, the way the chapter system does in the real level, so the Saboteurs can work out which
    /// doors lengthen the player's route. Without an objective there is no detour and no door is worth
    /// closing. It writes once, as soon as the grid and the blackboard exist, then switches itself off.
    /// Do not use in the real level: S1's chapter manager owns the objectives there.
    /// </summary>
    public sealed class SquadScenarioObjective : MonoBehaviour
    {
        /// <summary>The target id the helper publishes (the real level's ids are small task numbers).</summary>
        public const int TargetId = 9001;

        void Update()
        {
            AgentSpawner spawner = AgentSpawner.Instance;
            if (spawner == null || spawner.Blackboard == null || GridManager.Current == null)
                return;

            if (!GridManager.Current.TryWorldToCell(transform.position, out Vector2Int cell))
            {
                Debug.LogWarning("The squad scenario's objective is outside the grid.", this);
                enabled = false;
                return;
            }

            spawner.Blackboard.SetObjectiveTargets(new[] { new ObjectiveTarget(TargetId, cell, ObjectiveTargetKind.Task) });
            enabled = false;
        }
    }
}
