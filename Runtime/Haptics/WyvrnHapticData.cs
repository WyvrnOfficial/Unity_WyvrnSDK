// Copyright 2017-2025 Razer, Inc. All Rights Reserved.

using System.Collections.Generic;

using UnityEngine;

namespace WyvrnSDK.Haptics
{
    /// <summary>
    /// Baked haptics for a title: the controller-relevant slice of a WYVRN.config
    /// plus references to its imported effects.
    ///
    /// Produced at editor time by the WYVRN haptics importer and consumed at runtime
    /// by the Interhaptics backend. This is the contract between the two systems of
    /// the PS5 port.
    /// </summary>
    public class WyvrnHapticData : ScriptableObject
    {
        /// <summary>
        /// Resources-relative path the importer writes to and the runtime loads from.
        /// Living under a Resources folder is what gets the asset into the player
        /// build: nothing references it, so it would otherwise be stripped.
        /// </summary>
        public const string ResourcePath = "Wyvrn/WyvrnHapticData";

        /// <summary>Every WYVRN event that carries haptics.</summary>
        [SerializeField]
        private List<WyvrnHapticCommand> commands = new List<WyvrnHapticCommand>();

        /// <summary>WYVRN app / folder this data was baked from.</summary>
        [SerializeField]
        private string sourceAppName;

        public List<WyvrnHapticCommand> Commands
        {
            get { return commands; }
        }

        public string SourceAppName
        {
            get { return sourceAppName; }
        }

        /// <summary>Returns the command for eventName, or null if the event carries no haptics.</summary>
        public WyvrnHapticCommand FindCommand(string eventName)
        {
            for (int i = 0; i < commands.Count; ++i)
            {
                if (commands[i] != null && commands[i].EventName == eventName)
                {
                    return commands[i];
                }
            }
            return null;
        }

        /// <summary>
        /// Replaces the baked contents. Editor-time only, called by the importer; a
        /// reimport clears and refills rather than appending.
        /// </summary>
        public void SetImportedData(List<WyvrnHapticCommand> importedCommands, string appName)
        {
            commands = importedCommands ?? new List<WyvrnHapticCommand>();
            sourceAppName = appName;
        }
    }
}
