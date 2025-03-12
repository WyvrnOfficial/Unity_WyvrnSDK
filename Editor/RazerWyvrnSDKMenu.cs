#if UNITY_EDITOR

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class RazerWyvrnSDKMenu : MonoBehaviour
{
    public static int CompareDirectoriesByCreationTime(DirectoryInfo dir1, DirectoryInfo dir2)
    {
        return dir2.CreationTimeUtc.CompareTo(dir1.CreationTimeUtc);
    }

    static bool SearchForAndTruncateStart(ref string text, string search)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }
        int index = text.IndexOf(search);
        if (index < 0)
        {
            return false;
        }
        text = text.Substring(index + search.Length);
        return true;
    }

    static bool IsQuoteEscaped(string text, int index)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }
        if (index < 1)
        {
            return false;
        }
        if (index >= text.Length)
        {
            return false;
        }
        if (text[index] == '"' && text[index - 1] == '\\')
        {
            return true;
        }
        return false;
    }

    static bool SearchForQuoteTruncateEnd(ref string text)
    {
        const string search = "\"";
        int temp = 0;
        while (temp < text.Length)
        {
            int index = text.IndexOf(search, temp);
            if (index < 0)
            {
                break;
            }
            if (!IsQuoteEscaped(text, index))
            {
                text = text.Substring(0, index);
                return true;
            }
            temp += index;
        }
        return false;
    }

    static DirectoryInfo GetMostRecentPackageFolder()
    {
        // check manifest for local package folder
        const string manifestPath = "Packages\\manifest.json";
        if (File.Exists(manifestPath))
        {
            // read json
            string json = File.ReadAllText(manifestPath);
            if (SearchForAndTruncateStart(ref json, "\"com.razer.wyvrnsdk\""))
            {
                if (SearchForAndTruncateStart(ref json, ":"))
                {
                    if (SearchForAndTruncateStart(ref json, "\""))
                    {
                        if (SearchForQuoteTruncateEnd(ref json))
                        {
                            string tokenFile = "file:";
                            if (json.StartsWith(tokenFile))
                            {
                                return new DirectoryInfo(json.Substring(tokenFile.Length));
                            }
                        }
                    }
                }
            }
        }


        DirectoryInfo packageCache = new DirectoryInfo("Library\\PackageCache");
        if (packageCache.Exists)
        {
            List<DirectoryInfo> packageFolders = new List<DirectoryInfo>();
            foreach (DirectoryInfo di in packageCache.GetDirectories("com.razer.wyvrnsdk@*"))
            {
                packageFolders.Add(di);
            }

            if (packageFolders.Count > 0)
            {
                packageFolders.Sort(CompareDirectoriesByCreationTime);
                /*
                foreach (DirectoryInfo di in packageFolders)
                {
                    Debug.LogFormat("{0} {1}", di.CreationTimeUtc, di.Name);
                }
                */

                return packageFolders[0];

            }


        }
        return null;
    }


    // Add a menu item to setup sample scenes
    [MenuItem("Assets/Razer Wyvrn SDK - Setup Sample Scenes")]
    static void MenuSetupSampleData()
    {
        DirectoryInfo packageFolder = GetMostRecentPackageFolder();
        if (packageFolder != null)
        {
            // get assets fp;der
            DirectoryInfo assets = new DirectoryInfo("Assets");
            if (!assets.Exists)
            {
                Debug.LogError("Assets does not exist!");
            }
            else
            {
                // create scenes folder if missing
                DirectoryInfo scenes = new DirectoryInfo("Assets\\Scenes");
                if (!scenes.Exists)
                {
                    Debug.Log("Creating Assets\\Scenes");
                    scenes.Create();
                }

                // copy scenes to destination
                if (!scenes.Exists)
                {
                    Debug.LogError("Assets\\Scenes does not exist!");
                }
                else
                {
                    //Debug.LogFormat("Assets\\Scenes exists");
                    string pathSampleScenes = packageFolder.FullName + "\\Tests\\Scenes";
                    //Debug.Log(pathSampleScenes);
                    DirectoryInfo sampleScenes = new DirectoryInfo(pathSampleScenes);
                    if (sampleScenes.Exists)
                    {
                        foreach (FileInfo fi in sampleScenes.GetFiles("*.unity"))
                        {
                            try
                            {
                                string pathDestination = scenes.FullName + "\\" + fi.Name;
                                fi.CopyTo(pathDestination, true);
                            }
                            catch
                            {
                                Debug.LogErrorFormat("Failed to copy: {0}", fi.Name);
                            }
                        }
                    }
                }

                // Refresh asset database
                AssetDatabase.Refresh();

                Debug.Log("Razer WyvrnSDK sample content copied!");
            }

        }
    }
}

#endif
