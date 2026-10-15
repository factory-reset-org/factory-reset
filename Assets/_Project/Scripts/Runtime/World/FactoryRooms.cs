namespace ToyFactory.Runtime.World
{
    /// <summary>
    /// Which of the four rooms a point on the floor is in. The level is a 2 x 2 ring of rooms split
    /// at one line each way (see Docs/LevelLayout.md), so the room is a lookup on two comparisons.
    /// </summary>
    public static class FactoryRooms
    {
        /// <summary>The wall line between the rooms, in x and in z, in metres.</summary>
        public const float Split = 20.75f;

        /// <summary>How far past the line a point must be before the room changes, so standing in a doorway does not flicker.</summary>
        public const float Hysteresis = 0.6f;

        public const string Assembly = "Assembly Floor";
        public const string Painting = "Painting Room";
        public const string Storage = "Storage Area";
        public const string Control = "Control Room";

        /// <summary>The room at (<paramref name="x"/>, <paramref name="z"/>). Inside the dead band round a wall it keeps <paramref name="current"/>.</summary>
        public static string At(float x, float z, string current)
        {
            bool east = Side(x, current, Painting, Storage);
            bool north = Side(z, current, Control, Storage);
            if (north)
                return east ? Storage : Control;
            return east ? Painting : Assembly;
        }

        // True for the high side of the line. Near the line the side the current room is on wins.
        static bool Side(float v, string current, string highRoomA, string highRoomB)
        {
            if (v > Split + Hysteresis)
                return true;
            if (v < Split - Hysteresis)
                return false;
            return current == highRoomA || current == highRoomB;
        }
    }
}
