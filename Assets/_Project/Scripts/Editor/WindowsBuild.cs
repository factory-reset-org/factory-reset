using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ToyFactory.Editor
{
    /// <summary>
    /// One-click Windows build of the scenes in the Build Settings, from
    /// <b>Factory Reset / Build / Windows Player</b>. It writes to
    /// <c>Builds/Windows/FactoryReset.exe</c> (git-ignored) and appends a row to
    /// <c>Docs/BuildLog.md</c> with the date, version, result, size, time and the number of
    /// warnings and errors, so every weekly build leaves its evidence without anyone typing it.
    /// </summary>
    public static class WindowsBuild
    {
        const string OutputFolder = "Builds/Windows";
        const string ExecutableName = "FactoryReset.exe";

        // Builds before 19 October were named after the plan's working title. Their exe and
        // folders (_Data, _BackUpThisFolder...) are removed so they are not shipped beside the new ones.
        const string OldBuildPrefix = "ToyFactoryRevolt";
        const string LogPath = "Docs/BuildLog.md";

        const string LogHeader =
            "# Build log\n\n" +
            "One row per Windows build, appended by **Factory Reset / Build / Windows Player**. " +
            "The row says what was built and from which commit; whether it was then played through is the last column.\n\n" +
            "| Date | Version | Commit | Result | Size (MB) | Build time (s) | Scenes | Warnings | Errors | Played through |\n" +
            "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |\n";

        [MenuItem("Factory Reset/Build/Windows Player")]
        public static void Build()
        {
            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("No enabled scenes in the Build Settings.");
                return;
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string folder = Path.Combine(projectRoot, OutputFolder);
            Directory.CreateDirectory(folder);
            RemoveOldBuild(folder);
            string exe = Path.Combine(folder, ExecutableName);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            AppendLog(summary, scenes.Length, projectRoot);

            if (summary.result == BuildResult.Succeeded)
                Debug.Log($"Windows build succeeded: {exe} ({summary.totalSize / 1048576.0:0.0} MB, {summary.totalTime.TotalSeconds:0} s). Row added to {LogPath}.");
            else
                Debug.LogError($"Windows build {summary.result}: {summary.totalErrors} errors. Row added to {LogPath}.");
        }

        // Only entries named after the old title go; anything else in the folder is left alone.
        static void RemoveOldBuild(string folder)
        {
            foreach (string directory in Directory.GetDirectories(folder, OldBuildPrefix + "*"))
                Directory.Delete(directory, true);
            foreach (string file in Directory.GetFiles(folder, OldBuildPrefix + "*"))
                File.Delete(file);
        }

        static void AppendLog(BuildSummary summary, int scenes, string projectRoot)
        {
            string path = Path.Combine(projectRoot, LogPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (!File.Exists(path))
                File.WriteAllText(path, LogHeader, new UTF8Encoding(false));

            string row = string.Format(CultureInfo.InvariantCulture,
                "| {0:yyyy-MM-dd HH:mm} | {1} | {2} | {3} | {4:0.0} | {5:0} | {6} | {7} | {8} |  |\n",
                DateTime.Now, PlayerSettings.bundleVersion, Commit(projectRoot), summary.result,
                summary.totalSize / 1048576.0, summary.totalTime.TotalSeconds, scenes,
                summary.totalWarnings, summary.totalErrors);

            string text = File.ReadAllText(path);
            if (!text.EndsWith("\n"))
                text += "\n";
            File.WriteAllText(path, text + row, new UTF8Encoding(false));
        }

        // The short commit the build was made from, read straight from the repository so no git call is needed.
        static string Commit(string projectRoot)
        {
            try
            {
                string head = File.ReadAllText(Path.Combine(projectRoot, ".git", "HEAD")).Trim();
                if (head.StartsWith("ref: "))
                {
                    string refPath = Path.Combine(projectRoot, ".git", head.Substring(5).Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(refPath))
                        head = File.ReadAllText(refPath).Trim();
                    else
                        return head.Substring(5);
                }
                return head.Length >= 7 ? head.Substring(0, 7) : head;
            }
            catch (Exception)
            {
                return "unknown";
            }
        }
    }
}
