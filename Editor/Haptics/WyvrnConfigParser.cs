// Copyright 2017-2025 Razer, Inc. All Rights Reserved.

using System;
using System.Collections.Generic;

using UnityEngine;

namespace WyvrnSDK.Haptics.EditorTools
{
    /// <summary>One body-region target of a haptic event, with its side and gain (WYVRN.config Targeting entry).</summary>
    public struct WyvrnParsedTargeting
    {
        public WyvrnHapticRegion Target;
        public WyvrnHapticSide Side;
        public float Gain;
    }

    /// <summary>A haptic effect entry parsed from a command's Haptic_Events.</summary>
    public class WyvrnParsedEffect
    {
        public string EffectName;
        public int Loop;
        public WyvrnHapticPriority Priority = WyvrnHapticPriority.High;
        public WyvrnHapticMixing Mixing = WyvrnHapticMixing.Merge;
        public List<WyvrnParsedTargeting> Targeting = new List<WyvrnParsedTargeting>();
    }

    /// <summary>A parsed WYVRN.config ExternalCommand carrying haptics.</summary>
    public class WyvrnParsedCommand
    {
        public string EventName;
        public List<WyvrnParsedEffect> Effects = new List<WyvrnParsedEffect>();
        /// <summary>Literal event names to stop (array form of Interrupts_Commands).</summary>
        public List<string> InterruptCommands = new List<string>();
        /// <summary>True when Interrupts_Commands was the bare string "All" (stop every event).</summary>
        public bool InterruptAll;
    }

    /// <summary>
    /// Parses a WYVRN.config (the ExternalCommands / Haptic_Events / Targeting schema)
    /// into intermediate command data.
    ///
    /// Faithful to the file: it applies no device filtering and keeps every parsed
    /// target with its gain. The importer decides which targets the DualSense renders.
    /// Commands that carry no haptics (chroma-only) are omitted. Returns false on
    /// malformed JSON or a missing ExternalCommands array.
    /// </summary>
    public static class WyvrnConfigParser
    {
        public static bool Parse(string configJson, List<WyvrnParsedCommand> outCommands, out string outError)
        {
            outError = null;

            WyvrnJsonValue root;
            string jsonError;
            if (!WyvrnJson.TryParse(configJson, out root, out jsonError) || !root.IsObject)
            {
                outError = "WyvrnConfigParser: WYVRN.config is not valid JSON - " + (jsonError ?? "unexpected shape.");
                return false;
            }

            List<WyvrnJsonValue> commands;
            if (!root.TryGetArray("ExternalCommands", out commands))
            {
                outError = "WyvrnConfigParser: WYVRN.config has no 'ExternalCommands' array.";
                return false;
            }

            for (int i = 0; i < commands.Count; ++i)
            {
                WyvrnJsonValue commandObject = commands[i];
                if (commandObject == null || !commandObject.IsObject)
                {
                    continue;
                }

                WyvrnParsedCommand parsed = new WyvrnParsedCommand();
                if (!commandObject.TryGetString("External_Command_ID", out parsed.EventName) ||
                    string.IsNullOrEmpty(parsed.EventName))
                {
                    continue;
                }

                List<WyvrnJsonValue> hapticEvents;
                if (commandObject.TryGetArray("Haptic_Events", out hapticEvents))
                {
                    for (int j = 0; j < hapticEvents.Count; ++j)
                    {
                        WyvrnJsonValue eventObject = hapticEvents[j];
                        if (eventObject == null || !eventObject.IsObject)
                        {
                            continue;
                        }

                        WyvrnParsedEffect effect = ParseHapticEvent(eventObject, parsed.EventName);
                        if (string.IsNullOrEmpty(effect.EffectName) || effect.Targeting.Count == 0)
                        {
                            continue;
                        }
                        parsed.Effects.Add(effect);
                    }
                }

                ParseInterruptCommands(commandObject, parsed);

                // Drop chroma-only / empty commands: they produce no haptics on PS5. A stop-all
                // command has no effects and no named interrupts, so keep it via InterruptAll.
                if (parsed.Effects.Count > 0 || parsed.InterruptCommands.Count > 0 || parsed.InterruptAll)
                {
                    outCommands.Add(parsed);
                }
            }

            return true;
        }

        private static bool ParseTarget(string input, out WyvrnHapticRegion output)
        {
            switch (input)
            {
                case "Head": output = WyvrnHapticRegion.Head; return true;
                case "Hand": output = WyvrnHapticRegion.Hand; return true;

                // WYVRN's "All" is HAR's root body-part group (GroupID All = 0), which subsumes
                // Hand. This backend renders nothing but the hand anyway - InterhapticsRuntime.
                // AddTarget discards every other region - so "All" and "Hand" produce the
                // identical HAR call here. Without this case the targeting is dropped, the event
                // is left with no targets and discarded whole, and a command with no other
                // content disappears from the baked data entirely (its SetEventName then
                // matches nothing at runtime).
                case "All": output = WyvrnHapticRegion.Hand; return true;

                case "Chest": output = WyvrnHapticRegion.Chest; return true;
                case "Waist": output = WyvrnHapticRegion.Waist; return true;
                case "Leg": output = WyvrnHapticRegion.Leg; return true;
            }
            output = WyvrnHapticRegion.Hand;
            return false;
        }

