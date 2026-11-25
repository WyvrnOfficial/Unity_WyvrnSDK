// Copyright 2017-2025 Razer, Inc. All Rights Reserved.

using UnityEngine;

using WyvrnSDK;

/// <summary>
/// PS5 haptics behaviour test harness for the sample scene.
///
/// Each button fires one of the Debug_* commands in the sample
/// WYVRN.config (HapticFolders/Game Sample Application), so the arbitration rules -
/// interrupts, priority ducking, Merge vs Override, laterality and the adaptive-trigger
/// latch - can be verified by hand on a devkit. The sliders and toggles drive the three
/// haptic controls.
///
/// Add this alongside <see cref="GameSample"/> on the same scene object. Off PS5 the
/// controls report "not supported" and the buttons simply name events the Windows path
/// does not carry haptics for, so it is harmless to leave enabled anywhere.
/// </summary>
public class GameSampleHapticsDebug : MonoBehaviour
{
    /// <summary>
    /// On by default: this component is not part of any shipped scene, so adding it is
    /// already a deliberate act. Untick it to keep the component around while leaving
    /// the sample's own UI unobstructed.
    /// </summary>
    [Tooltip("Show the harness overlay. Untick to keep the component but hide its UI.")]
    public bool ShowHarness = true;

    private bool _mHapticsEnabled = true;
    private bool _mAdaptiveTriggersEnabled = true;
    private int _mVibrationGain = 100;
    private bool _mVerboseTrace;
    private string _mLastResult = string.Empty;

    private void Start()
    {
        // Read back whatever the backend currently holds, so the UI starts in sync
        // instead of asserting its own defaults over the title's.
        WyvrnAPI.GetHapticsEnabled(out _mHapticsEnabled);
        WyvrnAPI.GetVibrationGain(out _mVibrationGain);
        WyvrnAPI.GetAdaptiveTriggersEnabled(out _mAdaptiveTriggersEnabled);
        _mVerboseTrace = WyvrnSDK.Haptics.WyvrnHapticsLog.VerboseEnabled;
    }

    private void Fire(string eventName)
    {
        int result = WyvrnAPI.CoreSetEventName(eventName);
        _mLastResult = string.Format("{0} -> {1}", eventName, RazerErrors.GetResultString(result));
    }

    private void OnGUI()
    {
        if (!ShowHarness)
        {
            return;
        }

        const float width = 300.0f;
        const float buttonHeight = 28.0f;

        GUILayout.BeginArea(new Rect(10.0f, 10.0f, width, Screen.height - 20.0f), GUI.skin.box);
        GUILayout.Label("WYVRN PS5 HAPTICS HARNESS");

        // --- the three haptic controls ---------------------------------------------
        GUILayout.Space(4.0f);
        GUILayout.Label("Controls");

        bool wantHaptics = GUILayout.Toggle(_mHapticsEnabled, "Haptics enabled (master)");
        if (wantHaptics != _mHapticsEnabled)
        {
            _mHapticsEnabled = wantHaptics;
            WyvrnAPI.SetHapticsEnabled(wantHaptics);
        }

        bool wantTriggers = GUILayout.Toggle(_mAdaptiveTriggersEnabled, "Adaptive triggers enabled");
        if (wantTriggers != _mAdaptiveTriggersEnabled)
        {
            _mAdaptiveTriggersEnabled = wantTriggers;
            WyvrnAPI.SetAdaptiveTriggersEnabled(wantTriggers);
        }

        GUILayout.Label(string.Format("Vibration gain: {0}", _mVibrationGain));
        int wantGain = (int)GUILayout.HorizontalSlider(_mVibrationGain, 0.0f, 100.0f);
        if (wantGain != _mVibrationGain)
        {
            _mVibrationGain = wantGain;
            WyvrnAPI.SetVibrationGain(wantGain);
        }

        bool wantTrace = GUILayout.Toggle(_mVerboseTrace, "Verbose event trace");
        if (wantTrace != _mVerboseTrace)
        {
            _mVerboseTrace = wantTrace;
            WyvrnSDK.Haptics.WyvrnHapticsLog.VerboseEnabled = wantTrace;
        }

        // --- interrupts and stop-all -----------------------------------------------
        GUILayout.Space(6.0f);
        GUILayout.Label("Interrupts");
        if (GUILayout.Button("Loop A", GUILayout.Height(buttonHeight))) { Fire("Debug_LoopA"); }
        if (GUILayout.Button("Loop B", GUILayout.Height(buttonHeight))) { Fire("Debug_LoopB"); }
        if (GUILayout.Button("Stop A (named interrupt)", GUILayout.Height(buttonHeight))) { Fire("Debug_StopA"); }
        if (GUILayout.Button("Stop All (\"All\" sentinel)", GUILayout.Height(buttonHeight))) { Fire("Debug_StopAll"); }

        // --- laterality -------------------------------------------------------------
        GUILayout.Space(6.0f);
        GUILayout.Label("Laterality");
        if (GUILayout.Button("Hand Global (both palms)", GUILayout.Height(buttonHeight))) { Fire("Debug_HandGlobal"); }
        if (GUILayout.Button("Hand Left", GUILayout.Height(buttonHeight))) { Fire("Debug_HandLeft"); }
        if (GUILayout.Button("Hand Right", GUILayout.Height(buttonHeight))) { Fire("Debug_HandRight"); }

        // --- priority and mixing ----------------------------------------------------
        GUILayout.Space(6.0f);
        GUILayout.Label("Priority / mixing");
        if (GUILayout.Button("Priority Low (loop)", GUILayout.Height(buttonHeight))) { Fire("Debug_PriorityLow"); }
        if (GUILayout.Button("Priority High (once) - ducks", GUILayout.Height(buttonHeight))) { Fire("Debug_PriorityHigh"); }
        if (GUILayout.Button("Equal A (loop)", GUILayout.Height(buttonHeight))) { Fire("Debug_EqualA"); }
        if (GUILayout.Button("Merge B (once) - plays with A", GUILayout.Height(buttonHeight))) { Fire("Debug_MergeB"); }
        if (GUILayout.Button("Override B (once) - silences A", GUILayout.Height(buttonHeight))) { Fire("Debug_OverrideB"); }

        // --- adaptive triggers ------------------------------------------------------
        GUILayout.Space(6.0f);
        GUILayout.Label("Adaptive triggers (DualSense)");
        if (GUILayout.Button("Trigger On (both, latched)", GUILayout.Height(buttonHeight))) { Fire("Debug_TriggerOn"); }
        if (GUILayout.Button("Trigger Left only", GUILayout.Height(buttonHeight))) { Fire("Debug_TriggerLeft"); }
        if (GUILayout.Button("Trigger Shot (one-shot, latches)", GUILayout.Height(buttonHeight))) { Fire("Debug_TriggerShot"); }
        if (GUILayout.Button("Trigger Off (authored release)", GUILayout.Height(buttonHeight))) { Fire("Debug_TriggerOff"); }

        GUILayout.Space(6.0f);
        GUILayout.Label(string.Format("Initialized: {0}", WyvrnAPI.IsInitialized()));
        GUILayout.Label(_mLastResult);

        GUILayout.EndArea();
    }
}
