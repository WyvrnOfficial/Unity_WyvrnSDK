using WyvrnSDK;
using System.Collections;
using UnityEngine;

public class GameSample : MonoBehaviour
{
    private const int MAX_EFFECTS = 15;

    private bool _mInitialized = false;
    private int _mResult = 0;

    public IEnumerator Start()
    {
        if (!WyvrnAPI.IsWyvrnSDKAvailable())
        {
            _mResult = RazerErrors.RZRESULT_DLL_NOT_FOUND;
            yield break;
        }

        WyvrnSDK.APPINFOTYPE appInfo = new APPINFOTYPE();
        appInfo.Title = "Game Sample: Application";
        appInfo.Description = "A Unity sample application using Razer Wyvrn SDK";

        appInfo.Author_Name = "Razer";
        appInfo.Author_Contact = "https://wyvrn.com";

        //    0x01 | // Utility. (To specifiy this is an utility application)
        //    0x02   // Game. (To specifiy this is a game);
        appInfo.Category = 1;
        _mResult = WyvrnAPI.CoreInitSDK(ref appInfo);
        switch (_mResult)
        {
            case RazerErrors.RZRESULT_DLL_NOT_FOUND:
                Debug.Log(string.Format("Wyvrn DLL is not found! {0}", RazerErrors.GetResultString(_mResult)));
                break;
            case RazerErrors.RZRESULT_DLL_INVALID_SIGNATURE:
                Debug.Log(string.Format("Wyvrn DLL has an invalid signature! {0}", RazerErrors.GetResultString(_mResult)));
                break;
            case RazerErrors.RZRESULT_SUCCESS:
                _mInitialized = true;

                yield return new WaitForSeconds(0.1f);
                break;
            default:
                // If Wyvrn is not supported on the system, just avoid making further calls to the API until next time
                Debug.Log(string.Format("Failed to initialize Wyvrn! {0}", RazerErrors.GetResultString(_mResult)));
                break;
        }
    }
    public void OnApplicationQuit()
    {
        if (_mResult == RazerErrors.RZRESULT_SUCCESS)
        {
            int result = WyvrnAPI.CoreUnInit();
#if !UNITY_EDITOR
            WyvrnAPI.CoreUnInit();
#endif
            if (result != RazerErrors.RZRESULT_SUCCESS)
            {
                Debug.LogError("Failed to uninitialize Wyvrn!");
            }
        }
    }

    private string GetEffectName(int index)
    {
        string result = string.Format("Effect{0}", index);
        return result;
    }

    private void ExecuteItem(int index)
    {
        switch (index)
        {
            case 1:
                ShowEffect1();
                break;
            case 2:
                ShowEffect2();
                break;
            case 3:
                ShowEffect3();
                break;
            case 4:
                ShowEffect4();
                break;
            case 5:
                ShowEffect5();
                break;
            case 6:
                ShowEffect6();
                break;
            case 7:
                ShowEffect7();
                break;
            case 8:
                ShowEffect8();
                break;
            case 9:
                ShowEffect9();
                break;
            case 10:
                ShowEffect10();
                break;
            case 11:
                ShowEffect11();
                break;
            case 12:
                ShowEffect12();
                break;
            case 13:
                ShowEffect13();
                break;
            case 14:
                ShowEffect14();
                break;
            case 15:
                ShowEffect15();
                break;
        }
    }

