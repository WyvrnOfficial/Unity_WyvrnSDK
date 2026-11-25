// Copyright 2017-2025 Razer, Inc. All Rights Reserved.

using UnityEditor.Build;
using UnityEditor.Build.Reporting;

using UnityEngine;

namespace WyvrnSDK.Haptics.EditorTools
{
    /// <summary>
    /// Warns, at the start of a PS5 build, when the haptics have not been imported.
    ///
    /// This is the one setup step whose absence is invisible: the build succeeds, the
    /// title boots, HAR comes up, and every event is then silently dropped because the
    /// command table it looks up is not in the player. Catching it here turns a
    /// "haptics don't work on the devkit" hunt into a line in the build log.
    ///
    /// Deliberately a warning rather than a failed build: a project may legitimately
    /// build a PS5 player before its haptic folder exists, and breaking that would be
    /// worse than letting it through with a notice.
    /// </summary>
    public sealed class WyvrnHapticsBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder
        {
            get { return 0; }
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report == null)
            {
                return;
            }

            // Compared by name rather than against BuildTarget.PS5: that enum member only
            // exists when the PS5 platform module is installed, so naming it directly would
            // stop this file compiling for everyone who does not have it.
            if (report.summary.platform.ToString() != "PS5")
            {
                return;
            }

            // Exactly the lookup the runtime backend performs, so this cannot disagree
            // with it about where the data has to be.
            WyvrnHapticData data = Resources.Load<WyvrnHapticData>(WyvrnHapticData.ResourcePath);
            if (data == null)
            {
                Debug.LogWarning(string.Format(
                    "WyvrnSDK: building for PS5 with no imported haptics - 'Resources/{0}' is missing, so the " +
                    "title will run with HAR loaded but every WYVRN event dropped. Run Tools > WYVRN > " +
                    "Import WYVRN Haptics... before building.",
                    WyvrnHapticData.ResourcePath));
                return;
            }

            if (data.Commands.Count == 0)
            {
                Debug.LogWarning(
                    "WyvrnSDK: building for PS5 with imported haptics that contain no commands. Check that the " +
                    "WYVRN.config the importer read actually carries Haptic_Events.");
                return;
            }

            Debug.Log(string.Format("WyvrnSDK: building for PS5 with {0} imported haptic command(s).",
                data.Commands.Count));
        }
    }
}
