// Copyright 2017-2025 Razer, Inc. All Rights Reserved.

using System.Collections.Generic;

namespace WyvrnSDK.Haptics
{
    /// <summary>
    /// Plain (non-ScriptableObject) snapshot of one baked haptic event.
    /// </summary>
    public class WyvrnRuntimeEvent
    {
        /// <summary>Index into WyvrnRuntimeData.EffectJson (one distinct .haps); -1 if none.</summary>
        public int EffectId = -1;
        public float Gain = 1.0f;
        public int Loop;
        public WyvrnHapticPriority Priority = WyvrnHapticPriority.High;
        public WyvrnHapticMixing Mixing = WyvrnHapticMixing.Merge;
        public List<WyvrnHapticTarget> Targets = new List<WyvrnHapticTarget>();

        /// <summary>
        /// The effect asset carries a Stiffness track (property of the .haps, copied per
        /// event). Playing such an event latches its stiffness envelope onto the DualSense
        /// adaptive triggers its Hand sides address (Global = both, Left/Right = that one)
        /// until the event is interrupted — the envelope maps trigger travel, not time, so
        /// only a designer-authored OFF command (or stop-all) releases it.
        /// </summary>
        public bool HasStiffness;
    }

    public class WyvrnRuntimeCommand
    {
        public string EventName;
        public List<WyvrnRuntimeEvent> Effects = new List<WyvrnRuntimeEvent>();
        /// <summary>Literal event names to stop (array form of Interrupts_Commands).</summary>
        public List<string> InterruptCommands = new List<string>();
        /// <summary>Stop every active event (the bare-string "All" form).</summary>
        public bool InterruptAll;
    }

    /// <summary>
    /// Plain snapshot of the baked haptic command table.
    ///
    /// Built once on the main thread (Build reads the ScriptableObject data) and then
    /// owned and read exclusively by the render worker thread. Nothing here references a
    /// UnityEngine.Object, so it is safe to use off the main thread — which is the whole
    /// point: the worker never touches the Unity object system.
    /// </summary>
    public class WyvrnRuntimeData
    {
        /// <summary>Verbatim .haps JSON per EffectId; handed to HAR AddHM at preload.</summary>
        public readonly List<string> EffectJson = new List<string>();
        public readonly List<WyvrnRuntimeCommand> Commands = new List<WyvrnRuntimeCommand>();

        /// <summary>Builds the snapshot from the baked asset data. Must be called on the main thread.</summary>
        public static WyvrnRuntimeData Build(WyvrnHapticData data)
        {
            WyvrnRuntimeData result = new WyvrnRuntimeData();
            if (data == null)
            {
                return result;
            }

            // Distinct effect assets share an EffectId so they share one voice/material pool.
            Dictionary<WyvrnHapticEffect, int> effectIds = new Dictionary<WyvrnHapticEffect, int>();

            List<WyvrnHapticCommand> commands = data.Commands;
            for (int i = 0; i < commands.Count; ++i)
            {
                WyvrnHapticCommand command = commands[i];
                if (command == null)
                {
                    continue;
                }

                WyvrnRuntimeCommand runtimeCommand = new WyvrnRuntimeCommand();
                runtimeCommand.EventName = command.EventName;
                runtimeCommand.InterruptAll = command.InterruptAll;
                if (command.InterruptCommands != null)
                {
                    runtimeCommand.InterruptCommands.AddRange(command.InterruptCommands);
                }

                if (command.Effects != null)
                {
                    for (int j = 0; j < command.Effects.Count; ++j)
                    {
                        WyvrnHapticEvent effectEntry = command.Effects[j];
                        if (effectEntry == null || effectEntry.Effect == null)
                        {
                            continue;
                        }

                        int effectId;
                        if (!effectIds.TryGetValue(effectEntry.Effect, out effectId))
                        {
                            effectId = result.EffectJson.Count;
                            result.EffectJson.Add(effectEntry.Effect.Json);
                            effectIds.Add(effectEntry.Effect, effectId);
                        }

                        WyvrnRuntimeEvent runtimeEvent = new WyvrnRuntimeEvent();
                        runtimeEvent.EffectId = effectId;
                        runtimeEvent.Gain = effectEntry.Gain;
                        runtimeEvent.Loop = effectEntry.Loop;
                        runtimeEvent.Priority = effectEntry.Priority;
                        runtimeEvent.Mixing = effectEntry.Mixing;
                        runtimeEvent.HasStiffness = effectEntry.Effect.HasStiffnessTrack;
                        if (effectEntry.Targets != null)
                        {
                            runtimeEvent.Targets.AddRange(effectEntry.Targets);
                        }
                        runtimeCommand.Effects.Add(runtimeEvent);
                    }
                }

                result.Commands.Add(runtimeCommand);
            }

            return result;
        }

        /// <summary>Returns the command for eventName, or null.</summary>
        public WyvrnRuntimeCommand FindCommand(string eventName)
        {
            for (int i = 0; i < Commands.Count; ++i)
            {
                if (Commands[i].EventName == eventName)
                {
                    return Commands[i];
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Vibration gain conversions, shared by the backend and its unit tests.
    /// </summary>
    public static class WyvrnHapticGain
    {
        /// <summary>
        /// The vibration gain range exposed to games: an integer 0..100, where 100 is
        /// unattenuated and 0 is silent. Out-of-range input is clamped rather than rejected,
        /// and the clamped value is what the plugin caches and reports back, so a Get always
        /// returns a value that was actually applied.
        ///
        /// Vibration only - it does not affect adaptive-trigger resistance, which has its own
        /// on/off, nor does a gain of 0 stop events playing (an event may carry a Stiffness
        /// track that still needs to arm a trigger). The master switch is separate again.
        /// </summary>
        public static int Clamp(int gain0To100)
        {
            if (gain0To100 < 0)
            {
                return 0;
            }
            if (gain0To100 > 100)
            {
                return 100;
            }
            return gain0To100;
        }

        /// <summary>
        /// Maps the 0..100 vibration gain to HAR's global intensity factor (SetGlobalIntensity):
        /// 100 => 1.0, HAR's documented base value, and 0 => 0.0, genuine silence.
        /// HAR clamps only the low end, so the ceiling is ours - an intensity above 1.0
        /// scales the per-band amplitude past full scale and clips on the audio-out stream.
        /// </summary>
        public static float ToScalar(int gain0To100)
        {
            return Clamp(gain0To100) / 100.0f;
        }
    }
}
