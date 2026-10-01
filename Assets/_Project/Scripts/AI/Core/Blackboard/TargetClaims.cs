using System.Collections.Generic;

namespace ToyFactory.AI.Core.Blackboard
{
    /// <summary>
    /// Lets independent agents (the Saboteur squad) coordinate without talking to each
    /// other directly: at most one agent holds a claim on any given target at a time.
    /// </summary>
    public sealed class TargetClaims
    {
        readonly struct Claim
        {
            public readonly int AgentId;
            public readonly float Score;

            public Claim(int agentId, float score)
            {
                AgentId = agentId;
                Score = score;
            }
        }

        readonly Dictionary<int, Claim> _claims = new Dictionary<int, Claim>();

        /// <summary>
        /// Attempts to claim targetId for agentId at the given score. Succeeds if the
        /// target is unclaimed, already held by agentId (refreshes the score), or held
        /// by another agent with a lower score. On an exact tie the lower agentId wins.
        /// </summary>
        public bool TryClaim(int targetId, int agentId, float score)
        {
            if (_claims.TryGetValue(targetId, out Claim existing) && existing.AgentId != agentId)
            {
                bool outscores = score > existing.Score || (score == existing.Score && agentId < existing.AgentId);
                if (!outscores)
                    return false;
            }

            _claims[targetId] = new Claim(agentId, score);
            return true;
        }

        /// <summary>Releases every claim currently held by agentId.</summary>
        public void Release(int agentId)
        {
            List<int> toRemove = null;
            foreach (KeyValuePair<int, Claim> entry in _claims)
            {
                if (entry.Value.AgentId == agentId)
                {
                    toRemove ??= new List<int>();
                    toRemove.Add(entry.Key);
                }
            }

            if (toRemove == null)
                return;

            for (int i = 0; i < toRemove.Count; i++)
                _claims.Remove(toRemove[i]);
        }

        /// <summary>The agent currently holding targetId's claim, or null if unclaimed.</summary>
        public int? ClaimedBy(int targetId) =>
            _claims.TryGetValue(targetId, out Claim existing) ? existing.AgentId : (int?)null;
    }
}
