// Copyright 2017-2025 Razer, Inc. All Rights Reserved.

using System;
using System.Collections.Generic;

namespace WyvrnSDK.Haptics
{
    /// <summary>
    /// Body region a haptic event addresses, mirroring the WYVRN.config "Target".
    /// On PS5 the DualSense provider renders only the hand region; the others are
    /// ignored.
    /// </summary>
    public enum WyvrnHapticRegion
    {
        Head = 0,
        Hand = 1,
        Chest = 2,
        Waist = 3,
        Leg = 4,
    }

    /// <summary>
    /// Lateral side of a target region, mirroring the WYVRN.config Targeting
    /// "Spatialization". On the DualSense, Global drives both palms, Left/Right one.
    /// Maps to the HAR LateralFlag (Global/Left/Right) at play time.
    /// </summary>
    public enum WyvrnHapticSide
    {
        Global = 0,
        Left = 1,
        Right = 2,
    }

    /// <summary>
    /// Playback priority from the WYVRN.config "Priority". Five levels mirroring the
    /// authoring tool. A live event silences every lower-priority event for its
    /// duration (priority ducking), and drives voice stealing when an effect's voice
    /// pool is saturated.
    /// </summary>
    public enum WyvrnHapticPriority
    {
        VeryLow = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        VeryHigh = 4,
    }

    /// <summary>
    /// How an event mixes against other live events of equal priority, from the
    /// WYVRN.config "Mixing". Merge plays them together (additive); Override makes the
    /// newer event play alone and silences the older equal-priority one until it ends.
    /// </summary>
    public enum WyvrnHapticMixing
    {
        Merge = 0,
        Override = 1,
    }

    /// <summary>
    /// One body-region target of a haptic event with its lateral side (a WYVRN.config
    /// Targeting entry). On PS5 only the hand region is rendered.
    /// </summary>
    [Serializable]
    public struct WyvrnHapticTarget : IEquatable<WyvrnHapticTarget>
    {
        public WyvrnHapticRegion Region;
        public WyvrnHapticSide Side;

        public WyvrnHapticTarget(WyvrnHapticRegion region, WyvrnHapticSide side)
        {
            Region = region;
            Side = side;
        }

        public bool Equals(WyvrnHapticTarget other)
        {
            return Region == other.Region && Side == other.Side;
        }

        public override bool Equals(object obj)
        {
            return obj is WyvrnHapticTarget other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ((int)Region * 397) ^ (int)Side;
        }
    }

    /// <summary>
    /// One haptic event triggered by a command (a WYVRN.config Haptic_Event): the
    /// Interhaptics effect asset plus its playback parameters.
    /// </summary>
    [Serializable]
    public class WyvrnHapticEvent
    {
        /// <summary>Interhaptics haptic effect (one .haps). Its JSON is handed to HAR AddHM at runtime.</summary>
        public WyvrnHapticEffect Effect;

        /// <summary>Intensity factor forwarded to SetEventIntensity. HAR clamps it above 0.</summary>
        public float Gain = 1.0f;

        /// <summary>Loop count forwarded to SetEventLoop: 0/1 = one shot, &lt; 0 = infinite.</summary>
        public int Loop;

        public WyvrnHapticPriority Priority = WyvrnHapticPriority.High;

        /// <summary>Mixing against other live equal-priority events (WYVRN.config Mixing).</summary>
        public WyvrnHapticMixing Mixing = WyvrnHapticMixing.Merge;

        /// <summary>Body regions (with side) this effect targets. Translated to HAR targets at play time.</summary>
        public List<WyvrnHapticTarget> Targets = new List<WyvrnHapticTarget>();
    }

    /// <summary>
    /// A named WYVRN event (External_Command_ID) and the haptics it triggers.
    /// </summary>
    [Serializable]
    public class WyvrnHapticCommand
    {
        public string EventName;

        public List<WyvrnHapticEvent> Effects = new List<WyvrnHapticEvent>();

        /// <summary>Literal event names whose playback stops when this command fires (array form of Interrupts_Commands).</summary>
        public List<string> InterruptCommands = new List<string>();

        /// <summary>Stop every active event when this command fires (the bare-string "All" form of Interrupts_Commands).</summary>
        public bool InterruptAll;
    }
}
