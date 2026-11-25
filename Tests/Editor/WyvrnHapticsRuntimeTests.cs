// Copyright 2017-2025 Razer, Inc. All Rights Reserved.

using System.Collections.Generic;

using NUnit.Framework;

using UnityEngine;

using WyvrnSDK;
using WyvrnSDK.Haptics;

namespace WyvrnSDK.Haptics.Tests
{
    /// <summary>
    /// The 0..100 vibration gain and its mapping onto HAR's global intensity factor.
    /// </summary>
    [TestFixture]
    public class WyvrnHapticGainTests
    {
        [Test]
        public void TheEndsOfTheRangeAreTheContract()
        {
            // 100 is HAR's documented base value (a factor of exactly 1, i.e. untouched),
            // and 0 is genuine silence.
            Assert.AreEqual(1.0f, WyvrnHapticGain.ToScalar(100), "100 maps to a factor of 1");
            Assert.AreEqual(0.0f, WyvrnHapticGain.ToScalar(0), "0 maps to a factor of 0");
            Assert.AreEqual(0.5f, WyvrnHapticGain.ToScalar(50), "50 maps to a factor of 0.5");
        }

        [Test]
        public void OutOfRangeInputIsClamped()
        {
            // HAR clamps only the low end and has no ceiling of its own, so the upper clamp is
            // ours: a factor above 1 scales the per-band amplitude past full scale and clips.
            Assert.AreEqual(1.0f, WyvrnHapticGain.ToScalar(250), "above the range clamps to 1");
            Assert.AreEqual(0.0f, WyvrnHapticGain.ToScalar(-5), "below the range clamps to 0");
            Assert.AreEqual(1.0f, WyvrnHapticGain.ToScalar(int.MaxValue), "int.MaxValue clamps to 1");
            Assert.AreEqual(0.0f, WyvrnHapticGain.ToScalar(int.MinValue), "int.MinValue clamps to 0");
        }

        [Test]
        public void ClampingIsIdempotent()
        {
            // The cached gain is the clamped one, so a Get never reports a value that was
            // never applied - hence clamping has to be idempotent.
            Assert.AreEqual(37, WyvrnHapticGain.Clamp(37), "in-range values pass through");
            Assert.AreEqual(100, WyvrnHapticGain.Clamp(WyvrnHapticGain.Clamp(250)), "clamping is idempotent");
            Assert.AreEqual(0, WyvrnHapticGain.Clamp(WyvrnHapticGain.Clamp(-250)),
                "clamping is idempotent below the range");
        }
    }

    /// <summary>
    /// The plain snapshot the render worker runs on. Everything the worker needs has to
    /// cross this boundary, because it may never touch a UnityEngine.Object.
    /// </summary>
    [TestFixture]
    public class WyvrnRuntimeDataTests
    {
        [Test]
        public void BuildCarriesTheStiffnessFlagAndSharesEffectIds()
        {
            // The worker only sees the snapshot, so Build must carry the baked
            // HasStiffnessTrack flag through - dropping it would silently disable the whole
            // adaptive-trigger feature with the pool tests still green.
            WyvrnHapticEffect stiffEffect = ScriptableObject.CreateInstance<WyvrnHapticEffect>();
            stiffEffect.SetImportedData("{\"stiff\":1}", "Stiff", true);

            WyvrnHapticEffect plainEffect = ScriptableObject.CreateInstance<WyvrnHapticEffect>();
            plainEffect.SetImportedData("{\"plain\":1}", "Plain", false);

            WyvrnHapticData data = ScriptableObject.CreateInstance<WyvrnHapticData>();

            WyvrnHapticCommand command = new WyvrnHapticCommand();
            command.EventName = "Cmd";
            command.Effects.Add(new WyvrnHapticEvent { Effect = stiffEffect });
            command.Effects.Add(new WyvrnHapticEvent { Effect = plainEffect });
            command.Effects.Add(new WyvrnHapticEvent { Effect = stiffEffect });
            data.SetImportedData(new List<WyvrnHapticCommand> { command }, "Test App");

            try
            {
                WyvrnRuntimeData runtime = WyvrnRuntimeData.Build(data);

                Assert.AreEqual(1, runtime.Commands.Count, "one command");
                List<WyvrnRuntimeEvent> effects = runtime.Commands[0].Effects;
                Assert.AreEqual(3, effects.Count, "three events");

                Assert.IsTrue(effects[0].HasStiffness, "stiffness flag mirrored");
                Assert.IsFalse(effects[1].HasStiffness, "plain effect stays plain");
                Assert.IsTrue(effects[2].HasStiffness, "shared effect keeps the flag");
                Assert.AreEqual(effects[0].EffectId, effects[2].EffectId, "shared effect shares its EffectId");
                Assert.AreNotEqual(effects[0].EffectId, effects[1].EffectId, "distinct effects get distinct ids");
                Assert.AreEqual(2, runtime.EffectJson.Count, "two distinct .haps payloads");
                Assert.AreEqual("{\"stiff\":1}", runtime.EffectJson[effects[0].EffectId], "the JSON is carried verbatim");
            }
            finally
            {
                Object.DestroyImmediate(data);
                Object.DestroyImmediate(stiffEffect);
                Object.DestroyImmediate(plainEffect);
            }
        }

