// Copyright 2017-2025 Razer, Inc. All Rights Reserved.

using System.Collections.Generic;
using System.Globalization;
using System.Threading;

using NUnit.Framework;

using UnityEngine;

using WyvrnSDK.Haptics;
using WyvrnSDK.Haptics.EditorTools;

namespace WyvrnSDK.Haptics.Tests
{
    /// <summary>
    /// WYVRN.config parsing: the real schema, the two Interrupts_Commands forms, the
    /// "All" target, and the failure modes.
    /// </summary>
    [TestFixture]
    public class WyvrnConfigParserTests
    {
        [Test]
        public void ParsesEventsTargetingAndInterrupts()
        {
            // A command with a Head event (Global, 0.7) and a Hand event (Left, 1.0,
            // VeryHigh priority, Override mixing, infinite loop), plus an interrupt. Uses
            // the real WYVRN.config schema: "Interrupts_Commands", "Mixing", "Spatialization".
            const string config = "{ \"ExternalCommands\": [ { \"External_Command_ID\": \"Effect1\", " +
                "\"Haptic_Events\": [ { \"Haptic_Effect\": \"Effect1\", \"Loop\": 0, \"Priority\": \"High\", " +
                "\"Mixing\": \"Merge\", \"Targeting\": [ { \"Gain\": 0.7, \"Spatialization\": \"Global\", " +
                "\"Target\": \"Head\" } ] }, { \"Haptic_Effect\": \"Effect1\", \"Loop\": -1, " +
                "\"Priority\": \"VeryHigh\", \"Mixing\": \"Override\", \"Targeting\": [ { \"Gain\": 1.0, " +
                "\"Spatialization\": \"Left\", \"Target\": \"Hand\" } ] } ], " +
                "\"Interrupts_Commands\": \"Effect2\" } ] }";

            List<WyvrnParsedCommand> commands = new List<WyvrnParsedCommand>();
            string error;
            Assert.IsTrue(WyvrnConfigParser.Parse(config, commands, out error), "parse succeeds: " + error);

            Assert.AreEqual(1, commands.Count, "one command parsed");
            WyvrnParsedCommand command = commands[0];
            Assert.AreEqual("Effect1", command.EventName, "event name");
            Assert.AreEqual(2, command.Effects.Count, "two haptic events");
            Assert.AreEqual(1, command.InterruptCommands.Count, "interrupt command captured");
            Assert.AreEqual("Effect2", command.InterruptCommands[0], "interrupt target name");

            WyvrnParsedEffect handEvent = command.Effects[1];
            Assert.AreEqual(-1, handEvent.Loop, "hand event loops infinitely");
            Assert.AreEqual(WyvrnHapticPriority.VeryHigh, handEvent.Priority, "hand event priority VeryHigh");
            Assert.AreEqual(WyvrnHapticMixing.Override, handEvent.Mixing, "hand event mixing Override");
            Assert.AreEqual(1, handEvent.Targeting.Count, "hand event has one targeting");
            Assert.AreEqual(WyvrnHapticSide.Left, handEvent.Targeting[0].Side, "hand targeting spatialization Left");

            // The Head event's fractional gain must survive as 0.7, which is also the
            // regression guard for culture-sensitive number parsing.
            Assert.AreEqual(0.7f, command.Effects[0].Targeting[0].Gain, 0.0001f, "head event keeps its 0.7 gain");
        }

        [Test]
        public void AllTargetSurvivesAsHand()
        {
            // Target "All" is HAR's root body-part group and subsumes Hand, so it must survive
            // as a Hand target. Regressing this does not just drop the target: the event loses
            // all targeting, gets discarded, and a command with no other content vanishes
            // entirely.
            const string config = "{ \"ExternalCommands\": [ { \"External_Command_ID\": \"Throw_Trad\", " +
                "\"Haptic_Events\": [ { \"Haptic_Effect\": \"Throw_Trad\", \"Loop\": 1, \"Mixing\": \"Merge\", " +
                "\"Targeting\": [ { \"Gain\": 1, \"Spatialization\": \"Global\", \"Target\": \"All\" } ] } ] } ] }";

            List<WyvrnParsedCommand> commands = new List<WyvrnParsedCommand>();
            string error;
            Assert.IsTrue(WyvrnConfigParser.Parse(config, commands, out error), "All-target parse succeeds: " + error);

            Assert.AreEqual(1, commands.Count, "All-target command survives");
            Assert.AreEqual(1, commands[0].Effects.Count, "All-target event kept");
            Assert.AreEqual(1, commands[0].Effects[0].Targeting.Count, "All-target has one targeting");
            Assert.AreEqual(WyvrnHapticRegion.Hand, commands[0].Effects[0].Targeting[0].Target,
                "All maps to the Hand region");
            Assert.AreEqual(WyvrnHapticSide.Global, commands[0].Effects[0].Targeting[0].Side,
                "All keeps Global spatialization");
        }

