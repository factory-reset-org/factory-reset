namespace ToyFactory.Interaction
{
    /// <summary>
    /// Optional: what the prompt line says while the player looks at this prop, such as
    /// "Pull the lever". The interactor adds the key and "Hold" itself. A prop without it
    /// shows a plain "Use".
    /// </summary>
    public interface IInteractionPrompt
    {
        string PromptText { get; }
    }
}
