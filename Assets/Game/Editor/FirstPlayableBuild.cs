using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LittleCiv.Editor
{
    public static class FirstPlayableBuild
    {
        private const string OutputDirectory = "Builds/FirstPlayable";
        private const string ExecutablePath = OutputDirectory + "/LittleCivilization.exe";

        [MenuItem("Little Civilization/Build First Playable Windows")]
        public static void BuildWindows()
        {
            Directory.CreateDirectory(OutputDirectory);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                locationPathName = ExecutablePath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    $"First playable build failed: {report.summary.result}, " +
                    $"{report.summary.totalErrors} errors.");
            }

            Debug.Log($"First playable build created: {Path.GetFullPath(ExecutablePath)}");
        }
    }
}
