using System;
using UnityEngine;
using UnityEngine.AI;
using ToyFactory.AI.Core.Grid;

namespace ToyFactory.Runtime.World
{
    /// <summary>Queries baked navigation data without moving the flat grid's cell centres.</summary>
    public sealed class NavMeshGridSampler
    {
        readonly NavMeshQueryFilter _filter;
        readonly float _sampleDistance;
        readonly float _horizontalTolerance;
        readonly float _verticalTolerance;

        /// <summary>
        /// Selects an existing agent type and allowed areas. Tolerances are in metres.
        /// Sampling is limited to half a cell; a nearby hit is not automatically support
        /// for the requested centre. Agent clearance comes from the NavMesh bake.
        /// </summary>
        public NavMeshGridSampler(int agentTypeId, int areaMask, float sampleDistance,
            float horizontalTolerance, float verticalTolerance)
        {
            bool knownAgent = false;
            for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
                knownAgent |= NavMesh.GetSettingsByIndex(i).agentTypeID == agentTypeId;
            if (!knownAgent) throw new ArgumentException("Unknown NavMesh agent type.", nameof(agentTypeId));
            if (areaMask == 0) throw new ArgumentException("At least one navigation area is required.", nameof(areaMask));
            if (!IsFinite(sampleDistance) || sampleDistance <= 0f || sampleDistance > GridGraph.CellSize * 0.5f)
                throw new ArgumentOutOfRangeException(nameof(sampleDistance));
            if (!IsFinite(horizontalTolerance) || horizontalTolerance < 0f || horizontalTolerance > sampleDistance)
                throw new ArgumentOutOfRangeException(nameof(horizontalTolerance));
            if (!IsFinite(verticalTolerance) || verticalTolerance < 0f || verticalTolerance > sampleDistance)
                throw new ArgumentOutOfRangeException(nameof(verticalTolerance));

            _filter = new NavMeshQueryFilter { agentTypeID = agentTypeId, areaMask = areaMask };
            _sampleDistance = sampleDistance;
            _horizontalTolerance = horizontalTolerance;
            _verticalTolerance = verticalTolerance;
        }

        /// <summary>True only when matching navigation data supports this flat cell centre.</summary>
        public bool IsWalkable(Vector3 centre)
        {
            if (!IsFinite(centre.x) || !IsFinite(centre.y) || !IsFinite(centre.z))
                throw new ArgumentException("Cell centre must be finite.", nameof(centre));
            if (!NavMesh.SamplePosition(centre, out NavMeshHit hit, _sampleDistance, _filter))
                return false;

            Vector3 displacement = hit.position - centre;
            return displacement.x * displacement.x + displacement.z * displacement.z <=
                _horizontalTolerance * _horizontalTolerance &&
                Mathf.Abs(displacement.y) <= _verticalTolerance;
        }

        internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
