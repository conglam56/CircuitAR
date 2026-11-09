using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.XR.ARCore;

internal static class ARInstantPlacementBuild
{
    [MenuItem("CircuitAR/Validate and Build Instant Placement APK")]
    public static void Run()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            throw new InvalidOperationException("Select Android as the active build target first.");
        var xrSettings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
        var loaders = xrSettings?.Manager?.activeLoaders;
        string packageName = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        if (loaders == null || loaders.Count != 1 || !(loaders[0] is ARCoreLoader) ||
            packageName.EndsWith(".vrprototype", StringComparison.OrdinalIgnoreCase))
            throw new BuildFailedException("The AR APK requires ARCore as the Android XR loader and an AR package ID. Check Android Player Settings and XR Plug-in Management.");
        ARPlacementRegressionChecks.Run();
        Directory.CreateDirectory("Builds");
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
            locationPathName = "Builds/CircuitAR-instant-placement.apk",
            target = BuildTarget.Android,
            options = BuildOptions.Development
        });
        string summary = report.summary.result + ": " + report.summary.totalErrors + " errors; " +
            report.summary.outputPath;
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/InstantPlacementBuild.txt", summary);
        Debug.Log("[ARInstantPlacementBuild] " + summary);
        if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException(summary);
    }
}
