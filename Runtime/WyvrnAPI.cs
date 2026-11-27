using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

using UnityEngine;

namespace WyvrnSDK
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct APPINFOTYPE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Title; //TCHAR Title[256];

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
        public string Description; //TCHAR Description[1024];

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Author_Name; //TCHAR Name[256];

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Author_Contact; //TCHAR Contact[256];

        public UInt32 SupportedDevice; //DWORD SupportedDevice;

        public UInt32 Category; //DWORD Category;
    }

    public static class WyvrnAPI
    {
        // Everything down to IsWyvrnSDKAvailable() is the Windows/Xbox path: it probes
        // and binds the Synapse-hosted RzChromatic DLL. PS5 has no such DLL - events go
        // to the in-process Interhaptics HAR backend instead - and IL2CPP would try to
        // resolve these imports at link time, so the whole block is compiled out there.
#if !UNITY_PS5

#if PLATFORM_XBOXONE
#if UNITY_EDITOR
        const string DLL_NAME = "RzChromatic";
#else
        const string DLL_NAME = "RzChromatic";
#endif
#else
#if UNITY_3 || UNITY_3_0 || UNITY_3_1 || UNITY_3_2 || UNITY_3_3 || UNITY_3_4 || UNITY_3_5
        const string DLL_NAME = "RzChromatic";
#elif UNITY_64 || UNITY_EDITOR
        const string DLL_NAME = "RzChromatic64";
#else
        const string DLL_NAME = "RzChromatic";
#endif
#endif

#if ENABLE_IL2CPP

        [DllImport("version.dll", CharSet = CharSet.Unicode)]
        private static extern int GetFileVersionInfoSize(string lptstrFilename, out uint lpdwHandle);
        [DllImport("version.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetFileVersionInfo(string lptstrFilename, uint dwHandle, uint dwLen, IntPtr lpData);
        [DllImport("version.dll", CharSet = CharSet.Unicode)]
        private static extern bool VerQueryValue(IntPtr pBlock, string lpSubBlock, out IntPtr lplpBuffer, out uint puLen);

        // Define the structure to hold language and code page info
        [StructLayout(LayoutKind.Sequential)]
        private struct LangAndCodePage
        {
            public ushort wLanguage;
            public ushort wCodePage;
        }

        public static string GetProductVersion(string filePath)
        {
            uint handle;
            int size = GetFileVersionInfoSize(filePath, out handle);

            if (size == 0)
            {
                // File not found, no version info, or other error
                //Debug.LogError($"GetFileVersionInfoSize failed for {filePath}. Error: {Marshal.GetLastWin32Error()}");
                return null;
            }

            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (!GetFileVersionInfo(filePath, handle, (uint)size, buffer))
                {
                    //Debug.LogError($"GetFileVersionInfo failed for {filePath}. Error: {Marshal.GetLastWin32Error()}");
                    return null;
                }

                IntPtr pValue;
                uint len;

                // First, get the list of available translations
                if (!VerQueryValue(buffer, "\\VarFileInfo\\Translation", out pValue, out len))
                {
                    // If there are no translations, try a neutral one as a fallback.
                    // This is common for many files.
                    string subBlock = $"\\StringFileInfo\\{0409:X4}{1200:X4}\\FileVersion"; // US English, Unicode
                    if (VerQueryValue(buffer, subBlock, out pValue, out len))
                    {
                        return Marshal.PtrToStringUni(pValue);
                    }
                    //Debug.LogError("Could not find translation information");
                    return null;
                }
                
                // The length is the total size in bytes of the translation array.
                // We divide by the size of our struct to get the count.
                int translationCount = (int)len / Marshal.SizeOf(typeof(LangAndCodePage));
                LangAndCodePage[] translations = new LangAndCodePage[translationCount];
                
                // Marshal the array of structs from the pointer
                IntPtr currentPtr = pValue;
                for (int i = 0; i < translationCount; i++)
                {
                    translations[i] = (LangAndCodePage)Marshal.PtrToStructure(currentPtr, typeof(LangAndCodePage));
                    currentPtr = (IntPtr)(currentPtr.ToInt64() + Marshal.SizeOf(typeof(LangAndCodePage)));
                }

                // Try each translation until we find one that works
                foreach (var translation in translations)
                {
                    string subBlock = $@"\StringFileInfo\{translation.wLanguage:X4}{translation.wCodePage:X4}\FileVersion";
                    if (VerQueryValue(buffer, subBlock, out pValue, out len) && len > 0)
                    {
                        return Marshal.PtrToStringUni(pValue);
                    }
                }

                //Debug.LogError("Could not find FileVersion information in any language");
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

#endif

        static bool _sIsChromaticAvailable = false;

        static WyvrnAPI()
        {
            _sIsChromaticAvailable = false;

            try
            {
                string[] fileNames;

                // check program files for production version

                // check windows systems folders for production version

#if UNITY_64
                fileNames = new string[]
                {
					// Get 64-bit program files folder
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        "Razer Chroma SDK",
                        "bin",
                        "RzChromatic64.dll"),
					// Get system32 folder
					Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                        "System32",
                        "RzChromatic64.dll"),
                };
#else
				fileNames = new string[]
				{
					// Get 32-bit program files folder
                    Path.Combine(
						Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                        "Razer Chroma SDK",
						"bin",
						"RzChromatic.dll"),
					// Get SysWOW64 folder
					Path.Combine(
						Environment.GetFolderPath(Environment.SpecialFolder.Windows),
						"SysWOW64",
						"RzChromatic.dll"),
				};
#endif

                foreach (string fileName in fileNames)
                {
                    if (!IsProductionVersionAvailable(fileName))
                    {
                        return;
                    }
                }

                _sIsChromaticAvailable = true; // production version or better
            }
            catch (Exception ex)
            {
                Debug.LogError(string.Format("The ChromaSDK is not available! Exception={0}", ex));
            }
        }

        private static bool IsProductionVersionAvailable(string fileName)
        {
            try
            {
                FileInfo fi = new FileInfo(fileName);
                if (!fi.Exists)
                {
                    return false;
                }

#if ENABLE_IL2CPP
				String fileVersion = GetProductVersion(fileName);
#else
                System.Diagnostics.FileVersionInfo versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(fileName);


                String fileVersion = versionInfo.FileVersion;
#endif
                //Debug.Log(string.Format("ChromaSDK Version={0} File={1}", fileVersion, fileName));
                String[] versionParts = fileVersion.Split(".".ToCharArray());
                if (versionParts.Length < 4)
                {
                    return false;
                }

                int major;
                if (!int.TryParse(versionParts[0], out major))
                {
                    return false;
                }

                int minor;
                if (!int.TryParse(versionParts[1], out minor))
                {
                    return false;
                }

                int build;
                if (!int.TryParse(versionParts[2], out build))
                {
                    return false;
                }

                int revision;
                if (!int.TryParse(versionParts[3], out revision))
                {
                    return false;
                }

                // Anything less than the min version returns false

                // major, minor, build, revision ref: https://learn.microsoft.com/en-us/dotnet/api/system.reflection.assemblyversionattribute.-ctor?source=recommendations&view=net-7.0
                const int minMajor = 2;
                const int minMinor = 0;
                const int minBuild = 2;
                const int minRevision = 0;

                if (major < minMajor) // Less than minMajor
                {
                    return false;
                }

                if (major == minMajor && minor < minMinor) // Less than minMinor
                {
                    return false;
                }

                if (major == minMajor && minor == minMinor && build < minBuild) // Less than minBuild
                {
                    return false;
                }

                if (major == minMajor && minor == minMinor && build == minBuild && revision < minRevision) // Less than minRevision
                {
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError(string.Format("The ChromaSDK is not available! Exception={0}", ex));
                return false;
            }
        }

#endif // !UNITY_PS5

#if UNITY_PS5
        /// <summary>
        /// PS5 haptic backend. Owned statically so it spans the whole SDK lifetime:
        /// CoreInitSDK / CoreUnInit drive its Initialize() / Shutdown(), and the three
        /// haptic controls are accepted (and cached) whether or not it is up.
        /// </summary>
        private static readonly Haptics.IWyvrnHapticBackend _sBackend = new Haptics.InterhapticsHapticBackend();
#endif

        /// <summary>
        /// Check if the Wyvrn SDK is usable before calling API Methods.
        /// On Windows/Xbox this checks for a production RzChromatic DLL. On PS5 the
        /// backend is in-process so this is always true; whether the Interhaptics HAR
        /// libraries actually loaded is reported by CoreInitSDK / IsInitialized.
        /// </summary>
        /// <returns></returns>
        public static bool IsWyvrnSDKAvailable()
        {
#if UNITY_PS5
            return true;
#else
            return _sIsChromaticAvailable;
#endif
        }

        /// <summary>
        /// True once the SDK is initialized and able to play events. On PS5 this means
        /// the render worker is up with the Interhaptics runtime loaded, so it stays
        /// false when the HAR libraries are missing even though CoreInitSDK succeeded.
        /// </summary>
        public static bool IsInitialized()
        {
#if UNITY_PS5
            return _sBackend.IsInitialized;
#else
            return _sInitialized;
#endif
        }

#if !UNITY_PS5

        #region Helpers (handle path conversions)

        /// <summary>
        /// Helper to Unicode path string to IntPtr
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        private static IntPtr GetUnicodeIntPtr(string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return IntPtr.Zero;
            }
            byte[] array = UnicodeEncoding.Unicode.GetBytes(str + "\0");
            IntPtr lpData = Marshal.AllocHGlobal(array.Length);
            Marshal.Copy(array, 0, lpData, array.Length);
            return lpData;
        }

        /// <summary>
        /// Helper to recycle the IntPtr
        /// </summary>
        /// <param name="lpData"></param>
        private static void FreeIntPtr(IntPtr lpData)
        {
            if (lpData != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(lpData);
            }
        }

        #endregion

#endif // !UNITY_PS5

        #region Public API Methods
#if !UNITY_PS5
        private static bool _sInitialized = false;
#endif
        /// <summary>
        /// Direct access to low level API.
        /// On PS5 this starts the Interhaptics haptic backend instead of the Chroma SDK.
        /// </summary>
        public static int CoreInitSDK(ref WyvrnSDK.APPINFOTYPE appInfo)
        {
            appInfo.SupportedDevice = 63;

#if UNITY_PS5
            // Succeeds as long as the render worker starts. A missing HAR library leaves
            // the worker inert rather than failing here, exactly as on Unreal: events are
            // then dropped silently and IsInitialized() reports false.
            return _sBackend.Initialize() ? RazerErrors.RZRESULT_SUCCESS : RazerErrors.RZRESULT_INVALID;
#else
            if (!_sIsChromaticAvailable)
            {
                return -1;
            }
            if (_sInitialized)
            {
                return RazerErrors.RZRESULT_SUCCESS;
            }
            int result = PluginCoreInitSDK(ref appInfo);
            if (result == RazerErrors.RZRESULT_SUCCESS)
            {
                _sInitialized = true;
            }
            return result;
#endif
        }
        /// <summary>
        /// Direct access to low level API.
        /// On PS5 this hands the event to the haptic render worker.
        /// </summary>
        public static int CoreSetEventName(string name)
        {
#if UNITY_PS5
            Haptics.WyvrnHapticsLog.Trace(string.Format("Trace [API]: CoreSetEventName('{0}') requested (main thread).", name));
            if (!_sBackend.IsInitialized)
            {
                Haptics.WyvrnHapticsLog.Trace(string.Format("Trace [API]: CoreSetEventName('{0}') DROPPED - backend not initialized.", name));
                return RazerErrors.RZRESULT_INVALID;
            }
            _sBackend.SetEventName(name);
            return RazerErrors.RZRESULT_SUCCESS;
#else
            if (!_sIsChromaticAvailable)
            {
                return -1;
            }
            if (!_sInitialized)
            {
                return -1;
            }
            IntPtr lp_Name = GetUnicodeIntPtr(name);
            int result = PluginCoreSetEventName(lp_Name);
            FreeIntPtr(lp_Name);
            return result;
#endif
        }
        /// <summary>
        /// Direct access to low level API.
        /// On PS5 this stops the haptic render worker and tears the HAR runtime down.
        /// </summary>
        public static int CoreUnInit()
        {
#if UNITY_PS5
            _sBackend.Shutdown();
            return RazerErrors.RZRESULT_SUCCESS;
#else
            if (!_sIsChromaticAvailable)
            {
                return RazerErrors.RZRESULT_SUCCESS;
            }
            if (!_sInitialized)
            {
                return RazerErrors.RZRESULT_SUCCESS;
            }
            int result = PluginCoreUnInit();
            if (result == RazerErrors.RZRESULT_SUCCESS)
            {
                _sInitialized = false;
            }
            return result;
#endif
        }
        #endregion

        #region PS5 haptic controls
        // Three independent settings: a master on/off, a vibration strength, and an
        // adaptive-trigger on/off. All may be called before CoreInitSDK and survive
        // CoreInitSDK/CoreUnInit cycles, because the backend caches them and the render
        // worker re-applies them on every bring-up (HAR resets its own global intensity
        // to 1.0 on each Init).
        //
        // Each returns RZRESULT_SUCCESS on PS5 and RZRESULT_NOT_SUPPORTED on every other
        // platform: these are PS5-only levers, as RzChromatic has no equivalent.

        /// <summary>
        /// PS5 only. Master haptics on/off.
        /// Off is a hard off: anything playing stops, both adaptive triggers are released,
        /// and further events are skipped entirely rather than played inaudibly. Turning it
        /// back on does not resume what was stopped.
        /// </summary>
        public static int SetHapticsEnabled(bool enabled)
        {
#if UNITY_PS5
            // Deliberately NOT gated on IsInitialized(): the backend caches the setting and
            // the render worker applies it once HAR comes up, so a title can set it at startup.
            _sBackend.SetHapticsEnabled(enabled);
            return RazerErrors.RZRESULT_SUCCESS;
#else
            return RazerErrors.RZRESULT_NOT_SUPPORTED;
#endif
        }

        /// <summary>
        /// PS5 only. Writes the master haptics on/off state to outEnabled.
        /// outEnabled is false on platforms without haptics support.
        /// </summary>
        public static int GetHapticsEnabled(out bool outEnabled)
        {
#if UNITY_PS5
            outEnabled = _sBackend.AreHapticsEnabled;
            return RazerErrors.RZRESULT_SUCCESS;
#else
            outEnabled = false; // no haptics to enable off PS5
            return RazerErrors.RZRESULT_NOT_SUPPORTED;
#endif
        }

        /// <summary>
        /// PS5 only. Sets the vibration strength, 0..100 (0 = silent, 100 = full strength).
        /// Values outside the range are clamped.
        ///
        /// Vibration only. It does not touch adaptive-trigger resistance, which keeps the
        /// strength the effect author designed and has its own on/off. A gain of 0 silences
        /// vibration but still plays events, because an event may carry a Stiffness track
        /// that needs to arm a trigger - use SetHapticsEnabled(false) to stop everything.
        /// </summary>
        public static int SetVibrationGain(int gain)
        {
#if UNITY_PS5
            _sBackend.SetVibrationGain(Haptics.WyvrnHapticGain.Clamp(gain));
            return RazerErrors.RZRESULT_SUCCESS;
#else
            return RazerErrors.RZRESULT_NOT_SUPPORTED;
#endif
        }

        /// <summary>
        /// PS5 only. Writes the current vibration gain (0..100) to outGain.
        /// outGain is set to 100 (unattenuated) on platforms without haptics support.
        /// </summary>
        public static int GetVibrationGain(out int outGain)
        {
#if UNITY_PS5
            outGain = _sBackend.VibrationGain;
            return RazerErrors.RZRESULT_SUCCESS;
#else
            outGain = 100; // nothing is attenuating anything off PS5
            return RazerErrors.RZRESULT_NOT_SUPPORTED;
#endif
        }

        /// <summary>
        /// PS5 only. Turns DualSense adaptive-trigger resistance on or off, independently of
        /// vibration. Off releases whatever is armed but leaves events playing and vibrating;
        /// on re-arms whatever is still claimed.
        /// </summary>
        public static int SetAdaptiveTriggersEnabled(bool enabled)
        {
#if UNITY_PS5
            _sBackend.SetAdaptiveTriggersEnabled(enabled);
            return RazerErrors.RZRESULT_SUCCESS;
#else
            return RazerErrors.RZRESULT_NOT_SUPPORTED;
#endif
        }

        /// <summary>
        /// PS5 only. Writes the adaptive-trigger on/off state to outEnabled.
        /// outEnabled is false on platforms without adaptive triggers.
        /// </summary>
        public static int GetAdaptiveTriggersEnabled(out bool outEnabled)
        {
#if UNITY_PS5
            outEnabled = _sBackend.AreAdaptiveTriggersEnabled;
            return RazerErrors.RZRESULT_SUCCESS;
#else
            outEnabled = false; // no adaptive triggers off PS5
            return RazerErrors.RZRESULT_NOT_SUPPORTED;
#endif
        }
        #endregion

#if !UNITY_PS5

        #region Private DLL Hooks
        /// <summary>
        /// Direct access to low level API.
        /// EXPORT_API RZRESULT PluginCoreInitSDK(ChromaSDK::APPINFOTYPE* AppInfo);
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern int PluginCoreInitSDK(ref WyvrnSDK.APPINFOTYPE appInfo);
        /// <summary>
        /// Direct access to low level API.
        /// EXPORT_API RZRESULT PluginCoreSetEventName(LPCTSTR Name);
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern int PluginCoreSetEventName(IntPtr name);
        /// <summary>
        /// Direct access to low level API.
        /// EXPORT_API RZRESULT PluginCoreUnInit();
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern int PluginCoreUnInit();
        #endregion

#endif // !UNITY_PS5
    }
}
