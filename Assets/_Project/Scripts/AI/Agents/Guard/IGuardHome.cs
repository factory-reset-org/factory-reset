using UnityEngine;

namespace ToyFactory.AI.Agents.Guard
{
    /// <summary>
    /// The room a Guard holds. The Guard fights only for its own room: it engages a player who
    /// is in it (or close to its doors) and takes cover only inside it. Implemented by Runtime
    /// from the level's room volumes, so the brain never touches a scene object.
    /// </summary>
    public interface IGuardHome
    {
        /// <summary>
        /// True if <paramref name="position"/> is inside the room, or within
        /// <paramref name="margin"/> metres of it.
        /// </summary>
        bool Contains(Vector3 position, float margin);
    }
}