        [Test]
        public void EventsWithoutAnEffectAreSkippedAndCommandsSurvive()
        {
            // A command whose .haps was missing at import still carries its interrupts, so
            // it must survive the snapshot even with no playable effect left.
            WyvrnHapticData data = ScriptableObject.CreateInstance<WyvrnHapticData>();

            WyvrnHapticCommand command = new WyvrnHapticCommand();
            command.EventName = "StopEverything";
            command.Effects.Add(new WyvrnHapticEvent { Effect = null });
            command.InterruptAll = true;
            data.SetImportedData(new List<WyvrnHapticCommand> { command }, "Test App");

            try
            {
                WyvrnRuntimeData runtime = WyvrnRuntimeData.Build(data);

                Assert.AreEqual(1, runtime.Commands.Count, "the command survives");
                Assert.AreEqual(0, runtime.Commands[0].Effects.Count, "the effect-less event is dropped");
                Assert.IsTrue(runtime.Commands[0].InterruptAll, "its stop-all flag is carried");
                Assert.IsNotNull(runtime.FindCommand("StopEverything"), "it is findable by name");
                Assert.IsNull(runtime.FindCommand("Nope"), "an unknown name finds nothing");
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void BuildOfNullDataIsAnEmptySnapshot()
        {
            // The backend builds a snapshot even when the baked asset is missing, so that
            // path must not throw: it simply plays nothing.
            WyvrnRuntimeData runtime = WyvrnRuntimeData.Build(null);
            Assert.AreEqual(0, runtime.Commands.Count);
            Assert.AreEqual(0, runtime.EffectJson.Count);
            Assert.IsNull(runtime.FindCommand("anything"));
        }

        [Test]
        public void FindCommandOnTheAssetMatchesByName()
        {
            WyvrnHapticData data = ScriptableObject.CreateInstance<WyvrnHapticData>();
            WyvrnHapticCommand command = new WyvrnHapticCommand();
            command.EventName = "Effect1";
            data.SetImportedData(new List<WyvrnHapticCommand> { command }, "Test App");

            try
            {
                Assert.IsNotNull(data.FindCommand("Effect1"));
                Assert.IsNull(data.FindCommand("effect1"), "the lookup is case-sensitive, like SetEventName");
                Assert.IsNull(data.FindCommand("Missing"));
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }
    }

    /// <summary>
    /// The three haptic controls are PS5-only, but their methods exist on every platform
    /// so a shared settings menu can call them unconditionally. This pins down that
    /// doing so off PS5 is inert and safe: a "not supported" result and a defined
    /// out-parameter, never a crash or a stale value a settings widget would echo back.
    /// </summary>
    [TestFixture]
    public class WyvrnHapticControlsTests
    {
        [Test]
        public void SettersAreInertOffPs5()
        {
            // Including values that would be out of range on PS5. Reaching the next line at
            // all is most of the point: these must not throw off PS5.
            Assert.AreEqual(RazerErrors.RZRESULT_NOT_SUPPORTED, WyvrnAPI.SetHapticsEnabled(false));
            Assert.AreEqual(RazerErrors.RZRESULT_NOT_SUPPORTED, WyvrnAPI.SetHapticsEnabled(true));
            Assert.AreEqual(RazerErrors.RZRESULT_NOT_SUPPORTED, WyvrnAPI.SetVibrationGain(0));
            Assert.AreEqual(RazerErrors.RZRESULT_NOT_SUPPORTED, WyvrnAPI.SetVibrationGain(100));
            Assert.AreEqual(RazerErrors.RZRESULT_NOT_SUPPORTED, WyvrnAPI.SetVibrationGain(-5000));
            Assert.AreEqual(RazerErrors.RZRESULT_NOT_SUPPORTED, WyvrnAPI.SetVibrationGain(int.MaxValue));
            Assert.AreEqual(RazerErrors.RZRESULT_NOT_SUPPORTED, WyvrnAPI.SetAdaptiveTriggersEnabled(false));
            Assert.AreEqual(RazerErrors.RZRESULT_NOT_SUPPORTED, WyvrnAPI.SetAdaptiveTriggersEnabled(true));
        }

        [Test]
        public void GettersAlwaysWriteADefinedValueOffPs5()
        {
            // Seed each with a value the implementation would never choose, so a missing
            // write is visible.
            bool haptics = true;
            Assert.AreEqual(RazerErrors.RZRESULT_NOT_SUPPORTED, WyvrnAPI.GetHapticsEnabled(out haptics));
            Assert.IsFalse(haptics, "haptics report disabled off PS5");

            int gain = -1;
            Assert.AreEqual(RazerErrors.RZRESULT_NOT_SUPPORTED, WyvrnAPI.GetVibrationGain(out gain));
            Assert.AreEqual(100, gain, "gain reports unattenuated off PS5");

            bool triggers = true;
            Assert.AreEqual(RazerErrors.RZRESULT_NOT_SUPPORTED, WyvrnAPI.GetAdaptiveTriggersEnabled(out triggers));
            Assert.IsFalse(triggers, "adaptive triggers report disabled off PS5");
        }
    }
}