        [Test]
        public void BareStringAllIsTheStopAllSentinel()
        {
            // Interrupts_Commands as the bare string "All" is the stop-all sentinel (kept even
            // though it has no effects and no named interrupts).
            const string config = "{ \"ExternalCommands\": [ { \"External_Command_ID\": \"StopEverything\", " +
                "\"Interrupts_Commands\": \"All\" } ] }";

            List<WyvrnParsedCommand> commands = new List<WyvrnParsedCommand>();
            string error;
            Assert.IsTrue(WyvrnConfigParser.Parse(config, commands, out error), "stop-all parse succeeds: " + error);

            Assert.AreEqual(1, commands.Count, "stop-all command kept");
            Assert.IsTrue(commands[0].InterruptAll, "InterruptAll set for bare string All");
            Assert.AreEqual(0, commands[0].InterruptCommands.Count, "no named interrupts for bare string All");
        }

        [Test]
        public void ArrayAllIsALiteralEventName()
        {
            // Interrupts_Commands as an array containing "All" means a literal event named "All".
            const string config = "{ \"ExternalCommands\": [ { \"External_Command_ID\": \"StopNamed\", " +
                "\"Interrupts_Commands\": [ \"All\" ] } ] }";

            List<WyvrnParsedCommand> commands = new List<WyvrnParsedCommand>();
            string error;
            Assert.IsTrue(WyvrnConfigParser.Parse(config, commands, out error), "named-all parse succeeds: " + error);

            Assert.AreEqual(1, commands.Count, "named-all command kept");
            Assert.IsFalse(commands[0].InterruptAll, "InterruptAll NOT set for array All");
            Assert.AreEqual(1, commands[0].InterruptCommands.Count, "array All is one literal name");
            Assert.AreEqual("All", commands[0].InterruptCommands[0], "the literal name is kept verbatim");
        }

        [Test]
        public void ChromaOnlyCommandsAreDropped()
        {
            const string config = "{ \"ExternalCommands\": [ { \"External_Command_ID\": \"Start\", " +
                "\"Chroma_Events\": [ { \"Chroma_Effect\": \"Idle_Keyboard\" } ] } ] }";

            List<WyvrnParsedCommand> commands = new List<WyvrnParsedCommand>();
            string error;
            Assert.IsTrue(WyvrnConfigParser.Parse(config, commands, out error), "chroma-only parse succeeds: " + error);
            Assert.AreEqual(0, commands.Count, "chroma-only yields no commands");
        }

        [Test]
        public void MalformedJsonFailsWithAnError()
        {
            List<WyvrnParsedCommand> commands = new List<WyvrnParsedCommand>();
            string error;
            Assert.IsFalse(WyvrnConfigParser.Parse("{ not json", commands, out error), "malformed JSON fails");
            Assert.IsNotEmpty(error, "malformed JSON reports an error");
        }

        [Test]
        public void MissingExternalCommandsArrayFails()
        {
            List<WyvrnParsedCommand> commands = new List<WyvrnParsedCommand>();
            string error;
            Assert.IsFalse(WyvrnConfigParser.Parse("{ \"Something\": 1 }", commands, out error),
                "a config without ExternalCommands fails");
            Assert.IsNotEmpty(error, "it reports an error");
        }

        [Test]
        public void UnknownPriorityFallsBackToMediumWithAWarning()
        {
            // An unknown Priority must not become the dominant VeryHigh by accident: a
            // malformed event that ducks everything else is far worse than one that is ducked.
            const string config = "{ \"ExternalCommands\": [ { \"External_Command_ID\": \"Odd\", " +
                "\"Haptic_Events\": [ { \"Haptic_Effect\": \"Odd\", \"Priority\": \"Screaming\", " +
                "\"Targeting\": [ { \"Gain\": 1, \"Spatialization\": \"Global\", \"Target\": \"Hand\" } ] } ] } ] }";

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("unknown Priority"));

            List<WyvrnParsedCommand> commands = new List<WyvrnParsedCommand>();
            string error;
            Assert.IsTrue(WyvrnConfigParser.Parse(config, commands, out error), "parse succeeds: " + error);
            Assert.AreEqual(1, commands.Count, "the command is kept");
            Assert.AreEqual(WyvrnHapticPriority.Medium, commands[0].Effects[0].Priority,
                "unknown priority defaults to Medium");
        }

