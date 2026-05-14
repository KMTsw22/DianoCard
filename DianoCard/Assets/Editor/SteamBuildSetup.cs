using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace LastEmber.EditorTools
{
    public static class SteamBuildSetup
    {
        const string Menu = "LastEmber/Build/";
        const string OutputRoot = "../../Build";

        [MenuItem(Menu + "Apply Steam IL2CPP Settings (Windows x64)")]
        public static void ApplySteamSettings()
        {
            var nbt = NamedBuildTarget.Standalone;

            PlayerSettings.SetScriptingBackend(nbt, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(nbt, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.SetManagedStrippingLevel(nbt, ManagedStrippingLevel.Minimal);
            PlayerSettings.SetIl2CppCompilerConfiguration(nbt, Il2CppCompilerConfiguration.Master);
            PlayerSettings.SetIl2CppCodeGeneration(nbt, Il2CppCodeGeneration.OptimizeSize);

            PlayerSettings.stripEngineCode = true;
            PlayerSettings.gcIncremental = true;

            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
            EditorUserBuildSettings.SetPlatformSettings("Standalone", "CompressionType", "Lz4HC");

            AssetDatabase.SaveAssets();

            Debug.Log("[SteamBuildSetup] Applied: IL2CPP / .NET Standard / Stripping=Minimal / Compiler=Master / CodeGen=OptimizeSize / StripEngineCode / IncrementalGC / LZ4HC compression. Active target = Windows x64.");
        }

        [MenuItem(Menu + "Build Steam Windows x64")]
        public static void BuildSteamWindows()
        {
            ApplySteamSettings();

            var version = PlayerSettings.bundleVersion;
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, OutputRoot, $"LastEmber_V{version}_Windows"));
            Directory.CreateDirectory(dir);

            var scenes = EditorBuildSettings.scenes;
            int active = 0;
            for (int i = 0; i < scenes.Length; i++) if (scenes[i].enabled) active++;
            if (active == 0)
            {
                Debug.LogError("[SteamBuildSetup] No enabled scenes in Build Profile. Add scenes via File > Build Profiles.");
                return;
            }

            var opts = new BuildPlayerOptions
            {
                scenes = ScenesEnabled(scenes),
                locationPathName = Path.Combine(dir, "LastEmber.exe"),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.CompressWithLz4HC,
            };

            Debug.Log($"[SteamBuildSetup] Building → {opts.locationPathName}");
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[SteamBuildSetup] Result: {report.summary.result}  Size: {report.summary.totalSize / (1024 * 1024)} MB  Time: {report.summary.totalTime}");
        }

        static string[] ScenesEnabled(EditorBuildSettingsScene[] scenes)
        {
            int n = 0;
            for (int i = 0; i < scenes.Length; i++) if (scenes[i].enabled) n++;
            var result = new string[n];
            int j = 0;
            for (int i = 0; i < scenes.Length; i++) if (scenes[i].enabled) result[j++] = scenes[i].path;
            return result;
        }

        [MenuItem(Menu + "Log Current Build Settings")]
        public static void LogSettings()
        {
            var nbt = NamedBuildTarget.Standalone;
            Debug.Log(
                $"[SteamBuildSetup] Standalone scripting backend = {PlayerSettings.GetScriptingBackend(nbt)}\n" +
                $"API compatibility = {PlayerSettings.GetApiCompatibilityLevel(nbt)}\n" +
                $"Managed stripping = {PlayerSettings.GetManagedStrippingLevel(nbt)}\n" +
                $"IL2CPP compiler config = {PlayerSettings.GetIl2CppCompilerConfiguration(nbt)}\n" +
                $"IL2CPP code generation = {PlayerSettings.GetIl2CppCodeGeneration(nbt)}\n" +
                $"Strip engine code = {PlayerSettings.stripEngineCode}\n" +
                $"Incremental GC = {PlayerSettings.gcIncremental}\n" +
                $"Active target = {EditorUserBuildSettings.activeBuildTarget}");
        }
    }
}
