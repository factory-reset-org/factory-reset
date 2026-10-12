using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Runtime.Agents
{
    /// <summary>
    /// Puts this agent's body on a mark when a given cutscene starts, so that cutscene's fixed
    /// cameras find it: the Captain stands on its spawn facing the console in the ending,
    /// wherever the final fight left it, and powers down there in front of the camera.
    /// </summary>
    /// <remarks>
    /// The move happens as the cutscene starts, before its first shot is drawn, so the cut
    /// hides it. Agents are frozen during cutscenes, and the ending is followed by the Results
    /// screen, so nothing walks the body back. The brain is not told: it is not ticked again.
    /// </remarks>
    [RequireComponent(typeof(AgentController))]
    public sealed class AgentCutsceneMark : MonoBehaviour
    {
        [Tooltip("Id of the cutscene that puts the body on its mark.")]
        [SerializeField] string cutsceneId = "ending";

        [Tooltip("Where the body's feet go (world).")]
        [SerializeField] Vector3 position = new Vector3(10.5f, 0f, 36f);

        [Tooltip("The way it faces, in degrees about Y (180 = south).")]
        [SerializeField] float yaw = 180f;

        /// <summary>True once the body has been put on its mark.</summary>
        public bool OnMark { get; private set; }

        void OnEnable() => CutsceneEvents.OnCutsceneStarted += HandleStarted;

        void OnDisable() => CutsceneEvents.OnCutsceneStarted -= HandleStarted;

        void HandleStarted(string id)
        {
            if (id != cutsceneId)
                return;
            // A CharacterController overwrites a moved transform unless it is off for the move.
            var capsule = GetComponent<CharacterController>();
            bool wasEnabled = capsule != null && capsule.enabled;
            if (wasEnabled)
                capsule.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            if (wasEnabled)
                capsule.enabled = true;
            OnMark = true;
        }
    }
}
