using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TowerDefence.EditorTools
{
    // Batch-mode entry points for Tools/unity-check.ps1 -Mode Build / -Mode BuildWeb.
    // Both build the enabled Build Settings scenes and fail on any build-report error.
    public static class BuildScript
    {
        public const string DefaultWindowsOutput = "Builds/Windows/TowerDefense.exe";
        public const string DefaultWebGLOutput = "Builds/WebGL";

        [MenuItem("Tools/Tower Defence/Build Windows Player")]
        public static void BuildWindows()
        {
            Build(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, GetArgument("-buildOutput") ?? DefaultWindowsOutput, isFolder: false);
        }

        // Web version: output folder with index.html; serve it with Tools/serve-web.ps1.
        [MenuItem("Tools/Tower Defence/Build Web (WebGL) Player")]
        public static void BuildWebGL()
        {
            Build(BuildTarget.WebGL, BuildTargetGroup.WebGL, GetArgument("-buildOutput") ?? DefaultWebGLOutput, isFolder: true);
        }

        private static void Build(BuildTarget target, BuildTargetGroup group, string output, bool isFolder)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) throw new InvalidOperationException("No enabled scenes in Build Settings.");

            // Fully qualified so it never clashes with the game's TowerDefence.Paths.Path class.
            var fullOutput = System.IO.Path.GetFullPath(output);
            Directory.CreateDirectory(isFolder ? fullOutput : System.IO.Path.GetDirectoryName(fullOutput));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = target,
                targetGroup = group,
                options = BuildOptions.None
            });

            var summary = report.summary;
            Debug.Log($"[BuildScript] {target} result: {summary.result}, errors: {summary.totalErrors}, " +
                      $"warnings: {summary.totalWarnings}, size: {summary.totalSize / (1024 * 1024)} MB, output: {summary.outputPath}");
            // Unity can report Succeeded while logging errors (for example a lighting precompute
            // that cannot run). Treat any reported error as a failed build so it gets looked at.
            if (summary.result != BuildResult.Succeeded || summary.totalErrors > 0)
            {
                foreach (var step in report.steps)
                    foreach (var message in step.messages)
                        if (message.type == LogType.Error || message.type == LogType.Exception)
                            Debug.LogWarning($"[BuildScript] Build error in step '{step.name}': {message.content}");
                // Throwing from -executeMethod makes batch-mode Unity exit with a non-zero code.
                throw new Exception($"[BuildScript] {target} build failed: {summary.result} ({summary.totalErrors} errors).");
            }
            Debug.Log($"[BuildScript] {target} build succeeded.");
        }

        private static string GetArgument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }
}
