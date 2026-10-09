using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Journey.Cutscenes;

namespace ToyFactory.Journey.Debugging
{
    /// <summary>
    /// Debug keys that jump the journey forward: F6 to Chapter 2, F7 to Chapter 3, F8 to
    /// Chapter 4. Every earlier chapter is finished the way the game would finish it (each
    /// task completed through <see cref="ChapterFlow.CompleteTask"/>, each switch restored,
    /// each cutscene played and skipped, so its Critical signals fire), then the player is put
    /// at the entrance of the new chapter's room. Editor and development builds only.
    /// </summary>
    /// <remarks>
    /// Nothing is faked: the chapter flow, the cutscene runner and every listener (the doors,
    /// the Captain, the lighting) see the same events as in a real play-through, so the world
    /// is in the state a player would leave it in. Only forward jumps are possible; the
    /// journey cannot be wound back. Props keep their own look (a lever that was never pulled
    /// stays up), which does not matter for testing a later room.
    /// </remarks>
    public sealed class ChapterJump : MonoBehaviour
    {
        [Tooltip("Where the player is put for chapters 1 to 4 (room entrances), world positions.")]
        [SerializeField] Vector3[] entrances =
        {
            new Vector3(10.5f, 0.1f, 3f),      // Chapter 1: the Assembly Floor start
            new Vector3(22.6f, 0.1f, 10.5f),   // Chapter 2: inside door 1, Painting Room
            new Vector3(31f, 0.1f, 22.6f),     // Chapter 3: inside door 2, Storage Area
            new Vector3(10.5f, 0.1f, 22.6f),   // Chapter 4: inside door 4, Control Room
        };

        [Tooltip("Which way the player faces at each entrance, in degrees about the vertical (0 = north, +Z).")]
        [SerializeField] float[] entranceYaw = { 0f, 90f, 0f, 0f };

        [Tooltip("Seconds to wait for each cutscene to start before giving up.")]
        [SerializeField, Min(1f)] float cutsceneTimeout = 10f;

        static readonly Key[] JumpKeys = { Key.F6, Key.F7, Key.F8 };

        Coroutine _jump;

        /// <summary>True while a jump is running.</summary>
        public bool IsJumping => _jump != null;

        static bool Allowed => Application.isEditor || Debug.isDebugBuild;

        void Update()
        {
            if (!Allowed || IsJumping || Keyboard.current == null)
                return;
            for (int i = 0; i < JumpKeys.Length; i++)
                if (Keyboard.current[JumpKeys[i]].wasPressedThisFrame)
                    JumpTo(i + 2);
        }

        /// <summary>
        /// Finishes every chapter before <paramref name="chapter"/> and puts the player at its
        /// room's entrance. Ignored while a jump runs, before the journey has begun, or when the
        /// journey is already at or past that chapter.
        /// </summary>
        public void JumpTo(int chapter)
        {
            ChapterFlow flow = ChapterManager.Current != null ? ChapterManager.Current.Flow : null;
            if (IsJumping || flow == null || !flow.HasBegun)
                return;
            if (chapter <= flow.CurrentChapter || chapter > flow.ChapterCount)
            {
                Debug.Log($"Chapter jump: already at chapter {flow.CurrentChapter}; jumps only go forward (2 to {flow.ChapterCount}).");
                return;
            }
            _jump = StartCoroutine(Jump(flow, chapter));
        }

        IEnumerator Jump(ChapterFlow flow, int chapter)
        {
            CutsceneDirector director = CutsceneDirector.Current;
            if (director != null && director.IsPlaying)
                director.Skip();

            while (flow.CurrentChapter < chapter)
            {
                int from = flow.CurrentChapter;
                ChapterDefinition definition = flow.CurrentDefinition;
                foreach (TaskDefinition task in definition.Tasks)
                    flow.CompleteTask(task.TaskId);
                if (definition.SwitchNumber > 0)
                    flow.CompleteTask(ChapterFlow.SwitchTaskPrefix + definition.SwitchNumber);

                // The switch plays the next cutscene after a short delay; skip it as Escape would,
                // which fires its Critical signals, and the next chapter starts when it ends.
                float until = Time.realtimeSinceStartup + cutsceneTimeout;
                while (flow.CurrentChapter == from)
                {
                    director = CutsceneDirector.Current;
                    if (director != null && director.IsPlaying)
                        director.Skip();
                    if (Time.realtimeSinceStartup > until)
                    {
                        Debug.LogWarning($"Chapter jump: chapter {from} did not hand over to {from + 1}; stopped.");
                        _jump = null;
                        yield break;
                    }
                    yield return null;
                }
            }

            PlacePlayer(chapter);
            Debug.Log($"Chapter jump: now at chapter {chapter}.");
            _jump = null;
        }

        void PlacePlayer(int chapter)
        {
            if (!(PlayerState.Current is Component player) || player == null)
                return;
            int i = Mathf.Clamp(chapter - 1, 0, entrances.Length - 1);
            // The CharacterController would undo a direct move on its next update.
            var body = player.GetComponent<CharacterController>();
            if (body != null)
                body.enabled = false;
            player.transform.SetPositionAndRotation(entrances[i], Quaternion.Euler(0f, entranceYaw.ElementAtOrDefault(i), 0f));
            if (body != null)
                body.enabled = true;
        }
    }
}
