using Unity.AI.Navigation;
using UnityEngine;

namespace ToyFactory.Runtime.Debugging
{
    /// <summary>
    /// Test-scene helper that bakes this object's <see cref="NavMeshSurface"/> when the scene
    /// starts, so a test scene needs no committed NavMesh bake. Bake output is S1's to commit
    /// (Git LFS quota), and a small test floor bakes in a few milliseconds. Runs in Awake, so
    /// the NavMesh exists before any Start, including the spawner building the grid from it.
    /// Do not use in the real level: Env's NavMesh is baked once and committed by S1.
    /// </summary>
    [RequireComponent(typeof(NavMeshSurface))]
    [DefaultExecutionOrder(-100)]
    public sealed class RuntimeNavMeshBake : MonoBehaviour
    {
        void Awake()
        {
            GetComponent<NavMeshSurface>().BuildNavMesh();
        }
    }
}
