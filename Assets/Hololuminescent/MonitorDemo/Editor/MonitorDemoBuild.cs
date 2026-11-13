using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Hololuminescent.MonitorDemo.Editor
{
    /// <summary>
    /// Builds the same scenes into a SEPARATE folder (build_monitor/), so the hologram build in build/
    /// is never overwritten. Menu: HLD > Build Monitor Demo. Batch mode:
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Hololuminescent.MonitorDemo.Editor.MonitorDemoBuild.Build
    /// Run the result with  -monitor-demo  (see simulator/unity-landscape.bat).
    /// </summary>
    public static class MonitorDemoBuild
    {
        public const string Output = "build_monitor/Hololuminescent.exe";

        [MenuItem("HLD/Build Monitor Demo")]
        public static void Build()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            Debug.Log($"MonitorDemoBuild: {report.summary.result}, {report.summary.totalErrors} errors -> {Output}");
            if (Application.isBatchMode)
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
