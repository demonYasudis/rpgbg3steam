using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GuildTactics.Editor
{
    /// <summary>Local Windows build verification; generated output stays outside version control.</summary>
    public static class BuildChecks
    {
        public static void RunBatch()
        {
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (scenes.Length == 0) throw new InvalidOperationException("No enabled build scenes.");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = "Builds/UnityValidation/GuildTactics.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            Debug.Log($"Windows build: {report.summary.result}; errors {report.summary.totalErrors}; warnings {report.summary.totalWarnings}.");
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Windows validation build failed.");
        }
    }
}
