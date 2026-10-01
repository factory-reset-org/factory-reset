using UnityEngine;

namespace ToyFactory.AI.Agents.Captain
{
    /// <summary>One place the Captain thinks the player might be heading for.</summary>
    public readonly struct CandidateGoal
    {
        /// <summary>Stable id, used to cache this goal's distance field while the goal exists.</summary>
        public readonly int Id;

        /// <summary>The grid cell the player has to reach.</summary>
        public readonly Vector2Int Cell;

        /// <summary>Which prior share this goal belongs to.</summary>
        public readonly GoalCategory Category;

        public CandidateGoal(int id, Vector2Int cell, GoalCategory category)
        {
            Id = id;
            Cell = cell;
            Category = category;
        }
    }
}
