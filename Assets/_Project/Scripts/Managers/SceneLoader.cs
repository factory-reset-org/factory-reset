using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.World;

namespace ToyFactory.Managers
{
    /// <summary>
    /// Starts the game from the Bootstrap scene: loads the other scenes on top of it, then
    /// builds the grid, spawns the agents, starts the journey and sets the game state, in
    /// that order, because each step needs the one before it.
    /// </summary>
    /// <remarks>
    /// Lives in the default assembly, not Runtime, because it is the one script that has to
    /// reach Runtime (grid, spawner), Journey (chapters) and GameManager together.
    /// </remarks>
    public sealed class SceneLoader : MonoBehaviour
    {
        const string EnvironmentScene = "Env";

        // Loaded after the environment, in this order.
        static readonly string[] OtherScenes = { "Interactables", "Agents", "UI" };

        [Tooltip("Start the chapter journey once everything is loaded. Leave off until the task props exist: without them ChapterManager reports every task as missing.")]
        [SerializeField] bool beginJourney;

        [Tooltip("Game state once loading has finished. Title waits for a start screen; use Playing until one exists.")]
        [SerializeField] GameState stateAfterLoad = GameState.Title;

        IEnumerator Start()
        {
            yield return LoadAdditive(EnvironmentScene);

            // Lighting, skybox and fog come from the active scene.
            Scene environment = SceneManager.GetSceneByName(EnvironmentScene);
            if (environment.isLoaded)
                SceneManager.SetActiveScene(environment);

            for (int i = 0; i < OtherScenes.Length; i++)
                yield return LoadAdditive(OtherScenes[i]);

            BuildGrid();
            SpawnAgents();
            BeginJourney();

            if (GameManager.Instance != null)
                GameManager.Instance.SetState(stateAfterLoad);
            else
                Debug.LogError("SceneLoader: no GameManager in the Bootstrap scene, so the game state was not set.", this);
        }

        static IEnumerator LoadAdditive(string sceneName)
        {
            // Already open next to Bootstrap in the Editor.
            if (SceneManager.GetSceneByName(sceneName).isLoaded)
                yield break;

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"SceneLoader: scene '{sceneName}' is not in Build Settings, so it was skipped.");
                yield break;
            }

            yield return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        }

        // The NavMesh and the props exist now, so the grid can be sampled.
        void BuildGrid()
        {
            if (GridManager.Instance == null)
            {
                Debug.LogError("SceneLoader: no GridManager was loaded, so agents will have no grid.", this);
                return;
            }

            try
            {
                GridManager.Instance.BuildGrid();
            }
            catch (Exception e)
            {
                Debug.LogException(e, GridManager.Instance);
            }
        }

        // After the grid, so each agent's first path request lands on a valid grid.
        void SpawnAgents()
        {
            if (AgentSpawner.Instance == null)
            {
                Debug.LogWarning("SceneLoader: no AgentSpawner was loaded, so no agents were spawned.", this);
                return;
            }

            AgentSpawner.Instance.SpawnAll();
        }

        void BeginJourney()
        {
            if (!beginJourney)
                return;

            if (ChapterManager.Current == null)
            {
                Debug.LogWarning("SceneLoader: no ChapterManager was loaded, so the journey did not start.", this);
                return;
            }

            ChapterManager.Current.Begin();
        }
    }
}
