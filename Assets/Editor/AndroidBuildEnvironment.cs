using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps Android/IL2CPP compiler temporary files on the project drive.
/// This only changes the environment of the current Unity Editor process and
/// its child build tools; it does not change the user's Windows environment.
/// </summary>
[InitializeOnLoad]
internal static class AndroidBuildEnvironment
{
    static AndroidBuildEnvironment()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string buildTemp = Path.Combine(projectRoot, "Library", "AndroidBuildTemp");

        try
        {
            Directory.CreateDirectory(buildTemp);
            Environment.SetEnvironmentVariable("TEMP", buildTemp, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("TMP", buildTemp, EnvironmentVariableTarget.Process);
            Debug.Log($"[AndroidBuildEnvironment] IL2CPP temp redirected to: {buildTemp}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[AndroidBuildEnvironment] Could not redirect build temp: " + ex.Message);
        }
    }
}
