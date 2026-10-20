namespace ToyFactory.Interfaces
{
    /// <summary>A door the player or an agent can open and close.</summary>
    public interface IDoor
    {
        /// <summary>True once the door has fully finished opening.</summary>
        bool IsOpen { get; }

        /// <summary>Starts opening. No effect if already open or opening.</summary>
        void Open();

        /// <summary>
        /// Starts closing. No effect if already closed or closing. If something blocks the
        /// doorway, the door stops and keeps retrying rather than reversing back open.
        /// </summary>
        void Close();
    }
}
