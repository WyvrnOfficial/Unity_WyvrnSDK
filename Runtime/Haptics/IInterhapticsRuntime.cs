// Copyright 2017-2025 Razer, Inc. All Rights Reserved.

namespace WyvrnSDK.Haptics
{
    /// <summary>
    /// Abstraction over the Interhaptics HAR runtime. The real implementation binds
    /// HAR.prx / Provider_DualSensePS5.prx on PS5; tests substitute a mock. Every
    /// event is keyed by the material id returned from AddMaterial (HAR's _hMaterialID).
    /// </summary>
    public interface IInterhapticsRuntime
    {
        /// <summary>HAR Init() + ProviderInit(). Returns false when the runtime is unavailable.</summary>
        bool Initialize();

        void Shutdown();

        bool IsAvailable { get; }

        /// <summary>AddHM: loads a material from its JSON, returning its id or a negative value on failure.</summary>
        int AddMaterial(string materialJson);

        /// <summary>SetEventIntensity.</summary>
        void SetIntensity(int materialId, float intensity);

        /// <summary>
        /// SetGlobalIntensity: the engine-wide master gain, multiplied into every event on
        /// top of SetIntensity (final = Global * Event). Because it sits outside the
        /// per-event channel it layers cleanly over the voice pool's priority ducking,
        /// which owns SetIntensity. 1.0 is HAR's base value and 0.0 is silence; values
        /// above 1.0 clip, so the caller clamps. HAR resets this to 1.0 on every Init()
        /// (Quit() destroys the manager that holds it), so it must be re-applied after
        /// each successful Initialize().
        ///
        /// Vibration only: HAR would also scale the Stiffness envelope by this value, but
        /// StartTriggerEffect neutralises it so adaptive-trigger resistance stays at the
        /// strength the .haps author designed.
        /// </summary>
        void SetGlobalIntensity(float intensity);

        /// <summary>SetEventLoop: 0/1 = one shot, &lt; 0 = infinite.</summary>
        void SetLoop(int materialId, int numLoops);

        /// <summary>AddTargetToEvent. The implementation maps the region + lateral side to provider endpoints.</summary>
        void AddTarget(int materialId, WyvrnHapticRegion region, WyvrnHapticSide side);

        /// <summary>
        /// Plays the event anchored to timeSeconds (the same clock fed to Render()).
        /// Restarts the event if it is already playing. HAR renders an event at
        /// (current time - play offset), so the implementation passes -timeSeconds as the
        /// offset; otherwise the effect renders at the absolute engine time, past its end.
        /// </summary>
        void Play(int materialId, double timeSeconds);

        /// <summary>StopEvent.</summary>
        void Stop(int materialId);

        /// <summary>StopAllEvents.</summary>
        void StopAll();

        /// <summary>GetVibrationLength, in seconds.</summary>
        double GetLength(int materialId);

        /// <summary>ComputeAllEvents(Time) followed by ProviderRenderHaptics(). Call once per frame.</summary>
        void Render(double timeSeconds);

        /// <summary>
        /// DualSense adaptive trigger (provider startTriggerEffect): samples materialId's
        /// Stiffness envelope across the trigger's travel and applies it as position
        /// feedback on L2 (leftTrigger) or R2. The resistance stays applied until
        /// StopTriggerEffect for that trigger — it does not end with playback. One
        /// trigger per call; the latest call per trigger wins. Returns false when the
        /// pad rejected the effect (e.g. no controller yet) so the caller can retry.
        ///
        /// The envelope is sampled at the material's authored strength, unaffected by
        /// SetGlobalIntensity - the general haptics gain attenuates vibration, not the
        /// physical resistance of the triggers.
        /// </summary>
        bool StartTriggerEffect(int materialId, bool leftTrigger);

        /// <summary>Releases the adaptive trigger effect on L2 (leftTrigger) or R2 (provider stopTriggerEffect).</summary>
        void StopTriggerEffect(bool leftTrigger);
    }
}
