using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Batch-mode entry point for Tools/unity-check.ps1 -Mode Build.
// Builds the enabled scenes from Build Settings as a Windows 64-bit player.
public static class BuildScript
{
    public const string DefaultOutput = "Builds/Windows/TowerDefense.exe";

    [MenuItem("Tools/Tower Defence/Build Windows Player")]
    public static void BuildWindows()
    {
        var output = GetArgument("-buildOutput") ?? DefaultOutput;
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0) throw new InvalidOperationException("No enabled scenes in Build Settings.");

        // Fully qualified: the game's own global `Path` class (Plugins/Paths) hides System.IO.Path.
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(output)));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = output,
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None
        });

        var summary = report.summary;
        Debug.Log($"[BuildScript] Result: {summary.result}, errors: {summary.totalErrors}, " +
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
            throw new Exception($"[BuildScript] Player build failed: {summary.result} ({summary.totalErrors} errors).");
        }
        Debug.Log("[BuildScript] Player build succeeded.");
    }

    private static string GetArgument(string name)
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
