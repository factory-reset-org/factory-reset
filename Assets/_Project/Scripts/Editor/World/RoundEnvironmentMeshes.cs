using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using ToyFactory.Runtime.World;

namespace ToyFactory.Editor.World
{
    /// <summary>
    /// Rebuilds every chamfered environment box mesh (<c>BevelBox_*</c>) as a smooth rounded box
    /// (<see cref="RoundedBoxMesh"/>), in place, and renames it <c>RoundBox_*</c>. Meshes that are
    /// already <c>RoundBox_*</c> are rebuilt too, so a change to the rounding rule can be re-run.
    /// </summary>
    /// <remarks>
    /// The mesh asset keeps its GUID, so every wall, obstacle, toy and trim that uses it picks up
    /// the new shape without a scene edit. Colliders are separate components with their own
    /// sizes, so the NavMesh and grid do not change. Lightmap UVs are regenerated; rebake the
    /// lighting afterwards.
    /// </remarks>
    public static class RoundEnvironmentMeshes
    {
        const string Folder = "Assets/_Project/Prefabs/Environment/Meshes";

        static readonly Regex BevelName = new Regex(
            @"^(?:BevelBox_([0-9.]+)x([0-9.]+)x([0-9.]+)_b|RoundBox_([0-9.]+)x([0-9.]+)x([0-9.]+)_r)[0-9.]+$", RegexOptions.CultureInvariant);

        [MenuItem("Tools/Factory Reset/Round Environment Meshes")]
        static void RunFromMenu() => Debug.Log(Run());

        /// <summary>Rounds every <c>BevelBox_*</c> mesh in the environment mesh folder and
        /// returns a report (triangles before and after, per mesh and in total).</summary>
        public static string Run()
        {
            var report = new StringBuilder();
            int meshes = 0, trianglesBefore = 0, trianglesAfter = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { Folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                Match match = mesh != null ? BevelName.Match(mesh.name) : Match.Empty;
                if (!match.Success)
                    continue;

                int g = match.Groups[1].Success ? 1 : 4;   // BevelBox_ or RoundBox_ name
                var size = new Vector3(Parse(match.Groups[g].Value), Parse(match.Groups[g + 1].Value), Parse(match.Groups[g + 2].Value));
                float radius = RoundedBoxMesh.RecommendedRadius(size);
                int segments = RoundedBoxMesh.RecommendedSegments(size, radius);
                Mesh rounded = RoundedBoxMesh.Create(size, radius, segments);
                Unwrapping.GenerateSecondaryUVSet(rounded);

                int before = mesh.triangles.Length / 3;
                string newName = $"RoundBox_{Format(size.x)}x{Format(size.y)}x{Format(size.z)}_r{Format(radius)}";
                EditorUtility.CopySerialized(rounded, mesh);
                mesh.name = newName;
                Object.DestroyImmediate(rounded);
                EditorUtility.SetDirty(mesh);

                string error = System.IO.Path.GetFileNameWithoutExtension(path) == newName
                    ? null : AssetDatabase.RenameAsset(path, newName);
                if (!string.IsNullOrEmpty(error))
                    report.AppendLine($"  rename failed for {path}: {error}");

                int after = mesh.triangles.Length / 3;
                meshes++;
                trianglesBefore += before;
                trianglesAfter += after;
                report.AppendLine($"  {newName}: {before} -> {after} triangles (radius {Format(radius)} m, {segments} segments)");
            }

            AssetDatabase.SaveAssets();
            return $"Rounded {meshes} environment meshes: {trianglesBefore} -> {trianglesAfter} triangles (one copy of each).\n{report}";
        }

        static float Parse(string text) => float.Parse(text, CultureInfo.InvariantCulture);

        static string Format(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
