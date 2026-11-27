// Copyright 2017-2025 Razer, Inc. All Rights Reserved.

using System.Runtime.InteropServices;

namespace WyvrnSDK.Haptics
{
    /// <summary>
    /// Minimal, ABI-compatible mirror of the Interhaptics HAR target types
    /// (Interhaptics::HapticBodyMapping in the HAR SDK's SharedTypes.h).
    ///
    /// The PS5 backend calls HAR through P/Invoke rather than shipping the SDK's C#
    /// wrapper, so it only needs the binary layout of the structures it passes across
    /// the module boundary - not the SDK headers themselves (which are third-party and
    /// not vendored into this repo).
    ///
    /// The enum underlying type (int) and the field order of HARCommandData MUST match
    /// the SDK exactly; the values below are copied verbatim from SharedTypes.h.
    /// </summary>
    public enum HAROperator
    {
        Minus = -1,
        Neutral = 0,
        Plus = 1,
    }

    /// <summary>Interhaptics::HapticBodyMapping::LateralFlag.</summary>
    public enum HARLateralFlag
    {
        Unknown = -1,
        Global = 0,
        Right = 1,
        Left = 2,
        Center = 3,
    }

    /// <summary>Interhaptics::HapticBodyMapping::GroupID (only the group this backend targets).</summary>
    public enum HARGroupID
    {
        Hand = 302,
    }

    /// <summary>
    /// Interhaptics::HapticBodyMapping::CommandData { Operator Sign; GroupID Group; LateralFlag Side; }
    /// Sequential layout of three 4-byte enums, matching the SDK's 12-byte struct.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HARCommandData
    {
        public HAROperator Sign;
        public HARGroupID Group;
        public HARLateralFlag Side;

        public HARCommandData(HAROperator sign, HARGroupID group, HARLateralFlag side)
        {
            Sign = sign;
            Group = group;
            Side = side;
        }
    }
}
