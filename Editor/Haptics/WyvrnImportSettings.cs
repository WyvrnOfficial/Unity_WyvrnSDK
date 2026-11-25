// Copyright 2017-2025 Razer, Inc. All Rights Reserved.

using System.IO;

using UnityEditor;
using UnityEngine;

namespace WyvrnSDK.Haptics.EditorTools
{
    /// <summary>
    /// Project settings for the WYVRN haptics importer (Project Settings → WYVRN →
    /// Haptics Import). The importer's default source folder is
    /// &lt;HapticFolderRoot&gt;/&lt;AppFolderName&gt;; the confirm dialog lets the user accept
    /// it or browse to another.
    ///
    /// Stored in ProjectSettings/ rather than under Assets/, so importer configuration
    /// never ships inside the game's asset database.
    /// </summary>
    [FilePath("ProjectSettings/WyvrnHapticsImportSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class WyvrnImportSettings : ScriptableSingleton<WyvrnImportSettings>
    {
        /// <summary>Base folder Synapse/Interhaptics installs per-app WYVRN haptic folders under.</summary>
        [SerializeField]
        private string hapticFolderRoot = DefaultHapticFolderRoot;

        /// <summary>
        /// WYVRN app folder name (e.g. "Game Sample Application"). Empty falls back to
        /// the Unity product name, which is what a title's folder is usually named after.
        /// </summary>
        [SerializeField]
        private string appFolderName = string.Empty;

        /// <summary>
        /// Resources folder the baked assets are written under. It MUST be a Unity
        /// "Resources" folder: the backend loads the baked data with Resources.Load at
        /// runtime, and nothing in the project references it, so anywhere else it would
        /// be stripped from the player build. (This is the Unity counterpart of the
        /// Unreal importer adding its output path to DirectoriesToAlwaysCook.)
        /// </summary>
        [SerializeField]
        private string resourcesRoot = DefaultResourcesRoot;

        public const string DefaultHapticFolderRoot = "C:/Program Files (x86)/Interhaptics/hapticFolders";
        public const string DefaultResourcesRoot = "Assets/Resources";

        public string HapticFolderRoot
        {
            get { return string.IsNullOrEmpty(hapticFolderRoot) ? DefaultHapticFolderRoot : hapticFolderRoot; }
            set { hapticFolderRoot = value; }
        }

        public string AppFolderName
        {
            get { return appFolderName; }
            set { appFolderName = value; }
        }

        public string ResourcesRoot
        {
            get { return string.IsNullOrEmpty(resourcesRoot) ? DefaultResourcesRoot : resourcesRoot; }
            set { resourcesRoot = value; }
        }

        /// <summary>Resolved default source folder: HapticFolderRoot / AppFolderName.</summary>
        public string GetDiscoveredFolder()
        {
            string appName = string.IsNullOrEmpty(appFolderName) ? Application.productName : appFolderName;
            return CombineForward(HapticFolderRoot, appName);
        }

        /// <summary>
        /// Project-relative folder the baked assets go to:
        /// &lt;ResourcesRoot&gt;/&lt;directory of WyvrnHapticData.ResourcePath&gt;. Derived rather
        /// than configured outright so the written location and the runtime's
        /// Resources.Load path can never drift apart.
        /// </summary>
        public string GetOutputFolder()
        {
            string subFolder = Path.GetDirectoryName(WyvrnHapticData.ResourcePath);
            subFolder = string.IsNullOrEmpty(subFolder) ? string.Empty : subFolder.Replace('\\', '/');
            return string.IsNullOrEmpty(subFolder) ? ResourcesRoot : CombineForward(ResourcesRoot, subFolder);
        }

        /// <summary>Folder the individual imported effect assets go to.</summary>
        public string GetEffectFolder()
        {
            return CombineForward(GetOutputFolder(), "HapticEffect");
        }

        /// <summary>
        /// Writes the settings to ProjectSettings/. Named away from the base class's
        /// Save(bool) so there is no overload to resolve at the call sites.
        /// </summary>
        public void Persist()
        {
            // true: write even when nothing in the editor marked the object dirty.
            Save(true);
        }

        /// <summary>
        /// Path.Combine with forward slashes. Unity's asset paths are always
        /// forward-slashed, including on Windows, and AssetDatabase rejects backslashes.
        /// </summary>
        internal static string CombineForward(string left, string right)
        {
            if (string.IsNullOrEmpty(left))
            {
                return right ?? string.Empty;
            }
            if (string.IsNullOrEmpty(right))
            {
                return left;
            }
            return left.TrimEnd('/', '\\') + "/" + right.TrimStart('/', '\\');
        }
    }

    /// <summary>
    /// Renders <see cref="WyvrnImportSettings"/> in the Project Settings window and
    /// offers the import action next to it, so the whole workflow lives in one place.
    /// </summary>
    internal static class WyvrnImportSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider Create()
        {
            SettingsProvider provider = new SettingsProvider("Project/WYVRN/Haptics Import", SettingsScope.Project)
            {
                label = "Haptics Import",
                guiHandler = OnGui,
                keywords = new[] { "WYVRN", "Razer", "haptics", "Interhaptics", "haps", "PS5", "DualSense" },
            };
            return provider;
        }

        private static void OnGui(string searchContext)
        {
            WyvrnImportSettings settings = WyvrnImportSettings.instance;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();

            string root = EditorGUILayout.TextField(
                new GUIContent("Haptic Folder Root", "Base folder Synapse / Interhaptics installs per-app WYVRN haptic folders under."),
                settings.HapticFolderRoot);

            string appFolder = EditorGUILayout.TextField(
                new GUIContent("App Folder Name", "WYVRN app folder name. Leave empty to use the Unity product name."),
                settings.AppFolderName);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);

            string resourcesRoot = EditorGUILayout.TextField(
                new GUIContent("Resources Root", "Must be a Unity Resources folder: the runtime loads the baked data with Resources.Load."),
                settings.ResourcesRoot);

            if (EditorGUI.EndChangeCheck())
            {
                settings.HapticFolderRoot = root;
                settings.AppFolderName = appFolder;
                settings.ResourcesRoot = resourcesRoot;
                settings.Persist();
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Discovered Folder", settings.GetDiscoveredFolder());
                EditorGUILayout.TextField("Baked To", settings.GetOutputFolder());
            }

            if (!WyvrnConfigImporter.IsResourcesPath(settings.ResourcesRoot))
            {
                EditorGUILayout.HelpBox(
                    "Resources Root must be a folder named 'Resources' (or a path ending in one), " +
                    "otherwise the baked haptics are stripped from player builds and no events will play.",
                    MessageType.Error);
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Import WYVRN Haptics...", GUILayout.Height(24.0f)))
            {
                WyvrnHapticsImportMenu.RunInteractive();
            }
        }
    }
}
