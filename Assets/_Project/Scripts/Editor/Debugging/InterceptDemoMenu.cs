using UnityEditor;
using UnityEngine;
using ToyFactory.Runtime.Debugging;

namespace ToyFactory.Editor.Debugging
{
    /// <summary>Starts <see cref="InterceptDemo"/> from the menu while the game is playing.</summary>
    static class InterceptDemoMenu
    {
        const string MenuPath = "Factory Reset/Demo/Intercept vs Chase";

        [MenuItem(MenuPath)]
        static void Start()
        {
            if (InterceptDemo.Start(out string problem) == null)
                Debug.LogWarning($"Intercept vs Chase: {problem}");
            else
                Debug.Log("Intercept vs Chase: the chaser is beside the Captain and the overlay (F3) is on. Run for a goal out of their sight.");
        }

        [MenuItem(MenuPath, true)]
        static bool CanStart() => EditorApplication.isPlaying;

        const string TopDownPath = "Factory Reset/Demo/Top-Down View";

        [MenuItem(TopDownPath)]
        static void TopDown()
        {
            bool on = InterceptDemo.ToggleTopDownView();
            Debug.Log(on ? "Top-down view on, ceilings hidden." : "Top-down view off, ceilings back.");
        }

        [MenuItem(TopDownPath, true)]
        static bool CanTopDown() => EditorApplication.isPlaying;
    }
}
