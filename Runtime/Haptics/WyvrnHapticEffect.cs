// Copyright 2017-2025 Razer, Inc. All Rights Reserved.

using UnityEngine;

namespace WyvrnSDK.Haptics
{
    /// <summary>
    /// A single Interhaptics haptic effect imported from a .haps file.
    ///
    /// Stores the effect as JSON text so the PS5 backend can hand it verbatim to
    /// HAR AddHM(const char*) at runtime. Imported at editor time into a Resources
    /// folder, so the effect ships inside the player build and no loose files are
    /// needed on the packaged title.
    /// </summary>
    public class WyvrnHapticEffect : ScriptableObject
    {
        /// <summary>Interhaptics haptic effect JSON: the verbatim .haps contents.</summary>
        [SerializeField]
        [TextArea(3, 10)]
        private string json;

        /// <summary>Effect name this asset was imported from (e.g. "Effect1").</summary>
        [SerializeField]
        private string sourceName;

        /// <summary>
        /// True when the .haps carries an authored Stiffness track (detected at import).
        /// On PS5, playing such an event latches the stiffness envelope onto the DualSense
        /// adaptive trigger(s) it targets until the event is interrupted (designer-authored
        /// OFF command or stop-all). Assets imported before this flag existed read false;
        /// reimport to re-detect.
        /// </summary>
        [SerializeField]
        private bool hasStiffnessTrack;

        public string Json
        {
            get { return json; }
        }

        public string SourceName
        {
            get { return sourceName; }
        }

        public bool HasStiffnessTrack
        {
            get { return hasStiffnessTrack; }
        }

        /// <summary>
        /// Fills the asset from an imported .haps. Editor-time only: the runtime treats
        /// the asset as read-only, which is why the fields are private and this is the
        /// single entry point the importer uses.
        /// </summary>
        public void SetImportedData(string hapsJson, string effectName, bool carriesStiffnessTrack)
        {
            json = hapsJson;
            sourceName = effectName;
            hasStiffnessTrack = carriesStiffnessTrack;
        }
    }
}
