// Copyright 2017-2025 Razer, Inc. All Rights Reserved.

namespace WyvrnSDK.Haptics
{
    /// <summary>
    /// Platform haptic backend selected by WyvrnAPI. Windows routes events to the
    /// RzChromatic DLL (Synapse-hosted); PS5 routes them to the Interhaptics HAR
    /// runtime on the DualSense.
    /// </summary>
    public interface IWyvrnHapticBackend
    {
        /// <summary>
        /// Brings the backend up. Returns false when the platform runtime is
        /// unavailable (e.g. the PRX libraries are missing), in which case the SDK
        /// behaves as a no-op.
        /// </summary>
        bool Initialize();

        void Shutdown();

        bool IsInitialized { get; }

        /// <summary>Fires a named WYVRN event (External_Command_ID).</summary>
        void SetEventName(string eventName);

        // Three independent haptic controls. All are accepted at any time - including
        // before Initialize() - and re-applied to the platform runtime on every bring-up,
        // so they survive an Initialize()/Shutdown() cycle.

        /// <summary>
        /// Master switch. Off stops anything playing, releases both adaptive triggers, and
        /// skips further events outright rather than playing them inaudibly. Turning it
        /// back on does not resume what was stopped.
        /// </summary>
        void SetHapticsEnabled(bool enabled);

        bool AreHapticsEnabled { get; }

        /// <summary>
        /// Vibration strength, 0..100 (0 = silent, 100 = unattenuated), clamped by the
        /// implementation. Affects vibration only: 0 does not stop events or triggers.
        /// </summary>
        void SetVibrationGain(int gain0To100);

        /// <summary>The gain last accepted, already clamped to 0..100. 100 until set.</summary>
        int VibrationGain { get; }

        /// <summary>
        /// DualSense adaptive-trigger resistance on/off, independent of vibration. Off
        /// releases whatever is armed but leaves events playing and vibrating; on re-arms
        /// whatever is still claimed. Resistance is always at the authored strength - the
        /// vibration gain never scales it.
        /// </summary>
        void SetAdaptiveTriggersEnabled(bool enabled);

        bool AreAdaptiveTriggersEnabled { get; }
    }
}
