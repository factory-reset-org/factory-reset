namespace ToyFactory.Interaction
{
    /// <summary>
    /// Something the player uses by keeping Interact held down while staying in reach
    /// (the hack terminal, the shutdown console).
    /// </summary>
    public interface IHoldable : IInteractable
    {
        /// <summary>
        /// Called every frame the hold goes on, starting with the frame of the press.
        /// The calls simply stop when the player lets go or walks away.
        /// </summary>
        void Hold(float deltaTime);
    }
}
