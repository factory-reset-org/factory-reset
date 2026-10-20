namespace ToyFactory.AI.Core
{
    /// <summary>
    /// Implemented by a brain whose body shows a wind-up key (the Tracker). The brain owns
    /// the energy; the body only shows it: the key turns slower as the toy runs down and
    /// spins fast in reverse while it rewinds. A brain without a key does not implement
    /// this, and the body turns its key (if any) at a constant speed.
    /// </summary>
    /// <remarks>
    /// The runtime reads it every frame after <see cref="IAgentBrain.Tick"/>, so both
    /// members must be cheap and allocate nothing.
    /// </remarks>
    public interface IWindUpState
    {
        /// <summary>Wind-up energy from 0 (run down) to 1 (fully wound).</summary>
        float Energy01 { get; }

        /// <summary>True while the toy has stopped to wind itself back up.</summary>
        bool IsRewinding { get; }
    }
}
