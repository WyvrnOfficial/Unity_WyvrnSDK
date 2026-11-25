// Copyright 2017-2025 Razer, Inc. All Rights Reserved.

#if UNITY_PS5

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WyvrnSDK.Haptics
{
    /// <summary>
    /// PS5 concrete IInterhapticsRuntime. Calls the Interhaptics HAR engine (HAR.prx)
    /// and the DualSense provider (Provider_DualSensePS5.prx) through P/Invoke; Unity
    /// loads both from the PS5 plugin folder (see Plugins/PS5/README.md).
    ///
    /// Inert when the PRX are absent: the first entry-point call raises
    /// DllNotFoundException, Initialize() catches it and returns false, IsAvailable
    /// stays false, and every other call is a no-op. The title therefore runs with
    /// silent haptics instead of failing when the libraries were not staged.
    /// </summary>
    public sealed class InterhapticsRuntime : IInterhapticsRuntime
    {
        // Library names, kept in one place because the Interhaptics SDK changed its PS5
        // file naming between vintages: SDK 7-9 ship libHAR.prx / libProvider_DualSensePS5.prx
        // while SDK 11-12 ship HAR.prx / Provider_DualSensePS5.prx. These match the newer
        // naming; on an older SDK either drop the "lib" prefix when staging the files or
        // change these two lines. See Plugins/PS5/README.md.
        private const string HarLibrary = "HAR";
        private const string ProviderLibrary = "Provider_DualSensePS5";

        #region HAR engine (HAR.prx)

        [DllImport(HarLibrary, EntryPoint = "Init")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool HarInit();

        [DllImport(HarLibrary, EntryPoint = "Quit")]
        private static extern void HarQuit();

        // const char* content: marshalled as an explicitly NUL-terminated UTF-8 byte
        // array. Default string marshalling would hand HAR the platform's ANSI encoding
        // and mangle any non-ASCII text inside a .haps.
        [DllImport(HarLibrary, EntryPoint = "AddHM")]
        private static extern int HarAddHM(byte[] content);

        [DllImport(HarLibrary, EntryPoint = "PlayEvent")]
        private static extern void HarPlayEvent(int materialId, double vibrationOffset, double textureOffset, double stiffnessOffset);

        [DllImport(HarLibrary, EntryPoint = "StopEvent")]
        private static extern void HarStopEvent(int materialId);

        [DllImport(HarLibrary, EntryPoint = "StopAllEvents")]
        private static extern void HarStopAllEvents();

        [DllImport(HarLibrary, EntryPoint = "AddTargetToEventMarshal")]
        private static extern void HarAddTargetToEventMarshal(int materialId, [In] HARCommandData[] targets, int size);

        [DllImport(HarLibrary, EntryPoint = "ComputeAllEvents")]
        private static extern void HarComputeAllEvents(double currentTime);

        [DllImport(HarLibrary, EntryPoint = "SetEventIntensity")]
        private static extern void HarSetEventIntensity(int materialId, double intensity);

        [DllImport(HarLibrary, EntryPoint = "SetGlobalIntensity")]
        private static extern void HarSetGlobalIntensity(double intensity);

        [DllImport(HarLibrary, EntryPoint = "SetEventLoop")]
        private static extern void HarSetEventLoop(int materialId, int numLoops);

        [DllImport(HarLibrary, EntryPoint = "GetVibrationLength")]
        private static extern double HarGetVibrationLength(int materialId);

        #endregion

        #region DualSense provider (Provider_DualSensePS5.prx)

        [DllImport(ProviderLibrary, EntryPoint = "ProviderInit")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool ProviderInitialize();

        [DllImport(ProviderLibrary, EntryPoint = "ProviderClean")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool ProviderClean();

        [DllImport(ProviderLibrary, EntryPoint = "ProviderRenderHaptics")]
        private static extern void ProviderRenderHaptics();

        // C++ "bool isLeft" is one byte; marshal it as I1 rather than the 4-byte Win32
        // BOOL that the default marshaller would use.
        [DllImport(ProviderLibrary, EntryPoint = "startTriggerEffect")]
        private static extern int ProviderStartTriggerEffect(int id, [MarshalAs(UnmanagedType.I1)] bool isLeft);

        [DllImport(ProviderLibrary, EntryPoint = "stopTriggerEffect")]
        private static extern int ProviderStopTriggerEffect([MarshalAs(UnmanagedType.I1)] bool isLeft);

        #endregion

        private bool available;

        /// <summary>
        /// False when the loaded provider PRX predates the adaptive-trigger exports, so
        /// the triggers degrade to no-ops instead of throwing on every arm. Probed once
        /// at Initialize; never half-enabled, because a started effect that cannot be
        /// stopped would pin a trigger's resistance for the rest of the session.
        /// </summary>
        private bool triggersAvailable;

        /// <summary>
        /// Last value handed to SetGlobalIntensity. Remembered because HAR applies the
        /// global intensity to the Stiffness envelope as well as to vibration, and the
        /// general haptics gain is deliberately vibration-only: StartTriggerEffect
        /// neutralises the intensity for the duration of the arm and restores it from here.
        /// </summary>
        private float globalIntensity = 1.0f;

        public bool IsAvailable
        {
            get { return available; }
        }

        public bool Initialize()
        {
            if (available)
            {
                return true;
            }

            bool engineInit;
            bool providerInit;
            try
            {
                engineInit = HarInit();
                providerInit = ProviderInitialize();
            }
            catch (DllNotFoundException exception)
            {
                WyvrnHapticsLog.Warning("Interhaptics: HAR.prx / Provider_DualSensePS5.prx not loadable; haptics inert. " + exception.Message);
                return false;
            }
            catch (EntryPointNotFoundException exception)
            {
                WyvrnHapticsLog.Warning("Interhaptics: a HAR entry point is missing; haptics inert. " + exception.Message);
                return false;
            }

            WyvrnHapticsLog.Info(string.Format("Interhaptics: Init()={0} ProviderInit()={1}", engineInit, providerInit));
            if (!providerInit)
            {
                WyvrnHapticsLog.Warning("Interhaptics: ProviderInit() failed; haptics inert.");
                return false;
            }

            // The adaptive-trigger exports are newer than the rest of the provider
            // surface. Probe them with a release call - harmless on an unarmed trigger,
            // and it leaves the pad neutral at startup - so an older PRX raises
            // EntryPointNotFoundException here once instead of on every later arm.
            triggersAvailable = true;
            try
            {
                ProviderStopTriggerEffect(true);
                ProviderStopTriggerEffect(false);
            }
            catch (EntryPointNotFoundException)
            {
                triggersAvailable = false;
                WyvrnHapticsLog.Warning("Interhaptics: provider has no adaptive-trigger exports; adaptive triggers inert.");
            }

            available = true;
            WyvrnHapticsLog.Info("Interhaptics HAR runtime initialized.");
            return true;
        }

        public void Shutdown()
        {
            if (available)
            {
                try
                {
                    ProviderClean();
                    HarQuit();
                }
                catch (DllNotFoundException)
                {
                    // Unloaded underneath us; nothing left to tear down.
                }
            }
            triggersAvailable = false;
            available = false;
        }

        public int AddMaterial(string materialJson)
        {
            if (!available || string.IsNullOrEmpty(materialJson))
            {
                return -1;
            }
            return HarAddHM(ToUtf8(materialJson));
        }

        public void SetIntensity(int materialId, float intensity)
        {
            if (available)
            {
                HarSetEventIntensity(materialId, intensity);
            }
        }

        public void SetGlobalIntensity(float intensity)
        {
            // Kept even when HAR is inert so StartTriggerEffect always has a value to restore.
            globalIntensity = intensity;

            if (available)
            {
                HarSetGlobalIntensity(intensity);
            }
        }

        public void SetLoop(int materialId, int numLoops)
        {
            if (available)
            {
                HarSetEventLoop(materialId, numLoops);
            }
        }

        public void AddTarget(int materialId, WyvrnHapticRegion region, WyvrnHapticSide side)
        {
            // The DualSense provider renders the hand region (left + right palm); others are ignored.
            if (!available || region != WyvrnHapticRegion.Hand)
            {
                return;
            }

            HARLateralFlag lateralFlag;
            switch (side)
            {
                case WyvrnHapticSide.Left:
                    lateralFlag = HARLateralFlag.Left;
                    break;
                case WyvrnHapticSide.Right:
                    lateralFlag = HARLateralFlag.Right;
                    break;
                default:
                    lateralFlag = HARLateralFlag.Global;
                    break;
            }

            HARCommandData[] command = new HARCommandData[1];
            command[0] = new HARCommandData(HAROperator.Plus, HARGroupID.Hand, lateralFlag);
            HarAddTargetToEventMarshal(materialId, command, 1);
        }

        public void Play(int materialId, double timeSeconds)
        {
            if (available)
            {
                // HAR computes playback position as (offset + curTime) and does NOT subtract the
                // event's start time, so offset 0 renders at the absolute engine time - well past
                // a 1-2s effect's end, i.e. silence. Passing -timeSeconds makes the position
                // (curTime - timeSeconds) = time since the trigger, so the effect plays from its
                // start. (If HAR is fixed to subtract m_startingTime, this offset reverts to 0.)
                HarPlayEvent(materialId, -timeSeconds, -timeSeconds, -timeSeconds);
            }
        }

        public void Stop(int materialId)
        {
            if (available)
            {
                HarStopEvent(materialId);
            }
        }

        public void StopAll()
        {
            if (available)
            {
                HarStopAllEvents();
            }
        }

        public double GetLength(int materialId)
        {
            if (available)
            {
                return HarGetVibrationLength(materialId);
            }
            return 0.0;
        }

        public void Render(double timeSeconds)
        {
            if (available)
            {
                HarComputeAllEvents(timeSeconds);
                ProviderRenderHaptics();
            }
        }

        public bool StartTriggerEffect(int materialId, bool leftTrigger)
        {
            if (!available || !triggersAvailable)
            {
                return false;
            }

            // The provider samples the material's Stiffness envelope synchronously inside
            // startTriggerEffect (GetStiffnessAmp per trigger position), and HAR feeds that
            // sampling the SAME global intensity as vibration:
            //     HManager::GetStiffness -> HMaterial::EvaluateStiffness(step, m_globalIntensity)
            // The general haptics gain is deliberately vibration-only, so neutralise the
            // intensity across the arm and restore it immediately after. No locking needed:
            // every HAR call, including this one, is made on the render worker thread.
            bool neutralise = globalIntensity != 1.0f;
            if (neutralise)
            {
                HarSetGlobalIntensity(1.0);
            }

            // The provider forwards scePadSetTriggerEffect's result: negative when the
            // pad rejected the effect (e.g. controller not connected yet).
            bool started = ProviderStartTriggerEffect(materialId, leftTrigger) >= 0;

            if (neutralise)
            {
                HarSetGlobalIntensity(globalIntensity);
            }

            return started;
        }

        public void StopTriggerEffect(bool leftTrigger)
        {
            if (available && triggersAvailable)
            {
                ProviderStopTriggerEffect(leftTrigger);
            }
        }

        /// <summary>NUL-terminated UTF-8 bytes for a HAR "const char*" parameter.</summary>
        private static byte[] ToUtf8(string text)
        {
            int byteCount = Encoding.UTF8.GetByteCount(text);
            byte[] bytes = new byte[byteCount + 1];
            Encoding.UTF8.GetBytes(text, 0, text.Length, bytes, 0);
            bytes[byteCount] = 0;
            return bytes;
        }
    }
}

#endif // UNITY_PS5