    public void OnGUI()
    {
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal(GUILayout.Width(Screen.width));
        GUILayout.FlexibleSpace();

        if (!_mInitialized)
        {
            GUILayout.BeginVertical(GUILayout.Height(Screen.height));
            GUILayout.FlexibleSpace();
            GUILayout.Label("Sample has not yet initialized!");
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
        }
        else
        {
            switch (_mResult)
            {
                case RazerErrors.RZRESULT_DLL_NOT_FOUND:
                    GUILayout.BeginVertical(GUILayout.Height(Screen.height));
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("Wyvrn DLL is not found!");
                    GUILayout.FlexibleSpace();
                    GUILayout.EndVertical();
                    break;
                case RazerErrors.RZRESULT_DLL_INVALID_SIGNATURE:
                    GUILayout.BeginVertical(GUILayout.Height(Screen.height));
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("Wyvrn DLL has an invalid signature!");
                    GUILayout.FlexibleSpace();
                    GUILayout.EndVertical();
                    break;
                case RazerErrors.RZRESULT_SUCCESS:
                    {
                        const float buttonHeight = 40;

                        GUILayout.BeginHorizontal(GUILayout.Width(Screen.width), GUILayout.Height(Screen.height));
                        {
                            GUILayout.FlexibleSpace();
                            GUILayout.BeginVertical();
                            {
                                GUILayout.FlexibleSpace();

                                // horizontal center title
                                GUILayout.BeginHorizontal(GUILayout.Width(Screen.width));
                                {
                                    GUILayout.FlexibleSpace();
                                    GUILayout.Label("UNITY GAME WYVRN SAMPLE APP");
                                    GUILayout.FlexibleSpace();
                                }
                                GUILayout.EndHorizontal();

                                // center sections
                                GUILayout.BeginHorizontal(GUILayout.Width(Screen.width));
                                {
                                    GUILayout.FlexibleSpace();


                                    // sample section
                                    GUILayout.BeginVertical();
                                    {
                                        GUILayout.FlexibleSpace();

                                        for (int index = 0; index <= MAX_EFFECTS / 2; ++index)
                                        {
                                            if (GUILayout.Button(GetEffectName(index + 1), GUILayout.Height(buttonHeight)))
                                            {
                                                ExecuteItem(index + 1);
                                            }
                                        }

                                        GUILayout.FlexibleSpace();
                                    }
                                    GUILayout.EndVertical();

                                    GUILayout.FlexibleSpace();

                                    // sample section
                                    GUILayout.BeginVertical();
                                    {
                                        GUILayout.FlexibleSpace();

                                        for (int index = MAX_EFFECTS / 2 + 1; index < MAX_EFFECTS; ++index)
                                        {
                                            if (GUILayout.Button(GetEffectName(index + 1), GUILayout.Height(buttonHeight)))
                                            {
                                                ExecuteItem(index + 1);
                                            }
                                        }

                                        GUILayout.FlexibleSpace();
                                    }
                                    GUILayout.EndVertical();
                                    GUILayout.FlexibleSpace();
                                }
                                GUILayout.EndHorizontal();

                            }

                            GUILayout.FlexibleSpace();
                            GUILayout.EndVertical();

                            GUILayout.FlexibleSpace();

                        }

                        GUILayout.EndHorizontal();                        
                    }
                    break;
                default:
                    GUILayout.BeginVertical(GUILayout.Height(Screen.height));
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(string.Format("Failed to initialize Wyvrn! {0}", RazerErrors.GetResultString(_mResult)));
                    GUILayout.FlexibleSpace();
                    GUILayout.EndVertical();
                    break;
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
        }
    }

#region Autogenerated
    void ShowEffect1()
    {
        WyvrnAPI.CoreSetEventName("Effect1");
    }
    void ShowEffect1ChromaLink()
    {
    }
    void ShowEffect2()
    {
        WyvrnAPI.CoreSetEventName("Effect2");
    }
    void ShowEffect3()
    {
        WyvrnAPI.CoreSetEventName("Effect3");
    }
    void ShowEffect4()
    {
        WyvrnAPI.CoreSetEventName("Effect4");
    }
    void ShowEffect5()
    {
        WyvrnAPI.CoreSetEventName("Effect5");
    }
    void ShowEffect6()
    {
        WyvrnAPI.CoreSetEventName("Effect6");
    }
    void ShowEffect7()
    {
        WyvrnAPI.CoreSetEventName("Effect7");
    }
    void ShowEffect8()
    {
        WyvrnAPI.CoreSetEventName("Effect8");
    }
    void ShowEffect9()
    {
        WyvrnAPI.CoreSetEventName("Effect9");
    }
    void ShowEffect10()
    {
        WyvrnAPI.CoreSetEventName("Effect10");
    }
    void ShowEffect11()
    {
        WyvrnAPI.CoreSetEventName("Effect11");
    }
    void ShowEffect12()
    {
        WyvrnAPI.CoreSetEventName("Effect12");
    }
    void ShowEffect13()
    {
        WyvrnAPI.CoreSetEventName("Effect13");
    }
    void ShowEffect14()
    {
        WyvrnAPI.CoreSetEventName("Effect14");
    }
    void ShowEffect15()
    {
        WyvrnAPI.CoreSetEventName("Effect15");
    }
#endregion

}
