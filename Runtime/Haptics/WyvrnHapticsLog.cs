// Copyright 2017-2025 Razer, Inc. All Rights Reserved.

using UnityEngine;

namespace WyvrnSDK.Haptics
{
    /// <summary>
    /// Logging for the PS5 haptics path (Interhaptics runtime + backend + voice pool).
    ///
    /// Trace() follows a WYVRN event all the way down that path: SetEventName on the
    /// main thread -> backend -> render-worker queue -> command lookup -> voice
    /// playback. It is OFF by default and gated on <see cref="VerboseEnabled"/>, so a
    /// shipping title pays one boolean read per call site.
    ///
    /// Enable it from game code (a debug menu, a console command, a launch argument):
    ///     WyvrnSDK.Haptics.WyvrnHapticsLog.VerboseEnabled = true;
    ///
    /// Safe to call from any thread: the main thread enqueues events, the worker
    /// drains them, and UnityEngine.Debug marshals log calls internally.
    /// </summary>
    public static class WyvrnHapticsLog
    {
        private const string Prefix = "WyvrnSDK: ";

        /// <summary>
        /// Runtime toggle for the verbose per-event trace. Equivalent to the Unreal
        /// plugin's "wyvrn.Haptics.VerboseLog" console variable. Written from the main
        /// thread and read from the render worker; a stale read only misses one trace
        /// line, so no synchronisation is warranted.
        /// </summary>
        public static volatile bool VerboseEnabled;

        /// <summary>Verbose per-event trace. Compiles to a single boolean test when off.</summary>
        public static void Trace(string message)
        {
            if (VerboseEnabled)
            {
                Debug.Log(Prefix + message);
            }
        }

        /// <summary>Lifecycle message (bring-up, teardown, preload counts). Always logged.</summary>
        public static void Info(string message)
        {
            Debug.Log(Prefix + message);
        }

        /// <summary>
        /// A condition that leaves haptics inert or degraded (missing PRX, missing baked
        /// data, failed provider init). Always logged: these are the messages someone
        /// reads when the pad stays silent.
        /// </summary>
        public static void Warning(string message)
        {
            Debug.LogWarning(Prefix + message);
        }
    }
}
