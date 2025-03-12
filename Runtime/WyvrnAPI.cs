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

    public class WyvrnAPI
    {

#if PLATFORM_XBOXONE
#if UNITY_EDITOR
        const string DLL_NAME = "WyvrnSDK";
#else
        const string DLL_NAME = "GDKWyvrnSDK";
#endif
#else
#if UNITY_3 || UNITY_3_0 || UNITY_3_1 || UNITY_3_2 || UNITY_3_3 || UNITY_3_4 || UNITY_3_5
        const string DLL_NAME = "WyvrnSDK3";
#elif UNITY_64 || UNITY_EDITOR
        const string DLL_NAME = "WyvrnSDK64";
#else
        const string DLL_NAME = "WyvrnSDK";
#endif
#endif

#if ENABLE_IL2CPP

		[DllImport("version.dll", CharSet = CharSet.Unicode)]
		private static extern int GetFileVersionInfoSize(string lptstrFilename, out uint lpdwHandle);
		[DllImport("version.dll", CharSet = CharSet.Unicode)]
		private static extern bool GetFileVersionInfo(string lptstrFilename, uint dwHandle, uint dwLen, IntPtr lpData);
		[DllImport("version.dll", CharSet = CharSet.Unicode)]
		private static extern bool VerQueryValue(IntPtr pBlock, string lpSubBlock, out IntPtr lplpBuffer, out uint puLen);
		public static string GetProductVersion(string filePath)
		{
			string fileVersion = null;
			uint handle;
			int size = GetFileVersionInfoSize(filePath, out handle);
			if (size > 0)
			{
				IntPtr buffer = Marshal.AllocHGlobal(size);
				if (GetFileVersionInfo(filePath, handle, (uint)size, buffer))
				{
					IntPtr pValue;
					uint len;
					if (VerQueryValue(buffer, "\\StringFileInfo\\040904b0\\FileVersion", out pValue, out len))
					{
						fileVersion = Marshal.PtrToStringUni(pValue);
						//Debug.Log("File Version: " + fileVersion);
					}
				}
				Marshal.FreeHGlobal(buffer);
			}
			return fileVersion;
		}

#endif

        /// <summary>
        /// Check if the WyvrnSDK DLL exists before calling API Methods
        /// </summary>
        /// <returns></returns>
        public static bool IsWyvrnSDKAvailable()
        {
#if PLATFORM_XBOXONE
			return true;
#endif
            try
            {
                String fileName;
#if UNITY_64
                // Get SysWOW64 folder
                fileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "System32",
                    "RzChromatic64.dll"
                );
#else
                // Get system32 folder
                fileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "SysWOW64",
                    "RzChromatic.dll"
                );
#endif
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
                //Debug.Log(string.Format("WyvrnSDK Version={0} File={1}", fileVersion, fileName));
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
                const int minBuild = 0;
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

                return true; // production version or better
            }
            catch (Exception ex)
            {
                Debug.LogError(string.Format("The WyvrnSDK is not available! Exception={0}", ex));
            }
            return false;
        }

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


        #region Public API Methods
        /// <summary>
        /// Direct access to low level API.
        /// </summary>
        public static int CoreInitSDK(ref WyvrnSDK.APPINFOTYPE appInfo)
        {
            appInfo.SupportedDevice = 63;

            int result = PluginCoreInitSDK(ref appInfo);
            return result;
        }
        /// <summary>
        /// Direct access to low level API.
        /// </summary>
        public static int CoreSetEventName(string name)
        {
            IntPtr lp_Name = GetUnicodeIntPtr(name);
            int result = PluginCoreSetEventName(lp_Name);
            FreeIntPtr(lp_Name);
            return result;
        }
        /// <summary>
        /// Direct access to low level API.
        /// </summary>
        public static int CoreUnInit()
        {
            int result = PluginCoreUnInit();
            return result;
        }
        #endregion

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
    }
}
