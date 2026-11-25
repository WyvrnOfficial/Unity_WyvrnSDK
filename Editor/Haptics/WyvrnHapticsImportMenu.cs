// Copyright 2017-2025 Razer, Inc. All Rights Reserved.

using System;
using System.IO;

using UnityEditor;
using UnityEngine;

namespace WyvrnSDK.Haptics.EditorTools
{
    /// <summary>
    /// Editor entry points for pulling haptics out of a WYVRN haptic folder.
    ///
    /// The interactive item is the one designers use: it proposes the folder resolved
    /// from the project settings, lets them accept it or browse elsewhere, and confirms
    /// before overwriting an earlier import. <see cref="ImportFromCommandLine"/> is the
    /// same work without any dialogs, for build pipelines.
    /// </summary>
    public static class WyvrnHapticsImportMenu
    {
        /// <summary>Command-line switch read by <see cref="ImportFromCommandLine"/>.</summary>
        private const string FolderArgument = "-wyvrnHapticFolder";

        [MenuItem("Tools/WYVRN/Import WYVRN Haptics...", false, 100)]
        public static void RunInteractive()
        {
            WyvrnImportSettings settings = WyvrnImportSettings.instance;
            string folder = settings.GetDiscoveredFolder();

            while (true)
            {
                int answer = EditorUtility.DisplayDialogComplex(
                    "Import WYVRN Haptics",
                    string.Format("WYVRN haptics will be imported from:\n\n{0}\n\nUse this folder?", folder),
                    "Import",
                    "Cancel",
                    "Pick another...");

                if (answer == 1)
                {
                    return;
                }
                if (answer == 0)
                {
                    break;
                }

                string picked = EditorUtility.OpenFolderPanel("Select the WYVRN haptic folder", folder, string.Empty);
                if (!string.IsNullOrEmpty(picked))
                {
                    folder = picked;
                }
            }

            // A previous import already baked assets here: confirm before overwriting.
            string dataAssetPath = WyvrnImportSettings.CombineForward(settings.GetOutputFolder(), "WyvrnHapticData.asset");
            if (AssetDatabase.LoadAssetAtPath<WyvrnHapticData>(dataAssetPath) != null)
            {
                bool overwrite = EditorUtility.DisplayDialog(
                    "Import WYVRN Haptics",
                    string.Format("WYVRN haptics already exist at:\n\n{0}\n\nReimport and overwrite them?", settings.GetOutputFolder()),
                    "Reimport",
                    "Cancel");
                if (!overwrite)
                {
                    return;
                }
            }

            WyvrnConfigImporter.Result result = WyvrnConfigImporter.ImportFromFolder(folder, settings);
            if (result.Succeeded)
            {
                EditorUtility.DisplayDialog(
                    "Import WYVRN Haptics",
                    string.Format("Imported {0} WYVRN command(s) and {1} effect(s) to {2}.",
                        result.CommandCount, result.EffectCount, settings.GetOutputFolder()),
                    "OK");
                Selection.activeObject = result.Data;
                EditorGUIUtility.PingObject(result.Data);
            }
            else
            {
                Debug.LogError(result.Error);
                EditorUtility.DisplayDialog(
                    "Import WYVRN Haptics",
                    "WYVRN haptics import failed:\n\n" + result.Error,
                    "OK");
            }
        }

        /// <summary>
        /// Imports one .haps straight into the project, for an effect that is not listed
        /// in a WYVRN.config yet.
        ///
        /// Deliberately a menu action rather than a ScriptedImporter on the ".haps"
        /// extension: an extension can only be claimed by one importer in a project, so
        /// claiming it here would hard-fail any project that also has the Interhaptics
        /// Unity SDK installed.
        /// </summary>
        [MenuItem("Tools/WYVRN/Import Single .haps Effect...", false, 101)]
        public static void ImportSingleEffect()
        {
            WyvrnImportSettings settings = WyvrnImportSettings.instance;

            string hapsPath = EditorUtility.OpenFilePanel(
                "Select an Interhaptics .haps effect", settings.GetDiscoveredFolder(), "haps");
            if (string.IsNullOrEmpty(hapsPath))
            {
                return;
            }

            if (!WyvrnConfigImporter.IsResourcesPath(settings.ResourcesRoot))
            {
                string message = string.Format(
                    "Resources Root '{0}' is not a Resources folder; the imported effect would be stripped from " +
                    "player builds. Fix it in Project Settings > WYVRN > Haptics Import.", settings.ResourcesRoot);
                Debug.LogError("WyvrnConfigImporter: " + message);
                EditorUtility.DisplayDialog("Import WYVRN Haptics", message, "OK");
                return;
            }

            string effectName = Path.GetFileNameWithoutExtension(hapsPath);
            string json;
            try
            {
                json = File.ReadAllText(hapsPath);
            }
            catch (IOException exception)
            {
                Debug.LogError(string.Format("WyvrnConfigImporter: could not read '{0}' - {1}", hapsPath, exception.Message));
                return;
            }

            bool hasStiffness = WyvrnHapsFile.HasStiffnessTrack(json);
            string assetPath = WyvrnImportSettings.CombineForward(settings.GetEffectFolder(), effectName + ".asset");

            WyvrnHapticEffect effect = AssetDatabase.LoadAssetAtPath<WyvrnHapticEffect>(assetPath);
            bool created = effect == null;
            if (created)
            {
                effect = ScriptableObject.CreateInstance<WyvrnHapticEffect>();
            }
            effect.SetImportedData(json, effectName, hasStiffness);

            if (created)
            {
                AssetDatabase.CreateAsset(effect, assetPath);
            }
            else
            {
                EditorUtility.SetDirty(effect);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(string.Format("WyvrnConfigImporter: imported effect '{0}' to '{1}'{2}.",
                effectName, assetPath, hasStiffness ? " (carries a Stiffness track)" : string.Empty));

            Selection.activeObject = effect;
            EditorGUIUtility.PingObject(effect);
        }

        /// <summary>
        /// Headless import for build pipelines and quick iteration. The source folder
        /// comes from "-wyvrnHapticFolder &lt;path&gt;" on the command line, or from the
        /// project settings when the switch is absent. The Unity counterpart of the
        /// Unreal plugin's "Wyvrn.ImportHaptics" console command:
        ///
        ///     Unity.exe -batchmode -quit -projectPath &lt;project&gt; \
        ///       -executeMethod WyvrnSDK.Haptics.EditorTools.WyvrnHapticsImportMenu.ImportFromCommandLine \
        ///       -wyvrnHapticFolder "C:/Program Files (x86)/Interhaptics/hapticFolders/My Game"
        ///
        /// Exits with a non-zero code on failure so a pipeline step actually fails.
        /// </summary>
        public static void ImportFromCommandLine()
        {
            WyvrnImportSettings settings = WyvrnImportSettings.instance;
            string folder = ReadFolderArgument() ?? settings.GetDiscoveredFolder();

            WyvrnConfigImporter.Result result = WyvrnConfigImporter.ImportFromFolder(folder, settings);
            if (result.Succeeded)
            {
                Debug.Log(string.Format("Wyvrn.ImportHaptics: baked {0} command(s) and {1} effect(s) from '{2}'.",
                    result.CommandCount, result.EffectCount, folder));
                return;
            }

            Debug.LogError("Wyvrn.ImportHaptics: " + result.Error);
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }

        private static string ReadFolderArgument()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; ++i)
            {
                if (string.Equals(args[i], FolderArgument, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }
    }
}
