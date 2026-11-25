// Copyright 2017-2025 Razer, Inc. All Rights Reserved.

using System.Collections.Generic;

namespace WyvrnSDK.Haptics.EditorTools
{
    /// <summary>
    /// Inspects .haps (Interhaptics haptic effect) JSON at import time. The runtime
    /// hands the .haps text to HAR verbatim, so anything the SDK needs to know about
    /// an effect's content is detected here and baked onto the asset.
    /// </summary>
    public static class WyvrnHapsFile
    {
        /// <summary>
        /// True when the .haps carries an authored Stiffness track. Returns false for
        /// malformed JSON.
        ///
        /// Two file layouts have to be recognised. .haps v6 renamed the keys (dropping the
        /// "m_" prefix) and flattened the stiffness stream to a single interpolation curve,
        /// so there are no melodies or notes to walk:
        ///     "stiffness": { "gain", "interpolation_function", "keyframes": [ ... ] }
        /// HAR itself dispatches on the version key and parses both shapes (HapsFactory ->
        /// HapsParserV5/V6), so this detector has to know both as well or a v6 effect
        /// silently loses its adaptive trigger. The two spellings never coexist in one
        /// file, so probing for v6 first leaves the v5 answer below untouched.
        /// </summary>
        public static bool HasStiffnessTrack(string hapsJson)
        {
            WyvrnJsonValue root;
            string error;
            if (!WyvrnJson.TryParse(hapsJson, out root, out error) || !root.IsObject)
            {
                return false;
            }

            WyvrnJsonValue stiffnessV6;
            if (root.TryGetObject("stiffness", out stiffnessV6))
            {
                // Same "empty layer" rule as v5: the block can exist with nothing authored in it.
                List<WyvrnJsonValue> keyframes;
                return stiffnessV6.TryGetArray("keyframes", out keyframes) && keyframes.Count > 0;
            }

            WyvrnJsonValue stiffness;
            if (!root.TryGetObject("m_stiffness", out stiffness))
            {
                return false;
            }

            List<WyvrnJsonValue> melodies;
            if (!stiffness.TryGetArray("m_melodies", out melodies))
            {
                return false;
            }

            // Authoring tools leave empty layers behind (a melody with zero notes), so an
            // effect only counts as stiffness-carrying when a melody holds an actual note.
            for (int i = 0; i < melodies.Count; ++i)
            {
                WyvrnJsonValue melody = melodies[i];
                if (melody == null || !melody.IsObject)
                {
                    continue;
                }

                List<WyvrnJsonValue> notes;
                if (melody.TryGetArray("m_notes", out notes) && notes.Count > 0)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
