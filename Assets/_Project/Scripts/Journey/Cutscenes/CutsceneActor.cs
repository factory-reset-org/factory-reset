using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>
    /// Unit 047 as the cutscenes see it. The player is a first-person camera with no body, so
    /// while a cutscene plays this shows the Unit 047 model where the player stands, facing
    /// the way the player faces, and hides it again when the cutscene ends.
    /// </summary>
    /// <remarks>
    /// Lives in the Agents scene under the cutscene director. The follow cameras of the
    /// Timelines track this object's transform, so it is moved before the first shot is drawn
    /// (<see cref="CutsceneEvents.OnCutsceneStarted"/> is raised before the Timeline plays).
    /// The model's colliders are switched off for good: it is only ever seen, never touched.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CutsceneActor : MonoBehaviour
    {
        [Tooltip("The Unit 047 model, a child of this object. Shown only while a cutscene plays.")]
        [SerializeField] GameObject model;

        /// <summary>True while the model is on screen.</summary>
        public bool IsShowing => model != null && model.activeSelf;

        /// <summary>The Unit 047 model, for <see cref="Unit047Motion"/>.</summary>
        public GameObject Model => model;

        void Awake()
        {
            if (model == null)
                return;
            foreach (Collider part in model.GetComponentsInChildren<Collider>(true))
                part.enabled = false;
            model.SetActive(false);
        }

        void OnEnable()
        {
            CutsceneEvents.OnCutsceneStarted += HandleStarted;
            CutsceneEvents.OnCutsceneEnded += HandleEnded;
        }

        void OnDisable()
        {
            CutsceneEvents.OnCutsceneStarted -= HandleStarted;
            CutsceneEvents.OnCutsceneEnded -= HandleEnded;
        }

        void HandleStarted(string cutsceneId)
        {
            IPlayerState player = PlayerState.Current;
            if (player != null)
            {
                Vector3 facing = player.Forward;
                facing.y = 0f;
                Quaternion rotation = facing.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(facing) : transform.rotation;
                transform.SetPositionAndRotation(player.Position, rotation);
            }
            if (model != null)
                model.SetActive(true);
        }

        void HandleEnded(string cutsceneId)
        {
            if (model != null)
                model.SetActive(false);
        }
    }
}
