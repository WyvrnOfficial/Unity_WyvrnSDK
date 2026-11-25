// Copyright 2017-2025 Razer, Inc. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.IO;

using UnityEditor;
using UnityEngine;

namespace WyvrnSDK.Haptics.EditorTools
{
    /// <summary>
    /// Pulls a WYVRN haptic folder into the project: reads its WYVRN.config and the
    /// .haps files beside it, and bakes them into a <see cref="WyvrnHapticData"/> asset
    /// plus one <see cref="WyvrnHapticEffect"/> per distinct effect.
    ///
    /// The baked assets are the whole runtime contract - the PS5 backend reads nothing
    /// else - so a title ships without any loose WYVRN files.
    /// </summary>
    public static class WyvrnConfigImporter
    {
        /// <summary>
        /// Config file names to look for in a haptic folder, in order of preference.
        /// Synapse installs it lowercase ("wyvrn.config"), the Unreal/Unity samples ship
        /// it uppercase, and older Chroma-only sample folders call it "Effects.config".
        /// Matched case-insensitively, because a Unity editor on macOS or Linux will not
        /// forgive the difference the way Windows does.
        /// </summary>
        private static readonly string[] ConfigFileNames = { "wyvrn.config", "effects.config" };

        /// <summary>Result of one import run.</summary>
        public struct Result
        {
            /// <summary>The baked asset, or null when the import failed.</summary>
            public WyvrnHapticData Data;

            /// <summary>Commands baked (events that carry haptics or interrupts).</summary>
            public int CommandCount;

            /// <summary>Distinct .haps files imported as effect assets.</summary>
            public int EffectCount;

            /// <summary>Set when the import failed; empty on success.</summary>
            public string Error;

            public bool Succeeded
            {
                get { return Data != null && string.IsNullOrEmpty(Error); }
            }
        }

        /// <summary>
        /// Bakes sourceFolder into the project. settings supplies the output location;
        /// pass WyvrnImportSettings.instance unless a test needs its own.
        /// </summary>
        public static Result ImportFromFolder(string sourceFolder, WyvrnImportSettings settings)
        {
            Result result = new Result();

            if (string.IsNullOrEmpty(sourceFolder) || !Directory.Exists(sourceFolder))
            {
                result.Error = string.Format("WyvrnConfigImporter: '{0}' is not an existing folder.", sourceFolder);
                return result;
            }

            string configPath = FindConfigFile(sourceFolder);
            if (configPath == null)
            {
                result.Error = string.Format(
                    "WyvrnConfigImporter: no WYVRN.config found in '{0}'.", sourceFolder);
                return result;
            }

            string configJson;
            try
            {
                configJson = File.ReadAllText(configPath);
            }
            catch (IOException exception)
            {
                result.Error = string.Format("WyvrnConfigImporter: could not read '{0}' - {1}", configPath, exception.Message);
                return result;
            }

            List<WyvrnParsedCommand> parsedCommands = new List<WyvrnParsedCommand>();
            string parseError;
            if (!WyvrnConfigParser.Parse(configJson, parsedCommands, out parseError))
            {
                result.Error = parseError;
                return result;
            }

            string outputFolder = settings.GetOutputFolder();
            string effectFolder = settings.GetEffectFolder();

            if (!IsResourcesPath(settings.ResourcesRoot))
            {
                result.Error = string.Format(
                    "WyvrnConfigImporter: Resources Root '{0}' is not a Resources folder. The baked haptics would be " +
                    "stripped from player builds and no events would play - set it to a folder named 'Resources' in " +
                    "Project Settings > WYVRN > Haptics Import.", settings.ResourcesRoot);
                return result;
            }

            string folderError;
            if (!EnsureAssetFolder(effectFolder, out folderError))
            {
                result.Error = folderError;
                return result;
            }

            // One directory listing, matched case-insensitively, instead of a File.Exists
            // per effect: same reason as the config lookup, plus it keeps the import to a
            // single filesystem walk.
            Dictionary<string, string> hapsByName = IndexHapsFiles(sourceFolder);

            Dictionary<string, WyvrnHapticEffect> effectsByName =
                new Dictionary<string, WyvrnHapticEffect>(StringComparer.OrdinalIgnoreCase);
            List<WyvrnHapticCommand> commands = new List<WyvrnHapticCommand>();

            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < parsedCommands.Count; ++i)
                {
                    WyvrnParsedCommand parsedCommand = parsedCommands[i];

                    WyvrnHapticCommand command = new WyvrnHapticCommand();
                    command.EventName = parsedCommand.EventName;
                    command.InterruptCommands = new List<string>(parsedCommand.InterruptCommands);
                    command.InterruptAll = parsedCommand.InterruptAll;

                    for (int j = 0; j < parsedCommand.Effects.Count; ++j)
                    {
                        WyvrnParsedEffect parsedEffect = parsedCommand.Effects[j];

                        List<WyvrnHapticTarget> targets = new List<WyvrnHapticTarget>();
                        float gain = 1.0f;
                        bool hasControllerTarget = false;

                        for (int t = 0; t < parsedEffect.Targeting.Count; ++t)
                        {
                            WyvrnParsedTargeting targeting = parsedEffect.Targeting[t];
                            if (!IsDualSenseTarget(targeting.Target))
                            {
                                continue;
                            }

                            // Preserve the lateral side; dedup on the (region, side) pair so
                            // Hand-Left and Hand-Right both survive but duplicates collapse.
                            WyvrnHapticTarget target = new WyvrnHapticTarget(targeting.Target, targeting.Side);
                            if (!targets.Contains(target))
                            {
                                targets.Add(target);
                            }

                            // HAR intensity is per-event (one material id per effect+target-set), so an
                            // event carries a single Gain: take the first controller target's. Per-side
                            // gains (e.g. Left 0.5 / Right 1.0) collapse to this one value.
                            if (!hasControllerTarget)
                            {
                                gain = targeting.Gain;
                                hasControllerTarget = true;
                            }
                        }

                        if (!hasControllerTarget)
                        {
                            continue;
                        }

                        WyvrnHapticEffect effect;
                        if (!effectsByName.TryGetValue(parsedEffect.EffectName, out effect))
                        {
                            string effectError;
                            effect = ImportHapticEffect(hapsByName, effectFolder, parsedEffect.EffectName, out effectError);
                            if (effect == null)
                            {
                                if (!string.IsNullOrEmpty(effectError))
                                {
                                    result.Error = effectError;
                                    return result;
                                }
                                // The .haps is simply absent: skip this effect, keep importing.
                                continue;
                            }
                            effectsByName.Add(parsedEffect.EffectName, effect);
                        }

                        WyvrnHapticEvent effectEntry = new WyvrnHapticEvent();
                        effectEntry.Effect = effect;
                        effectEntry.Gain = gain;
                        effectEntry.Loop = parsedEffect.Loop;
                        effectEntry.Priority = parsedEffect.Priority;
                        effectEntry.Mixing = parsedEffect.Mixing;
                        effectEntry.Targets = targets;
                        command.Effects.Add(effectEntry);
                    }

                    if (command.Effects.Count > 0 || command.InterruptCommands.Count > 0 || command.InterruptAll)
                    {
                        commands.Add(command);
                    }
                }

