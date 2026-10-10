namespace ToyFactory.AI.Agents.Guard
{
    /// <summary>A cover candidate with the score it was ranked by.</summary>
    public readonly struct RankedCover
    {
        public RankedCover(CoverCandidate candidate, float score)
        {
            Candidate = candidate;
            Score = score;
        }

        public CoverCandidate Candidate { get; }

        public float Score { get; }
    }
}
