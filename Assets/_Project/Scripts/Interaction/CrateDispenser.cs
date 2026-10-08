using UnityEngine;

namespace ToyFactory.Interaction
{
    /// <summary>
    /// Keeps the pressure plate task from getting stuck. The crate can end up wedged in a
    /// corner or pushed somewhere it cannot come back from; using the dispenser brings it
    /// back to the outlet, and a crate that falls out of the level comes back by itself.
    /// </summary>
    public sealed class CrateDispenser : MonoBehaviour, IInteractable
    {
        [Tooltip("The crate this dispenser looks after.")]
        [SerializeField] PushableBox crate;

        [Tooltip("Where the crate reappears. Left empty, one metre above the dispenser.")]
        [SerializeField] Transform outlet;

        [Tooltip("Once this task is done the crate is left alone. Optional.")]
        [SerializeField] TaskProp doneWhen;

        [Tooltip("A crate below this height has fallen out of the level.")]
        [SerializeField] float lostBelowHeight = -5f;
        [SerializeField, Min(0.1f)] float checkInterval = 1f;

        float _nextCheckTime;

        public void Interact() => Recall();

        /// <summary>Puts the crate back at the outlet, at rest.</summary>
        public void Recall()
        {
            if (crate == null || (doneWhen != null && doneWhen.IsCompleted))
                return;

            if (outlet != null)
                crate.Teleport(outlet.position, outlet.rotation);
            else
                crate.Teleport(transform.position + Vector3.up, transform.rotation);
        }

        void Update()
        {
            if (Time.time < _nextCheckTime)
                return;
            _nextCheckTime = Time.time + checkInterval;

            if (crate != null && crate.transform.position.y < lostBelowHeight)
                Recall();
        }
    }
}