        [Test]
        public void FractionalGainsParseUnderACommaDecimalCulture()
        {
            // Regression guard: a machine whose culture uses a comma decimal separator must
            // still read "Gain": 0.7 as 0.7 and not as 7 (or fail outright), which would
            // silently change every authored gain in the config.
            CultureInfo original = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-FR");

                const string config = "{ \"ExternalCommands\": [ { \"External_Command_ID\": \"E\", " +
                    "\"Haptic_Events\": [ { \"Haptic_Effect\": \"E\", \"Loop\": -1, " +
                    "\"Targeting\": [ { \"Gain\": 0.35, \"Spatialization\": \"Right\", \"Target\": \"Hand\" } ] } ] } ] }";

                List<WyvrnParsedCommand> commands = new List<WyvrnParsedCommand>();
                string error;
                Assert.IsTrue(WyvrnConfigParser.Parse(config, commands, out error), "parse succeeds: " + error);
                Assert.AreEqual(0.35f, commands[0].Effects[0].Targeting[0].Gain, 0.0001f,
                    "0.35 stays 0.35 under a comma-decimal culture");
                Assert.AreEqual(-1, commands[0].Effects[0].Loop, "a negative loop count still parses");
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }
    }

    /// <summary>
    /// .haps stiffness detection: this single boolean decides whether an event drives
    /// the DualSense adaptive triggers at all, across both authored file versions.
    /// </summary>
    [TestFixture]
    public class WyvrnHapsFileTests
    {
        [Test]
        public void V5StiffnessMelodyWithANoteIsDetected()
        {
            // A .haps with an authored Stiffness track: one melody holding one note whose
            // amplitude envelope maps trigger travel to resistance. Real .haps v5 shape:
            // authored stiffness notes carry all their scalars INSIDE m_hapticEffect
            // (unlike vibration notes, which keep m_gain/m_length at the note level).
            const string haps = "{ \"m_version\": \"5\", \"m_stiffness\": { \"m_gain\": 1.0, \"m_loop\": 0, " +
                "\"m_melodies\": [ { \"m_gain\": 1.0, \"m_mute\": 0, \"m_notes\": [ { \"m_hapticEffect\": { " +
                "\"m_amplitudeModulation\": { \"m_extrapolationStrategy\": 1, \"m_keyframes\": [ " +
                "{ \"m_time\": 0.0, \"m_value\": 0.0 }, { \"m_time\": 1.0, \"m_value\": 1.0 } ] }, " +
                "\"m_gain\": 1, \"m_length\": 1, \"m_priority\": 1, \"m_startingPoint\": 0 } } ] } ], " +
                "\"m_signalEvaluationMethod\": 3 }, \"m_vibration\": { \"m_melodies\": [] } }";
            Assert.IsTrue(WyvrnHapsFile.HasStiffnessTrack(haps), "stiffness melody with a note is detected");
        }

        [Test]
        public void V5EmptyLayersAreNotATrack()
        {
            // The common case: the m_stiffness block exists but its melody list is empty.
            const string emptyMelodies = "{ \"m_stiffness\": { \"m_gain\": 1.0, \"m_loop\": 0, \"m_melodies\": [], " +
                "\"m_signalEvaluationMethod\": 3 }, \"m_vibration\": { \"m_melodies\": [ { \"m_notes\": [ " +
                "{ \"m_gain\": 1.0 } ] } ] } }";
            Assert.IsFalse(WyvrnHapsFile.HasStiffnessTrack(emptyMelodies), "empty stiffness melody list is not a track");

            // Authoring tools can leave an empty layer: a melody with zero notes.
            const string emptyNotes = "{ \"m_stiffness\": { \"m_melodies\": [ { \"m_gain\": 1.0, \"m_mute\": 0, " +
                "\"m_notes\": [] } ] } }";
            Assert.IsFalse(WyvrnHapsFile.HasStiffnessTrack(emptyNotes), "stiffness melody without notes is not a track");
        }

        [Test]
        public void V6StiffnessCurveWithKeyframesIsDetected()
        {
            // .haps v6: no "m_" prefix and no melodies/notes - the stiffness stream is one
            // interpolation curve. Shape taken from a real v6 export (Interhaptics Composer).
            const string haps = "{ \"version\": \"6\", \"gain\": 1.0, \"stiffness\": { \"gain\": 1.0, " +
                "\"interpolation_function\": \"Cubic\", \"keyframes\": [ { \"position\": 0.0, \"slope\": 0.0, " +
                "\"value\": 0.0 }, { \"position\": 1.0, \"slope\": 0.0, \"value\": 0.96 } ] }, " +
                "\"vibration\": { \"melodies\": [ { \"notes\": [] } ], \"transients\": [] } }";
            Assert.IsTrue(WyvrnHapsFile.HasStiffnessTrack(haps), "v6 stiffness curve with keyframes is detected");
        }

        [Test]
        public void V6EmptyOrAbsentStiffnessIsNotATrack()
        {
            // Same empty-layer rule as v5: the block exists but nothing was authored into it.
            const string emptyKeyframes = "{ \"version\": \"6\", \"stiffness\": { \"gain\": 1.0, " +
                "\"interpolation_function\": \"Cubic\", \"keyframes\": [] }, \"vibration\": { \"melodies\": [ " +
                "{ \"notes\": [ { \"gain\": 1.0 } ] } ] } }";
            Assert.IsFalse(WyvrnHapsFile.HasStiffnessTrack(emptyKeyframes), "v6 stiffness with no keyframes is not a track");

            const string noKeyframesField = "{ \"version\": \"6\", \"stiffness\": { \"gain\": 1.0 } }";
            Assert.IsFalse(WyvrnHapsFile.HasStiffnessTrack(noKeyframesField),
                "v6 stiffness without a keyframes field is not a track");

            // A v6 vibration-only effect (the common case) must stay false: no stiffness block.
            const string vibrationOnly = "{ \"version\": \"6\", \"gain\": 1.0, \"vibration\": { \"melodies\": [ " +
                "{ \"notes\": [ { \"gain\": 1.0 } ] } ], \"transients\": [] } }";
            Assert.IsFalse(WyvrnHapsFile.HasStiffnessTrack(vibrationOnly), "v6 vibration-only effect is not a track");
        }

        [Test]
        public void VibrationOnlyAndMalformedInputAreNotATrack()
        {
            const string noStiffnessBlock = "{ \"m_vibration\": { \"m_melodies\": [ { \"m_notes\": [ " +
                "{ \"m_gain\": 1.0 } ] } ] } }";
            Assert.IsFalse(WyvrnHapsFile.HasStiffnessTrack(noStiffnessBlock), "no m_stiffness block is not a track");
            Assert.IsFalse(WyvrnHapsFile.HasStiffnessTrack("{ not json"), "malformed JSON is not a track");
            Assert.IsFalse(WyvrnHapsFile.HasStiffnessTrack(string.Empty), "empty input is not a track");
            Assert.IsFalse(WyvrnHapsFile.HasStiffnessTrack(null), "null input is not a track");
        }
    }

    /// <summary>
    /// The hand-rolled JSON reader the importer relies on. Covers what the WYVRN
    /// schema actually uses plus the shapes that would silently corrupt an import.
    /// </summary>
    [TestFixture]
    public class WyvrnJsonTests
    {
        [Test]
        public void ParsesTheShapesTheWyvrnSchemaUses()
        {
            const string text = "{ \"s\": \"text\", \"n\": -12.5e1, \"b\": true, \"z\": null, " +
                "\"arr\": [ 1, \"two\", { \"k\": 3 } ], \"obj\": { \"nested\": \"yes\" } }";

            WyvrnJsonValue root;
            string error;
            Assert.IsTrue(WyvrnJson.TryParse(text, out root, out error), "parse succeeds: " + error);
            Assert.IsTrue(root.IsObject, "root is an object");

            string s;
            Assert.IsTrue(root.TryGetString("s", out s));
            Assert.AreEqual("text", s);

            double n;
            Assert.IsTrue(root.TryGetNumber("n", out n));
            Assert.AreEqual(-125.0, n, 0.0001, "exponent notation parses");

            List<WyvrnJsonValue> arr;
            Assert.IsTrue(root.TryGetArray("arr", out arr));
            Assert.AreEqual(3, arr.Count, "mixed-type array");
            Assert.AreEqual("two", arr[1].AsString(), "array element strings are readable");

            WyvrnJsonValue obj;
            Assert.IsTrue(root.TryGetObject("obj", out obj));
            string nested;
            Assert.IsTrue(obj.TryGetString("nested", out nested));
            Assert.AreEqual("yes", nested);

            // A field of the wrong type must read as absent rather than throwing, which is
            // what lets the parser treat Interrupts_Commands as "array or else string".
            List<WyvrnJsonValue> notAnArray;
            Assert.IsFalse(root.TryGetArray("s", out notAnArray), "a string field is not an array");
            string notAString;
            Assert.IsFalse(root.TryGetString("arr", out notAString), "an array field is not a string");
            Assert.IsNull(root.GetField("missing"), "an absent field reads as null");
        }

        [Test]
        public void HandlesEscapesAndByteOrderMarks()
        {
            // The WYVRN tooling writes a UTF-8 BOM, which is not part of the JSON grammar.
            const string text = "﻿{ \"escaped\": \"a\\\"b\\\\c\\nd\\u00e9\" }";

            WyvrnJsonValue root;
            string error;
            Assert.IsTrue(WyvrnJson.TryParse(text, out root, out error), "a BOM-prefixed document parses: " + error);

            string escaped;
            Assert.IsTrue(root.TryGetString("escaped", out escaped));
            Assert.AreEqual("a\"b\\c\ndé", escaped, "escapes decode, including \\u");
        }

        [Test]
        public void RejectsMalformedInput()
        {
            string[] malformed =
            {
                "{ not json",
                "{ \"a\": }",
                "{ \"a\" 1 }",
                "{ \"a\": 1,, }",
                "[ 1, 2",
                "{ \"a\": \"unterminated",
                "{ } trailing",
                string.Empty,
                null,
            };

            for (int i = 0; i < malformed.Length; ++i)
            {
                WyvrnJsonValue root;
                string error;
                Assert.IsFalse(WyvrnJson.TryParse(malformed[i], out root, out error),
                    "rejects: " + (malformed[i] ?? "<null>"));
                Assert.IsNotEmpty(error, "reports an error for: " + (malformed[i] ?? "<null>"));
                Assert.IsNull(root, "no partial tree is handed back");
            }
        }

        [Test]
        public void ParsesEmptyContainers()
        {
            WyvrnJsonValue root;
            string error;
            Assert.IsTrue(WyvrnJson.TryParse("{ \"a\": [], \"b\": {} }", out root, out error), error);

            List<WyvrnJsonValue> emptyArray;
            Assert.IsTrue(root.TryGetArray("a", out emptyArray));
            Assert.AreEqual(0, emptyArray.Count);

            WyvrnJsonValue emptyObject;
            Assert.IsTrue(root.TryGetObject("b", out emptyObject));
            Assert.IsNull(emptyObject.GetField("anything"));
        }
    }

    /// <summary>Output-path rules the importer enforces before it writes anything.</summary>
    [TestFixture]
    public class WyvrnImportSettingsTests
    {
        [Test]
        public void OnlyResourcesFoldersAreAcceptedAsOutput()
        {
            // The runtime loads the baked data with Resources.Load and nothing in the project
            // references it, so anywhere outside a Resources folder it is stripped from the
            // player build and every event silently stops playing.
            Assert.IsTrue(WyvrnConfigImporter.IsResourcesPath("Assets/Resources"));
            Assert.IsTrue(WyvrnConfigImporter.IsResourcesPath("Assets/Game/Resources"));
            Assert.IsTrue(WyvrnConfigImporter.IsResourcesPath("Assets/Game/Resources/"));
            Assert.IsTrue(WyvrnConfigImporter.IsResourcesPath("Assets\\Game\\Resources"));
            Assert.IsTrue(WyvrnConfigImporter.IsResourcesPath("resources"), "the check is case-insensitive");

            Assert.IsFalse(WyvrnConfigImporter.IsResourcesPath("Assets"));
            Assert.IsFalse(WyvrnConfigImporter.IsResourcesPath("Assets/Wyvrn"));
            Assert.IsFalse(WyvrnConfigImporter.IsResourcesPath("Assets/Resources/Wyvrn"),
                "a subfolder of Resources is not itself the Resources root");
            Assert.IsFalse(WyvrnConfigImporter.IsResourcesPath(string.Empty));
            Assert.IsFalse(WyvrnConfigImporter.IsResourcesPath(null));
        }

        [Test]
        public void OutputFolderMatchesTheRuntimeLoadPath()
        {
            // The written location and the runtime's Resources.Load path are derived from the
            // same constant precisely so they cannot drift apart; this pins that down.
            WyvrnImportSettings settings = WyvrnImportSettings.instance;
            string originalRoot = settings.ResourcesRoot;
            try
            {
                settings.ResourcesRoot = "Assets/Resources";
                Assert.AreEqual("Assets/Resources/Wyvrn", settings.GetOutputFolder(),
                    "baked under <Resources>/<dir of ResourcePath>");
                Assert.AreEqual("Assets/Resources/Wyvrn/HapticEffect", settings.GetEffectFolder(),
                    "effects go in a subfolder of it");

                settings.ResourcesRoot = "Assets/Game/Resources";
                Assert.AreEqual("Assets/Game/Resources/Wyvrn", settings.GetOutputFolder(),
                    "a relocated Resources root is honoured");
            }
            finally
            {
                settings.ResourcesRoot = originalRoot;
            }
        }
    }
}