        private static WyvrnHapticPriority ParsePriority(string input)
        {
            if (Matches(input, "VeryLow")) { return WyvrnHapticPriority.VeryLow; }
            if (Matches(input, "Low")) { return WyvrnHapticPriority.Low; }
            if (Matches(input, "Medium")) { return WyvrnHapticPriority.Medium; }
            if (Matches(input, "High")) { return WyvrnHapticPriority.High; }
            if (Matches(input, "VeryHigh")) { return WyvrnHapticPriority.VeryHigh; }

            // Unknown/missing Priority defaults to Medium: a malformed event must not dominate,
            // since priority ducking silences every event below the highest one playing.
            if (!string.IsNullOrEmpty(input))
            {
                Debug.LogWarning(string.Format("WyvrnConfigParser: unknown Priority '{0}'; defaulting to Medium.", input));
            }
            return WyvrnHapticPriority.Medium;
        }

        private static WyvrnHapticMixing ParseMixing(string input)
        {
            return Matches(input, "Override") ? WyvrnHapticMixing.Override : WyvrnHapticMixing.Merge;
        }

        private static WyvrnHapticSide ParseSpatialization(string input)
        {
            if (Matches(input, "Left")) { return WyvrnHapticSide.Left; }
            if (Matches(input, "Right")) { return WyvrnHapticSide.Right; }
            return WyvrnHapticSide.Global;
        }

        private static bool Matches(string input, string literal)
        {
            return !string.IsNullOrEmpty(input) && string.Equals(input, literal, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Interrupts_Commands says what to stop when this command fires. WYVRN semantics:
        /// the bare string "All" stops EVERY event, but an ARRAY (even one containing "All")
        /// is a list of literal event names to stop. So an array element "All" means an event
        /// literally named "All", not the stop-everything sentinel.
        /// </summary>
        private static void ParseInterruptCommands(WyvrnJsonValue commandObject, WyvrnParsedCommand parsed)
        {
            List<WyvrnJsonValue> asArray;
            if (commandObject.TryGetArray("Interrupts_Commands", out asArray))
            {
                for (int i = 0; i < asArray.Count; ++i)
                {
                    string name = asArray[i] != null ? asArray[i].AsString() : null;
                    if (!string.IsNullOrEmpty(name))
                    {
                        parsed.InterruptCommands.Add(name);
                    }
                }
                return;
            }

            string single;
            if (commandObject.TryGetString("Interrupts_Commands", out single) && !string.IsNullOrEmpty(single))
            {
                if (Matches(single, "All"))
                {
                    parsed.InterruptAll = true;
                }
                else
                {
                    parsed.InterruptCommands.Add(single);
                }
            }
        }

        private static WyvrnParsedEffect ParseHapticEvent(WyvrnJsonValue eventObject, string eventName)
        {
            WyvrnParsedEffect effect = new WyvrnParsedEffect();
            eventObject.TryGetString("Haptic_Effect", out effect.EffectName);

            double loop;
            if (eventObject.TryGetNumber("Loop", out loop))
            {
                effect.Loop = (int)loop;
            }

            string priorityString;
            eventObject.TryGetString("Priority", out priorityString);
            effect.Priority = ParsePriority(priorityString);

            string mixingString;
            eventObject.TryGetString("Mixing", out mixingString);
            effect.Mixing = ParseMixing(mixingString);

            List<WyvrnJsonValue> targeting;
            if (eventObject.TryGetArray("Targeting", out targeting))
            {
                for (int i = 0; i < targeting.Count; ++i)
                {
                    WyvrnJsonValue targetingObject = targeting[i];
                    if (targetingObject == null || !targetingObject.IsObject)
                    {
                        continue;
                    }

                    string targetString;
                    targetingObject.TryGetString("Target", out targetString);

                    WyvrnHapticRegion target;
                    if (!ParseTarget(targetString, out target))
                    {
                        Debug.LogWarning(string.Format(
                            "WyvrnConfigParser: unknown Target '{0}' in event '{1}'; skipping it.",
                            targetString, eventName));
                        continue;
                    }

                    double gain;
                    if (!targetingObject.TryGetNumber("Gain", out gain))
                    {
                        gain = 1.0;
                    }

                    string spatializationString;
                    targetingObject.TryGetString("Spatialization", out spatializationString);

                    WyvrnParsedTargeting parsedTargeting = new WyvrnParsedTargeting();
                    parsedTargeting.Target = target;
                    parsedTargeting.Side = ParseSpatialization(spatializationString);
                    parsedTargeting.Gain = (float)gain;
                    effect.Targeting.Add(parsedTargeting);
                }
            }

            return effect;
        }
    }
}
