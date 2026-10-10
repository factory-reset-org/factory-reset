using UnityEngine;
using ToyFactory.AI.Agents.Mock;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.World;

namespace ToyFactory.Runtime.Debugging
{
    /// <summary>
    /// The intercept against a chaser, for recording: adds a second Captain body beside the
    /// real one, driven by <see cref="ChaserBrain"/> (straight at the player, no prediction),
    /// and shows the debug overlay, so both routes are drawn as the player runs for a goal.
    /// Started from the Editor (Factory Reset → Demo → Intercept vs Chase) while playing.
    /// </summary>
    public static class InterceptDemo
    {
        public const string ChaserName = "Captain (chaser)";

        /// <summary>
        /// Spawns the chaser two metres to the real Captain's right and turns the overlay on.
        /// Returns the chaser, or null with the reason in <paramref name="problem"/> (no level,
        /// no Captain yet, a chaser already there).
        /// </summary>
        public static AgentController Start(out string problem)
        {
            AgentSpawner spawner = AgentSpawner.Instance;
            GridGraph grid = GridManager.Current;
            problem = null;
            if (spawner == null || grid == null)
            {
                problem = "Play from Bootstrap first: there is no agent spawner or level grid.";
                return null;
            }

            AgentController captain = null;
            foreach (AgentController agent in spawner.SpawnedAgents)
            {
                if (agent == null || agent.Type != AgentType.Captain)
                    continue;
                if (agent.Brain is ChaserBrain)
                {
                    problem = "The chaser is already in the level.";
                    return null;
                }
                captain = agent;
            }
            if (captain == null)
            {
                problem = "There is no Captain in the level.";
                return null;
            }

            Vector3 beside = captain.transform.position + captain.transform.right * 2f;
            if (grid.TryFindNearestTraversable(grid.WorldToCell(beside), 6, out Vector2Int cell))
                beside = grid.CellToWorld(cell);
            AgentController chaser = spawner.Spawn(AgentType.Captain, new ChaserBrain(grid, new AStarSearch(grid)),
                beside, captain.transform.rotation, ChaserName);
            if (chaser == null)
            {
                problem = "The spawner has no Captain body.";
                return null;
            }

            AgentDebugOverlay overlay = Object.FindFirstObjectByType<AgentDebugOverlay>();
            if (overlay != null)
                overlay.Shown = true;
            return chaser;
        }

        const string TopDownName = "Demo Top-Down Camera";

        /// <summary>
        /// Switches a camera looking straight down on the whole level on or off, hiding the
        /// ceilings while it is on so both routes can be seen at once. The player still plays
        /// with the usual controls. Play mode only: nothing in a scene is changed or saved.
        /// Returns true if the view is now on.
        /// </summary>
        public static bool ToggleTopDownView()
        {
            GameObject existing = GameObject.Find(TopDownName);
            bool on = existing == null;
            if (on)
            {
                var go = new GameObject(TopDownName);
                var camera = go.AddComponent<Camera>();
                camera.depth = 50f;   // drawn over the player's camera
                camera.fieldOfView = 60f;
                // The level is about 41 m square: 37 m up at 60 degrees shows all four rooms.
                go.transform.SetPositionAndRotation(new Vector3(20.5f, 37f, 20.5f), Quaternion.Euler(90f, 0f, 0f));
                AgentDebugOverlay.LabelCamera = camera;
            }
            else
            {
                Object.Destroy(existing);
                AgentDebugOverlay.LabelCamera = null;
            }
            SetCeilingsShown(!on);
            return on;
        }

        // The Env scene's ceilings and their light fittings (S1's), switched off only in play.
        static void SetCeilingsShown(bool shown)
        {
            UnityEngine.SceneManagement.Scene env = UnityEngine.SceneManagement.SceneManager.GetSceneByName("Env");
            if (!env.isLoaded)
                return;
            foreach (GameObject root in env.GetRootGameObjects())
                foreach (Transform child in root.transform)
                    if (child.name == "Ceilings" || child.name == "CeilingLights_Baked")
                        child.gameObject.SetActive(shown);
        }
    }
}
