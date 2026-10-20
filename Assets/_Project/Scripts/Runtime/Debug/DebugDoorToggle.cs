using UnityEngine;
using UnityEngine.InputSystem;
using ToyFactory.Runtime.World;

namespace ToyFactory.Runtime.Debugging
{
    /// <summary>
    /// Test-scene door: a key opens and closes a doorway in the grid
    /// (<see cref="GridManager.SetDoorClosed"/>) and shows or hides a door panel, so closed-door
    /// behaviour can be shown without S2's real door. The panel should carry a NavMeshModifier
    /// set to ignore, so the runtime NavMesh bake still sees the doorway as floor.
    /// </summary>
    public sealed class DebugDoorToggle : MonoBehaviour
    {
        [Tooltip("The DoorwayMarker's door id.")]
        [SerializeField, Min(0)] int doorId;

        [Tooltip("Shown while the door is closed.")]
        [SerializeField] GameObject panel;

        [SerializeField] Key toggleKey = Key.O;

        [Tooltip("Must match the DoorwayMarker's 'Initially Closed'.")]
        [SerializeField] bool startClosed = true;

        /// <summary>Whether the door is closed now.</summary>
        public bool IsClosed { get; private set; }

        void Start()
        {
            IsClosed = startClosed;
            ShowPanel();
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
                SetClosed(!IsClosed);
        }

        /// <summary>Opens or closes the doorway in the grid and shows the panel to match.</summary>
        public void SetClosed(bool closed)
        {
            IsClosed = closed;
            GridManager.SetDoorClosed(doorId, closed);
            ShowPanel();
        }

        void ShowPanel()
        {
            if (panel != null)
                panel.SetActive(IsClosed);
        }
    }
}
