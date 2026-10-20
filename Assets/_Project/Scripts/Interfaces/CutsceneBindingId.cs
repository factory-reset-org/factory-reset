using System.Collections.Generic;
using UnityEngine;

namespace ToyFactory.Interfaces
{
    /// <summary>
    /// A stable name on an object that a cutscene animates in another scene (a door, a lamp,
    /// a core). Timelines live in the Agents scene and cannot keep a serialised reference to
    /// an object in Env or Interactables, so the cutscene system looks the target up by this
    /// id when the cutscene starts. Each id must be unique among loaded objects.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CutsceneBindingId : MonoBehaviour
    {
        static readonly Dictionary<string, CutsceneBindingId> Registered = new Dictionary<string, CutsceneBindingId>();

        [SerializeField, Tooltip("Unique name the cutscene uses to find this object, e.g. \"ControlRoomDoor\".")]
        string id;

        /// <summary>The name cutscenes use to find this object.</summary>
        public string Id => id;

        /// <summary>Finds the loaded, enabled object with this id.</summary>
        public static bool TryFind(string id, out CutsceneBindingId target)
        {
            if (string.IsNullOrEmpty(id))
            {
                target = null;
                return false;
            }
            return Registered.TryGetValue(id, out target);
        }

        void OnEnable()
        {
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogWarning($"{nameof(CutsceneBindingId)} on {name} has no id; cutscenes cannot find it.", this);
                return;
            }

            if (Registered.TryGetValue(id, out CutsceneBindingId other) && other != this)
            {
                Debug.LogError($"Cutscene binding id \"{id}\" is used by both {other.name} and {name}. Ids must be unique.", this);
                return;
            }

            Registered[id] = this;
        }

        void OnDisable()
        {
            // Only remove the entry if it is ours, so a duplicate being disabled cannot
            // unregister the original.
            if (!string.IsNullOrEmpty(id) && Registered.TryGetValue(id, out CutsceneBindingId current) && current == this)
                Registered.Remove(id);
        }

        // Domain reload is off in this project, so the static registry survives between play
        // sessions. Clear it at the start of each session so it never holds destroyed objects.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearRegistry()
        {
            Registered.Clear();
        }
    }
}
