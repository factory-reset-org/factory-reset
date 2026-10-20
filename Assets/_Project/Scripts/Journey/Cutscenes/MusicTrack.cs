namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>The game's pieces of music: the title, one per room, and the two endings.</summary>
    public enum MusicTrack
    {
        None,
        /// <summary>The title screen, and the Results screen once its sting has played.</summary>
        Title,
        /// <summary>Chapter 1: the Tracker prowling a toy factory at night.</summary>
        AssemblyFloor,
        /// <summary>Chapter 2: the firefight with the Guard.</summary>
        PaintingRoom,
        /// <summary>Chapter 3: stalked by the Captain between the shelves.</summary>
        StorageArea,
        /// <summary>Chapter 4: the boss fight against Factory OS itself.</summary>
        ControlRoom,
        /// <summary>A sting, played once: the factory was shut down.</summary>
        WinJingle,
        /// <summary>A sting, played once: Unit 047 was recalled.</summary>
        LoseJingle,
    }
}