                string dataError;
                WyvrnHapticData data = WriteHapticData(outputFolder, commands, sourceFolder, out dataError);
                if (data == null)
                {
                    result.Error = dataError;
                    return result;
                }

                result.Data = data;
                result.CommandCount = commands.Count;
                result.EffectCount = effectsByName.Count;
            }
            finally
            {
                // Must pair with StartAssetEditing even on the early returns above, or the
                // editor is left with the asset database locked.
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log(string.Format(
                "WyvrnConfigImporter: baked {0} command(s) and {1} effect(s) from '{2}' into '{3}'.",
                result.CommandCount, result.EffectCount, sourceFolder, outputFolder));
            return result;
        }

        /// <summary>
        /// True when assetPath is, or ends in, a folder named "Resources" - Unity's rule
        /// for what gets included in a player build without being referenced.
        /// </summary>
        public static bool IsResourcesPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }
            string normalized = assetPath.Replace('\\', '/').TrimEnd('/');
            return normalized.EndsWith("/Resources", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "Resources", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The DualSense provider renders the hand region (left + right actuators);
        /// other body targets in the config produce nothing on PS5 and are dropped.
        /// </summary>
        private static bool IsDualSenseTarget(WyvrnHapticRegion region)
        {
            return region == WyvrnHapticRegion.Hand;
        }

        /// <summary>Returns the config file inside folder, or null when there is none.</summary>
        private static string FindConfigFile(string folder)
        {
            string[] candidates;
            try
            {
                candidates = Directory.GetFiles(folder, "*.config", SearchOption.TopDirectoryOnly);
            }
            catch (IOException)
            {
                return null;
            }

            for (int preference = 0; preference < ConfigFileNames.Length; ++preference)
            {
                for (int i = 0; i < candidates.Length; ++i)
                {
                    if (string.Equals(Path.GetFileName(candidates[i]), ConfigFileNames[preference],
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return candidates[i];
                    }
                }
            }

            // An unrecognised name is still unambiguous when it is the only config present.
            return candidates.Length == 1 ? candidates[0] : null;
        }

        /// <summary>Maps effect name (case-insensitive, no extension) to its .haps full path.</summary>
        private static Dictionary<string, string> IndexHapsFiles(string folder)
        {
            Dictionary<string, string> index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] files;
            try
            {
                files = Directory.GetFiles(folder, "*.haps", SearchOption.TopDirectoryOnly);
            }
            catch (IOException)
            {
                return index;
            }

            for (int i = 0; i < files.Length; ++i)
            {
                string name = Path.GetFileNameWithoutExtension(files[i]);
                if (!string.IsNullOrEmpty(name) && !index.ContainsKey(name))
                {
                    index.Add(name, files[i]);
                }
            }
            return index;
        }

        /// <summary>
        /// Reads &lt;sourceFolder&gt;/&lt;effectName&gt;.haps and saves it as a WyvrnHapticEffect.
        /// Returns null with an empty outError when the file is simply absent, so the
        /// caller can skip that effect; outError is set only on a hard failure.
        /// </summary>
        private static WyvrnHapticEffect ImportHapticEffect(
            Dictionary<string, string> hapsByName, string effectFolder, string effectName, out string outError)
        {
            outError = null;

            string hapsPath;
            if (!hapsByName.TryGetValue(effectName, out hapsPath))
            {
                Debug.LogWarning(string.Format(
                    "WyvrnConfigImporter: missing '{0}.haps'; skipping effect '{1}'.", effectName, effectName));
                return null;
            }

            string json;
            try
            {
                json = File.ReadAllText(hapsPath);
            }
            catch (IOException exception)
            {
                outError = string.Format("WyvrnConfigImporter: could not read '{0}' - {1}", hapsPath, exception.Message);
                return null;
            }

            bool hasStiffness = WyvrnHapsFile.HasStiffnessTrack(json);

            string assetPath = WyvrnImportSettings.CombineForward(effectFolder, effectName + ".asset");

            // Reuse an existing asset rather than replacing it, so references from the
            // data asset (and from anything else in the project) survive a reimport.
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

            if (hasStiffness)
            {
                Debug.Log(string.Format(
                    "WyvrnConfigImporter: effect '{0}' carries a Stiffness track; its events will drive the DualSense adaptive triggers.",
                    effectName));
            }

            return effect;
        }

        /// <summary>Creates or updates the single baked data asset.</summary>
        private static WyvrnHapticData WriteHapticData(
            string outputFolder, List<WyvrnHapticCommand> commands, string sourceFolder, out string outError)
        {
            outError = null;

            string assetPath = WyvrnImportSettings.CombineForward(outputFolder, "WyvrnHapticData.asset");

            WyvrnHapticData data = AssetDatabase.LoadAssetAtPath<WyvrnHapticData>(assetPath);
            bool created = data == null;
            if (created)
            {
                data = ScriptableObject.CreateInstance<WyvrnHapticData>();
            }

            // A reimport replaces the contents wholesale rather than appending.
            data.SetImportedData(commands, GetSourceAppName(sourceFolder));

            if (created)
            {
                AssetDatabase.CreateAsset(data, assetPath);
            }
            else
            {
                EditorUtility.SetDirty(data);
            }

            return data;
        }

        /// <summary>The haptic folder's own name, e.g. "Game Sample Application".</summary>
        private static string GetSourceAppName(string sourceFolder)
        {
            string normalized = sourceFolder.Replace('\\', '/').TrimEnd('/');
            int slash = normalized.LastIndexOf('/');
            return slash >= 0 ? normalized.Substring(slash + 1) : normalized;
        }

        /// <summary>
        /// Makes sure assetFolder (a project-relative path such as
        /// "Assets/Resources/Wyvrn/HapticEffect") exists, creating each missing level.
        /// </summary>
        private static bool EnsureAssetFolder(string assetFolder, out string outError)
        {
            outError = null;

            string normalized = assetFolder.Replace('\\', '/').TrimEnd('/');
            if (!normalized.StartsWith("Assets", StringComparison.Ordinal))
            {
                outError = string.Format(
                    "WyvrnConfigImporter: output folder '{0}' must be inside the project's Assets folder.", assetFolder);
                return false;
            }

            string[] parts = normalized.Split('/');
            string path = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; ++i)
            {
                if (string.IsNullOrEmpty(parts[i]))
                {
                    continue;
                }
                string next = path + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    string guid = AssetDatabase.CreateFolder(path, parts[i]);
                    if (string.IsNullOrEmpty(guid))
                    {
                        outError = string.Format("WyvrnConfigImporter: could not create folder '{0}'.", next);
                        return false;
                    }
                }
                path = next;
            }
            return true;
        }
    }
}
