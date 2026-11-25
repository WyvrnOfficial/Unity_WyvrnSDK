// Copyright 2017-2025 Razer, Inc. All Rights Reserved.

using System;
using System.Collections.Generic;

using UnityEngine;

using WyvrnSDK;

/// <summary>
/// PS5 haptics behaviour test harness for the sample scene.
///
/// Walks through the arbitration rules one step at a time - interrupts, priority
/// ducking, Merge vs Override, laterality and the adaptive-trigger latch - firing the
/// Debug_* commands from the sample WYVRN.config and saying what each step should feel
/// like. Everything is driven by a single "advance" press, because a devkit has no
/// mouse and the IMGUI buttons below are only reachable with a pointer.
///
/// Add this to the GameSample object when you want it. It is deliberately not attached
/// to the shipped sample scene: that scene predates Unity 2021 and saving it from a
/// current editor rewrites it into a format older editors cannot open.
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

    [Tooltip("UI scale. The default IMGUI font is unreadable on a TV at 1080p.")]
    [Range(1.0f, 4.0f)]
    public float UiScale = 2.0f;

    /// <summary>One step of the guided walkthrough.</summary>
    private struct Step
    {
        public string Action;
        public string Expect;
        public Action Run;

        public Step(string action, string expect, Action run)
        {
            Action = action;
            Expect = expect;
            Run = run;
        }
    }

    private readonly List<Step> steps = new List<Step>();
    private int stepIndex = -1;
    private string lastResult = string.Empty;
    private string inputDiagnostic = "no input seen yet";

    // Any gamepad button advances. The DualSense's legacy-input button numbering is
    // platform-specific, and a bare project has no named axes set up (asking for one
    // by name throws), so polling the button range is both the safest and the most
    // portable thing available here.
    private const int JoystickButtonCount = 20;
    private bool advanceHeld;

    private bool hapticsEnabled = true;
    private bool adaptiveTriggersEnabled = true;
    private int vibrationGain = 100;

    private void Start()
    {
        // Read back whatever the backend currently holds, so the UI starts in sync
        // instead of asserting its own defaults over the title's.
        WyvrnAPI.GetHapticsEnabled(out hapticsEnabled);
        WyvrnAPI.GetVibrationGain(out vibrationGain);
        WyvrnAPI.GetAdaptiveTriggersEnabled(out adaptiveTriggersEnabled);

        BuildSteps();
    }

    private void BuildSteps()
    {
        steps.Clear();

        steps.Add(new Step("Effect1 (Hand, Global)",
            "a one-shot buzz in both grips",
            () => Fire("Effect1")));

        // --- laterality -------------------------------------------------------------
        steps.Add(new Step("Debug_HandLeft",
            "LEFT grip only",
            () => Fire("Debug_HandLeft")));
        steps.Add(new Step("Debug_HandRight",
            "RIGHT grip only",
            () => Fire("Debug_HandRight")));
        steps.Add(new Step("Debug_HandGlobal",
            "both grips evenly",
            () => Fire("Debug_HandGlobal")));
        steps.Add(new Step("Debug_StopAll",
            "silence",
            () => Fire("Debug_StopAll")));

        // --- named interrupt vs stop-all --------------------------------------------
        steps.Add(new Step("Debug_LoopA",
            "a continuous loop; it should NOT stop on its own",
            () => Fire("Debug_LoopA")));
        steps.Add(new Step("Debug_LoopB (merges with A)",
            "both loops together, stronger than A alone",
            () => Fire("Debug_LoopB")));
        steps.Add(new Step("Debug_StopA (named interrupt)",
            "A stops, B KEEPS playing - only the named event is stopped",
            () => Fire("Debug_StopA")));
        steps.Add(new Step("Debug_StopAll",
            "silence",
            () => Fire("Debug_StopAll")));

        // --- priority ducking -------------------------------------------------------
        steps.Add(new Step("Debug_PriorityLow (loop)",
            "a steady low-priority loop",
            () => Fire("Debug_PriorityLow")));
        steps.Add(new Step("Debug_PriorityHigh (one-shot)",
            "the loop goes SILENT, then resumes MID-STREAM - not from its start",
            () => Fire("Debug_PriorityHigh")));
        steps.Add(new Step("Debug_StopAll",
            "silence",
            () => Fire("Debug_StopAll")));

        // --- mixing: merge vs override ----------------------------------------------
        steps.Add(new Step("Debug_EqualA (loop)",
            "a steady equal-priority loop",
            () => Fire("Debug_EqualA")));
        steps.Add(new Step("Debug_MergeB (one-shot, Merge)",
            "B plays ON TOP of A; A is never silenced",
            () => Fire("Debug_MergeB")));
        steps.Add(new Step("Debug_OverrideB (one-shot, Override)",
            "A is SILENCED while B plays, then A resumes",
            () => Fire("Debug_OverrideB")));
        steps.Add(new Step("Debug_StopAll",
            "silence",
            () => Fire("Debug_StopAll")));

        // --- adaptive triggers ------------------------------------------------------
        steps.Add(new Step("Debug_TriggerOn (latches both)",
            "squeeze L2 and R2: BOTH resist",
            () => Fire("Debug_TriggerOn")));
        steps.Add(new Step("Debug_TriggerOff (authored release)",
            "both triggers go loose again",
            () => Fire("Debug_TriggerOff")));
        steps.Add(new Step("Debug_TriggerLeft",
            "L2 resists, R2 stays LOOSE",
            () => Fire("Debug_TriggerLeft")));
        steps.Add(new Step("Debug_TriggerOff",
            "L2 goes loose",
            () => Fire("Debug_TriggerOff")));
        steps.Add(new Step("Debug_TriggerShot (ONE-SHOT, still latches)",
            "the buzz ends but the triggers STAY STIFF - the key case. If they go loose on their own, latching is broken",
            () => Fire("Debug_TriggerShot")));
        steps.Add(new Step("Debug_TriggerOff",
            "only now do the triggers release",
            () => Fire("Debug_TriggerOff")));

        // --- the three controls -----------------------------------------------------
        steps.Add(new Step("Debug_LoopA + gain 100",
            "a loop at full strength",
            () => { SetGain(100); Fire("Debug_LoopA"); }));
        steps.Add(new Step("gain 50",
            "the SAME loop, clearly weaker",
            () => SetGain(50)));
        steps.Add(new Step("gain 0",
            "silence, but the loop is still playing",
            () => SetGain(0)));
        steps.Add(new Step("Debug_TriggerOn at gain 0",
            "triggers resist at FULL strength - the gain must not touch resistance",
            () => Fire("Debug_TriggerOn")));
        steps.Add(new Step("gain 100",
            "the loop comes back; triggers unchanged",
            () => SetGain(100)));
        steps.Add(new Step("adaptive triggers OFF",
            "triggers go loose, the loop KEEPS vibrating",
            () => SetTriggers(false)));
        steps.Add(new Step("adaptive triggers ON",
            "triggers re-arm from the surviving claim, without replaying anything",
            () => SetTriggers(true)));
        steps.Add(new Step("master haptics OFF",
            "everything stops: vibration AND trigger resistance",
            () => SetHaptics(false)));
        steps.Add(new Step("master haptics ON",
            "still silent - turning it back on must NOT resume what was stopped",
            () => SetHaptics(true)));
        steps.Add(new Step("Debug_StopAll (reset)",
            "a clean slate; press again to run the sequence from the top",
            () => Fire("Debug_StopAll")));
    }

    private void Fire(string eventName)
    {
        int result = WyvrnAPI.CoreSetEventName(eventName);
        lastResult = string.Format("{0} -> {1}", eventName, RazerErrors.GetResultString(result));
    }

    private void SetGain(int gain)
    {
        vibrationGain = gain;
        int result = WyvrnAPI.SetVibrationGain(gain);
        lastResult = string.Format("SetVibrationGain({0}) -> {1}", gain, RazerErrors.GetResultString(result));
    }

    private void SetTriggers(bool enabled)
    {
        adaptiveTriggersEnabled = enabled;
        int result = WyvrnAPI.SetAdaptiveTriggersEnabled(enabled);
        lastResult = string.Format("SetAdaptiveTriggersEnabled({0}) -> {1}", enabled, RazerErrors.GetResultString(result));
    }

    private void SetHaptics(bool enabled)
    {
        hapticsEnabled = enabled;
        int result = WyvrnAPI.SetHapticsEnabled(enabled);
        lastResult = string.Format("SetHapticsEnabled({0}) -> {1}", enabled, RazerErrors.GetResultString(result));
    }

    private void Advance()
    {
        stepIndex = steps.Count == 0 ? -1 : (stepIndex + 1) % steps.Count;
        if (stepIndex >= 0 && steps[stepIndex].Run != null)
        {
            steps[stepIndex].Run();
        }
    }

    private void Update()
    {
        if (!ShowHarness)
        {
            return;
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        // Any gamepad button, or Space / Return on a desktop, advances one step. Edge
        // triggered, so holding a button does not run away through the sequence.
        bool pressed = Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.Return);
        int whichButton = -1;
        for (int i = 0; i < JoystickButtonCount; ++i)
        {
            if (Input.GetKey(KeyCode.JoystickButton0 + i))
            {
                pressed = true;
                whichButton = i;
                break;
            }
        }

        if (whichButton >= 0)
        {
            // Surfaced so the DualSense's button numbering is visible on screen rather
            // than guessed at; useful if a specific button ever needs its own action.
            inputDiagnostic = "JoystickButton" + whichButton;
        }

        if (pressed && !advanceHeld)
        {
            Advance();
        }
        advanceHeld = pressed;
#else
        inputDiagnostic = "built without the legacy Input Manager; use the buttons below";
#endif
    }

    private void OnGUI()
    {
        if (!ShowHarness)
        {
            return;
        }

        Matrix4x4 restore = GUI.matrix;
        GUIUtility.ScaleAroundPivot(new Vector2(UiScale, UiScale), Vector2.zero);

        float width = Screen.width / UiScale - 20.0f;
        GUILayout.BeginArea(new Rect(10.0f, 10.0f, width, Screen.height / UiScale - 20.0f));

        GUILayout.Label("WYVRN PS5 HAPTICS WALKTHROUGH");
        GUILayout.Label(string.Format("initialized: {0}    haptics: {1}    gain: {2}    triggers: {3}",
            WyvrnAPI.IsInitialized(), hapticsEnabled, vibrationGain, adaptiveTriggersEnabled));
        GUILayout.Space(6.0f);

        if (stepIndex < 0)
        {
            GUILayout.Label("Press ANY gamepad button to begin.");
        }
        else
        {
            Step step = steps[stepIndex];
            GUILayout.Label(string.Format("STEP {0}/{1}:  {2}", stepIndex + 1, steps.Count, step.Action));
            GUILayout.Label("EXPECT:  " + step.Expect);
            GUILayout.Space(4.0f);
            GUILayout.Label("Press again for the next step.");
        }

        GUILayout.Space(8.0f);
        GUILayout.Label("last call: " + lastResult);
        GUILayout.Label("input: " + inputDiagnostic);

        // Pointer-driven fallbacks, for running the sample in the editor or on desktop.
        GUILayout.Space(8.0f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Next step")) { Advance(); }
        if (GUILayout.Button("Stop all")) { Fire("Debug_StopAll"); }
        if (GUILayout.Button("Triggers off")) { Fire("Debug_TriggerOff"); }
        if (GUILayout.Button("Restart")) { stepIndex = -1; Fire("Debug_StopAll"); }
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
        GUI.matrix = restore;
    }
}
