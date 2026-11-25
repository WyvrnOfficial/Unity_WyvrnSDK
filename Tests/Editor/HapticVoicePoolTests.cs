// Copyright 2017-2025 Razer, Inc. All Rights Reserved.

using NUnit.Framework;

using WyvrnSDK.Haptics;

using static WyvrnSDK.Haptics.Tests.WyvrnTestData;

namespace WyvrnSDK.Haptics.Tests
{
    /// <summary>
    /// Voice pool behaviour: polyphony and voice stealing, interrupts, priority
    /// ducking, Merge/Override mixing, and the DualSense adaptive-trigger latch.
    ///
    /// The pool is the one piece of the PS5 path that is platform-agnostic (it talks to
    /// IInterhapticsRuntime, never to HAR directly), which is exactly why it carries
    /// the arbitration logic - it can be tested on the editor platform.
    /// </summary>
    [TestFixture]
    public class HapticVoicePoolTests
    {
        [Test]
        public void PolyphonyStealsAndReclaimsVoices()
        {
            WyvrnRuntimeData data = new WyvrnRuntimeData();
            data.EffectJson.Add("{}");
            data.Commands.Add(MakePlay("Effect1", 0, 0, WyvrnHapticPriority.High, WyvrnHapticMixing.Merge));

            MockInterhapticsRuntime runtime = new MockInterhapticsRuntime();
            HapticVoicePool pool = new HapticVoicePool(runtime, 2);
            pool.Preload(data);

            // Three concurrent plays of the same effect with a 2-voice pool: two distinct
            // voices, then a stolen reuse - never a silent restart of a single id.
            pool.PlayCommand(data.Commands[0], 0.0);
            pool.PlayCommand(data.Commands[0], 0.0);
            pool.PlayCommand(data.Commands[0], 0.0);

            Assert.AreEqual(3, runtime.Played.Count, "three plays issued");
            Assert.AreEqual(1, runtime.Played[0], "first voice");
            Assert.AreEqual(2, runtime.Played[1], "second voice gives polyphony");
            Assert.AreEqual(1, runtime.Played[2], "third reuses a stolen voice");

            // A command interrupting "Effect1" stops the voices it currently owns.
            pool.PlayCommand(MakeStop("Stop", "Effect1"), 0.0);
            Assert.Contains(1, runtime.Stopped, "interrupt stopped voice 1");
            Assert.Contains(2, runtime.Stopped, "interrupt stopped voice 2");

            // After the playback length elapses, voices free up and reclaim cleanly.
            pool.Tick(5.0);
            pool.PlayCommand(data.Commands[0], 5.0);
            Assert.AreEqual(4, runtime.Played.Count, "a play after reclaim issues one more");

            pool.Dispose();
        }

        [Test]
        public void BareStringAllInterruptCallsStopAll()
        {
            WyvrnRuntimeData data = new WyvrnRuntimeData();
            data.EffectJson.Add("{}");
            data.Commands.Add(MakePlay("Effect1", 0, 0, WyvrnHapticPriority.High, WyvrnHapticMixing.Merge));

            MockInterhapticsRuntime runtime = new MockInterhapticsRuntime();
            HapticVoicePool pool = new HapticVoicePool(runtime, 2);
            pool.Preload(data);

            pool.PlayCommand(data.Commands[0], 0.0);

            // A command flagged InterruptAll (the bare-string "All" form) stops every active
            // event through StopAll, rather than looking up an event named "All".
            pool.PlayCommand(MakeStopAll("Stop"), 0.0);

            Assert.AreEqual(1, runtime.StoppedAllCount, "\"All\" interrupt calls StopAll once");

            pool.Dispose();
        }

        [Test]
        public void HigherPriorityDucksLowerAndUnducksOnStop()
        {
            // Two looping effects of different priority. A (High) silences B (Low) while it
            // plays; stopping A un-ducks B mid-stream.
            WyvrnRuntimeData data = new WyvrnRuntimeData();
            data.EffectJson.Add("{}"); // EffectId 0 -> A
            data.EffectJson.Add("{}"); // EffectId 1 -> B
            data.Commands.Add(MakePlay("A", 0, -1, WyvrnHapticPriority.High, WyvrnHapticMixing.Merge));
            data.Commands.Add(MakePlay("B", 1, -1, WyvrnHapticPriority.Low, WyvrnHapticMixing.Merge));
            data.Commands.Add(MakeStop("StopA", "A"));

            MockInterhapticsRuntime runtime = new MockInterhapticsRuntime();
            HapticVoicePool pool = new HapticVoicePool(runtime, 1);
            pool.Preload(data);
            // One voice each, preloaded in command order: A -> id 1, B -> id 2.

            pool.PlayCommand(data.Commands[0], 0.0); // A (High)
            pool.PlayCommand(data.Commands[1], 0.0); // B (Low) -> ducked under A

            Assert.AreEqual(1.0f, runtime.IntensityOf(1), "A audible at full gain");
            Assert.AreEqual(0.0f, runtime.IntensityOf(2), "B ducked to silence under higher-priority A");

            pool.PlayCommand(data.Commands[2], 0.0); // StopA
            Assert.Contains(1, runtime.Stopped, "A stopped");
            Assert.AreEqual(1.0f, runtime.IntensityOf(2), "B restored to full gain once A stops");

            pool.Dispose();
        }

        [Test]
        public void MergeKeepsBothAudibleAndOverridePlaysAlone()
        {
            // Equal-priority events. Merge plays both; Override makes the newest play alone
            // and silences the older one, which resumes when the newer stops.
            WyvrnRuntimeData data = new WyvrnRuntimeData();
            data.EffectJson.Add("{}"); // EffectId 0 -> A
            data.EffectJson.Add("{}"); // EffectId 1 -> B (shared by BMerge/BOverride)
            data.Commands.Add(MakePlay("A", 0, -1, WyvrnHapticPriority.Medium, WyvrnHapticMixing.Merge));
            data.Commands.Add(MakePlay("BMerge", 1, -1, WyvrnHapticPriority.Medium, WyvrnHapticMixing.Merge));
            data.Commands.Add(MakePlay("BOverride", 1, -1, WyvrnHapticPriority.Medium, WyvrnHapticMixing.Override));
            data.Commands.Add(MakeStop("StopB", "BOverride"));
            data.Commands.Add(MakeStopAll("StopAll"));

            MockInterhapticsRuntime runtime = new MockInterhapticsRuntime();
            HapticVoicePool pool = new HapticVoicePool(runtime, 1);
            pool.Preload(data);
            // A -> id 1, B -> id 2.

            // Merge: both equal-priority events stay audible.
            pool.PlayCommand(data.Commands[0], 0.0); // A
            pool.PlayCommand(data.Commands[1], 0.0); // BMerge
            Assert.AreEqual(1.0f, runtime.IntensityOf(1), "A audible when merging");
            Assert.AreEqual(1.0f, runtime.IntensityOf(2), "B audible when merging");

            // Reset and exercise Override.
            pool.PlayCommand(data.Commands[4], 0.0); // StopAll

            pool.PlayCommand(data.Commands[0], 0.0); // A again -> id 1
            pool.PlayCommand(data.Commands[2], 0.0); // BOverride -> id 2, overrides A
            Assert.AreEqual(0.0f, runtime.IntensityOf(1), "A silenced by newer Override at equal priority");
            Assert.AreEqual(1.0f, runtime.IntensityOf(2), "Override event audible");

            pool.PlayCommand(data.Commands[3], 0.0); // StopB (the override)
            Assert.AreEqual(1.0f, runtime.IntensityOf(1), "A resumes once the Override stops");

            pool.Dispose();
        }

        [Test]
        public void InterruptedLoopingVoiceIsReusedNotLeaked()
        {
            // Regression for the looping-voice leak: a looping voice stopped via interrupt must
            // become free again (not stay permanently busy). LoopHi (loop) and OneShotLo
            // (one-shot) share one 2-voice pool so the freed loop voice can be observed being
            // reused.
            WyvrnRuntimeData data = new WyvrnRuntimeData();
            data.EffectJson.Add("{}");
            data.Commands.Add(MakePlay("LoopHi", 0, -1, WyvrnHapticPriority.High, WyvrnHapticMixing.Merge));
            data.Commands.Add(MakePlay("OneShotLo", 0, 0, WyvrnHapticPriority.Low, WyvrnHapticMixing.Merge));
            data.Commands.Add(MakeStop("StopLoop", "LoopHi"));

            MockInterhapticsRuntime runtime = new MockInterhapticsRuntime();
            HapticVoicePool pool = new HapticVoicePool(runtime, 2);
            pool.Preload(data);

            pool.PlayCommand(data.Commands[0], 0.0); // LoopHi -> voice 1 (looping, High)
            pool.PlayCommand(data.Commands[1], 0.0); // OneShotLo -> voice 2 (one-shot, Low)

            pool.PlayCommand(data.Commands[2], 0.0); // StopLoop -> stops & frees voice 1
            Assert.Contains(1, runtime.Stopped, "looping voice stopped");

            // The freed loop voice 1 must be reused next. Without the leak fix it would still
            // read as busy (Looping/BusyUntil=MaxValue), forcing the play to steal the Low
            // voice 2 instead.
            pool.PlayCommand(data.Commands[1], 0.0); // OneShotLo again
            Assert.AreEqual(3, runtime.Played.Count, "three plays total");
            Assert.AreEqual(1, runtime.Played[2], "freed looping voice 1 is reused, not leaked");

            pool.Dispose();
        }

        [Test]
        public void RetriggeringALoopCoalescesIntoOneVoice()
        {
            // Re-triggering a looping event restarts its single voice instead of stacking new
            // ones, so spamming a loop cannot pile up concurrent voices (the PS5 frame-drop fix).
            WyvrnRuntimeData data = new WyvrnRuntimeData();
            data.EffectJson.Add("{}");
            data.Commands.Add(MakePlay("Loop", 0, -1, WyvrnHapticPriority.High, WyvrnHapticMixing.Merge));

            MockInterhapticsRuntime runtime = new MockInterhapticsRuntime();
            HapticVoicePool pool = new HapticVoicePool(runtime, 4);
            pool.Preload(data);

            for (int index = 0; index < 5; ++index)
            {
                pool.PlayCommand(data.Commands[0], 0.0);
            }

            Assert.AreEqual(5, runtime.Played.Count, "five plays issued");
            for (int i = 0; i < runtime.Played.Count; ++i)
            {
                Assert.AreEqual(1, runtime.Played[i], "all loop re-triggers reuse the one voice (no pile-up)");
            }

            pool.Dispose();
        }

        [Test]
        public void GlobalStiffnessEventArmsBothTriggersAndInterruptReleasesThem()
        {
            // A stiffness-carrying event claims the adaptive triggers while it plays: a
            // Global hand side arms BOTH triggers with its material; an interrupt releases
            // them. An effect without a Stiffness track never touches the triggers.
            WyvrnRuntimeData data = new WyvrnRuntimeData();
            data.EffectJson.Add("{}"); // EffectId 0 -> stiffness-carrying "On"
            data.EffectJson.Add("{}"); // EffectId 1 -> plain vibration
            data.Commands.Add(MakeStiffnessPlay("On", 0, -1, WyvrnHapticSide.Global));
            data.Commands.Add(MakePlay("Plain", 1, 0, WyvrnHapticPriority.High, WyvrnHapticMixing.Merge));
            data.Commands.Add(MakeStop("Off", "On"));

            MockInterhapticsRuntime runtime = new MockInterhapticsRuntime();
            HapticVoicePool pool = new HapticVoicePool(runtime, 1);
            pool.Preload(data);
            // On -> material 1, Plain -> material 2.

            pool.PlayCommand(data.Commands[0], 0.0); // On
            Assert.AreEqual(2, runtime.TriggerStarts.Count, "both triggers armed by the Global stiffness event");
            Assert.AreEqual(1, runtime.CountTriggerStarts(1, true), "L2 armed with the event's material");
            Assert.AreEqual(1, runtime.CountTriggerStarts(1, false), "R2 armed with the event's material");

            pool.PlayCommand(data.Commands[1], 0.0); // Plain
            Assert.AreEqual(2, runtime.TriggerStarts.Count, "a stiffness-less event arms nothing");
            Assert.AreEqual(0, runtime.TriggerStops.Count, "a stiffness-less event releases nothing");

            pool.PlayCommand(data.Commands[2], 0.0); // Off (interrupts On)
            Assert.Contains(1, runtime.Stopped, "interrupt stopped the stiffness voice");
            Assert.AreEqual(2, runtime.TriggerStops.Count, "both triggers released on interrupt");
            Assert.Contains(true, runtime.TriggerStops, "L2 released");
            Assert.Contains(false, runtime.TriggerStops, "R2 released");

            pool.Dispose();
        }

        [Test]
        public void LeftSidedLatchOutlivesPlaybackAndOnlyTheInterruptReleases()
        {
            // A Left-sided stiffness one-shot arms only L2, and the latch OUTLIVES the
            // voice: the natural end of playback releases nothing; only the interrupt does.
            WyvrnRuntimeData data = new WyvrnRuntimeData();
            data.EffectJson.Add("{}");
            data.Commands.Add(MakeStiffnessPlay("FireL", 0, 0, WyvrnHapticSide.Left));
            data.Commands.Add(MakeStop("Off", "FireL"));

            MockInterhapticsRuntime runtime = new MockInterhapticsRuntime();
            HapticVoicePool pool = new HapticVoicePool(runtime, 1);
            pool.Preload(data);

            pool.PlayCommand(data.Commands[0], 0.0);
            Assert.AreEqual(1, runtime.TriggerStarts.Count, "only one trigger armed");
            Assert.AreEqual(1, runtime.CountTriggerStarts(1, true), "L2 armed");
            Assert.AreEqual(0, runtime.CountTriggerStarts(1, false), "R2 untouched");

            pool.Tick(5.0); // past the mock's 1s length: the one-shot's VOICE retires...
            Assert.AreEqual(0, runtime.TriggerStops.Count, "latch persists past natural end");

            pool.PlayCommand(data.Commands[1], 0.0); // ...but only the authored OFF releases
            Assert.AreEqual(1, runtime.TriggerStops.Count, "one release on interrupt");
            Assert.IsTrue(runtime.TriggerStops[0], "L2 released");

            pool.Dispose();
        }

        [Test]
        public void RightSidedLatchArmsOnlyR2()
        {
            // The Right-side mirror of the lateral test: only R2 is armed, and only the
            // authored OFF releases it.
            WyvrnRuntimeData data = new WyvrnRuntimeData();
            data.EffectJson.Add("{}");
            data.Commands.Add(MakeStiffnessPlay("FireR", 0, 0, WyvrnHapticSide.Right));
            data.Commands.Add(MakeStop("Off", "FireR"));

            MockInterhapticsRuntime runtime = new MockInterhapticsRuntime();
            HapticVoicePool pool = new HapticVoicePool(runtime, 1);
            pool.Preload(data);

            pool.PlayCommand(data.Commands[0], 0.0);
            Assert.AreEqual(1, runtime.TriggerStarts.Count, "only one trigger armed");
            Assert.AreEqual(1, runtime.CountTriggerStarts(1, false), "R2 armed");
            Assert.AreEqual(0, runtime.CountTriggerStarts(1, true), "L2 untouched");

            pool.Tick(5.0);
            Assert.AreEqual(0, runtime.TriggerStops.Count, "latch persists past natural end");

            pool.PlayCommand(data.Commands[1], 0.0);
            Assert.AreEqual(1, runtime.TriggerStops.Count, "one release on interrupt");
            Assert.IsFalse(runtime.TriggerStops[0], "R2 released");

            pool.Dispose();
        }

        [Test]
        public void NewestClaimOwnsTheTriggerAndHandsBackOnStop()
        {
            // Overlapping stiffness events: the newest claimant owns the trigger; stopping
            // it hands the trigger back to the survivor (a re-arm, not a release); stop-all
            // releases everything.
            WyvrnRuntimeData data = new WyvrnRuntimeData();
            data.EffectJson.Add("{}"); // EffectId 0 -> A
            data.EffectJson.Add("{}"); // EffectId 1 -> B
            data.Commands.Add(MakeStiffnessPlay("A", 0, -1, WyvrnHapticSide.Global));
            data.Commands.Add(MakeStiffnessPlay("B", 1, -1, WyvrnHapticSide.Global));
            data.Commands.Add(MakeStop("StopB", "B"));
            data.Commands.Add(MakeStopAll("StopAll"));

            MockInterhapticsRuntime runtime = new MockInterhapticsRuntime();
            HapticVoicePool pool = new HapticVoicePool(runtime, 1);
            pool.Preload(data);
            // A -> material 1, B -> material 2.

            pool.PlayCommand(data.Commands[0], 0.0); // A owns both triggers
            Assert.AreEqual(1, runtime.CountTriggerStarts(1, true), "A arms L2");
            Assert.AreEqual(1, runtime.CountTriggerStarts(1, false), "A arms R2");

            pool.PlayCommand(data.Commands[1], 0.0); // B is newer -> takes both triggers over
            Assert.AreEqual(1, runtime.CountTriggerStarts(2, true), "B re-arms L2");
            Assert.AreEqual(1, runtime.CountTriggerStarts(2, false), "B re-arms R2");
            Assert.AreEqual(0, runtime.TriggerStops.Count, "takeover is a re-arm, not a release");

            pool.PlayCommand(data.Commands[2], 0.0); // StopB -> triggers hand back to A
            Assert.AreEqual(2, runtime.CountTriggerStarts(1, true), "L2 handed back to A");
            Assert.AreEqual(2, runtime.CountTriggerStarts(1, false), "R2 handed back to A");
            Assert.AreEqual(0, runtime.TriggerStops.Count, "handoff is a re-arm, not a release");

            pool.PlayCommand(data.Commands[3], 0.0); // StopAll -> no claimant left
            Assert.AreEqual(2, runtime.TriggerStops.Count, "both triggers released by stop-all");
            Assert.Contains(true, runtime.TriggerStops, "L2 released");
            Assert.Contains(false, runtime.TriggerStops, "R2 released");

            pool.Dispose();
        }

        [Test]
        public void PartialLateralityOnlyTakesOverItsOwnSide()
        {
            // Partial laterality: a newer Left-only claim takes L2 but leaves R2 with the
            // older Global claim; stopping it hands only L2 back.
            WyvrnRuntimeData data = new WyvrnRuntimeData();
            data.EffectJson.Add("{}"); // EffectId 0 -> A (Global)
            data.EffectJson.Add("{}"); // EffectId 1 -> C (Left)
            data.Commands.Add(MakeStiffnessPlay("A", 0, -1, WyvrnHapticSide.Global));
            data.Commands.Add(MakeStiffnessPlay("C", 1, -1, WyvrnHapticSide.Left));
            data.Commands.Add(MakeStop("StopC", "C"));
            data.Commands.Add(MakeStopAll("StopAll"));

            MockInterhapticsRuntime runtime = new MockInterhapticsRuntime();
            HapticVoicePool pool = new HapticVoicePool(runtime, 1);
            pool.Preload(data);
            // A -> material 1, C -> material 2.

            pool.PlayCommand(data.Commands[0], 0.0); // A owns both
            pool.PlayCommand(data.Commands[1], 0.0); // C takes L2 only
            Assert.AreEqual(1, runtime.CountTriggerStarts(2, true), "C armed L2");
            Assert.AreEqual(0, runtime.CountTriggerStarts(2, false), "C never touches R2");
            Assert.AreEqual(1, runtime.CountTriggerStarts(1, false), "R2 not re-armed (still A's)");
            Assert.AreEqual(0, runtime.TriggerStops.Count, "no releases during takeover");

            pool.PlayCommand(data.Commands[2], 0.0); // StopC -> L2 back to A, R2 untouched
            Assert.AreEqual(2, runtime.CountTriggerStarts(1, true), "L2 handed back to A");
            Assert.AreEqual(1, runtime.CountTriggerStarts(1, false), "R2 still not re-armed");
            Assert.AreEqual(0, runtime.TriggerStops.Count, "handoff is a re-arm, not a release");

            pool.PlayCommand(data.Commands[3], 0.0); // StopAll
            Assert.AreEqual(2, runtime.TriggerStops.Count, "both triggers released");

            pool.Dispose();
        }

        [Test]
        public void RetriggeringALiveStiffnessLoopDoesNotRearmThePad()
        {
            // Re-triggering a live looping stiffness event coalesces into its own voice and
            // must NOT re-arm the pad every time (the owner material is unchanged).
            WyvrnRuntimeData data = new WyvrnRuntimeData();
            data.EffectJson.Add("{}");
            data.Commands.Add(MakeStiffnessPlay("Loop", 0, -1, WyvrnHapticSide.Global));

            MockInterhapticsRuntime runtime = new MockInterhapticsRuntime();
            HapticVoicePool pool = new HapticVoicePool(runtime, 4);
            pool.Preload(data);

            for (int index = 0; index < 3; ++index)
            {
                pool.PlayCommand(data.Commands[0], 0.0);
            }

            Assert.AreEqual(3, runtime.Played.Count, "three plays coalesce into one voice");
            Assert.AreEqual(2, runtime.TriggerStarts.Count, "triggers armed exactly once per side");
            Assert.AreEqual(0, runtime.TriggerStops.Count, "no spurious releases");

            pool.Dispose();
        }

        [Test]
        public void DisposeReleasesEveryArmedTrigger()
        {
            // The applied resistance outlives playback on the pad, so tearing the pool down
            // mid-claim must release every armed trigger.
            WyvrnRuntimeData data = new WyvrnRuntimeData();
            data.EffectJson.Add("{}");
            data.Commands.Add(MakeStiffnessPlay("On", 0, -1, WyvrnHapticSide.Global));

            MockInterhapticsRuntime runtime = new MockInterhapticsRuntime();
            using (HapticVoicePool pool = new HapticVoicePool(runtime, 1))
            {
                pool.Preload(data);
                pool.PlayCommand(data.Commands[0], 0.0);
                Assert.AreEqual(2, runtime.TriggerStarts.Count, "armed before teardown");
                Assert.AreEqual(0, runtime.TriggerStops.Count, "nothing released before teardown");
            }

            Assert.AreEqual(2, runtime.TriggerStops.Count, "both triggers released by Dispose");
            Assert.Contains(true, runtime.TriggerStops, "L2 released");
            Assert.Contains(false, runtime.TriggerStops, "R2 released");
        }

        [Test]
        public void DuckingDoesNotDisturbTheTriggerClaim()
        {
            // Priority ducking silences a voice's vibration but must not disturb its
            // adaptive-trigger claim: no release or re-arm across duck and un-duck.
            WyvrnRuntimeData data = new WyvrnRuntimeData();
            data.EffectJson.Add("{}"); // EffectId 0 -> stiffness A (High, from the helper)
            data.EffectJson.Add("{}"); // EffectId 1 -> dominant plain B (VeryHigh)
            data.Commands.Add(MakeStiffnessPlay("A", 0, -1, WyvrnHapticSide.Global));
            data.Commands.Add(MakePlay("B", 1, -1, WyvrnHapticPriority.VeryHigh, WyvrnHapticMixing.Merge));
            data.Commands.Add(MakeStop("StopB", "B"));

            MockInterhapticsRuntime runtime = new MockInterhapticsRuntime();
            HapticVoicePool pool = new HapticVoicePool(runtime, 1);
            pool.Preload(data);
            // A -> material 1, B -> material 2.

            pool.PlayCommand(data.Commands[0], 0.0); // A armed
            pool.PlayCommand(data.Commands[1], 0.0); // B ducks A's vibration
            Assert.AreEqual(0.0f, runtime.IntensityOf(1), "A's vibration ducked");
            Assert.AreEqual(2, runtime.TriggerStarts.Count, "claim kept while ducked (no extra arms)");
            Assert.AreEqual(0, runtime.TriggerStops.Count, "claim kept while ducked (no releases)");

            pool.PlayCommand(data.Commands[2], 0.0); // StopB un-ducks A
            Assert.AreEqual(1.0f, runtime.IntensityOf(1), "A's vibration restored");
            Assert.AreEqual(2, runtime.TriggerStarts.Count, "no trigger churn across un-duck");
            Assert.AreEqual(0, runtime.TriggerStops.Count, "still no releases");

            pool.Dispose();
        }

        [Test]
        public void LatchSurvivesCompleteVoiceRetirement()
        {
            // The latch is independent of the voice, so it must survive the voice's complete
            // retirement (a stiffness-only .haps: GetLength 0, the voice retires on the very
            // next Tick), replays must re-assert without re-arming the pad, and the authored
            // OFF must release it even though no voice is live anymore.
            WyvrnRuntimeData data = new WyvrnRuntimeData();
            data.EffectJson.Add("{}");
            data.Commands.Add(MakeStiffnessPlay("Bow", 0, 0, WyvrnHapticSide.Global));
            data.Commands.Add(MakeStop("Release", "Bow"));

            MockInterhapticsRuntime runtime = new MockInterhapticsRuntime();
            HapticVoicePool pool = new HapticVoicePool(runtime, 2);
            pool.Preload(data);
            runtime.LengthOverrides[1] = 0.0; // materials are stiffness-only
            runtime.LengthOverrides[2] = 0.0;

            pool.PlayCommand(data.Commands[0], 1.0);
            Assert.AreEqual(2, runtime.TriggerStarts.Count, "both triggers armed");

            pool.Tick(100.0); // the zero-length voice retires immediately...
            Assert.AreEqual(0, pool.GetActiveVoiceCount(), "voice retired");
            Assert.AreEqual(0, runtime.TriggerStops.Count, "latch survives the voice");

            pool.PlayCommand(data.Commands[0], 101.0); // replay re-asserts the same material
            Assert.AreEqual(2, runtime.TriggerStarts.Count, "no re-arm on replay of the same claim");

            pool.Tick(200.0);
            pool.PlayCommand(data.Commands[1], 201.0); // authored OFF, long after any voice ended
            Assert.AreEqual(2, runtime.TriggerStops.Count, "interrupt releases both triggers");
            Assert.Contains(true, runtime.TriggerStops, "L2 released");
            Assert.Contains(false, runtime.TriggerStops, "R2 released");

            pool.Dispose();
        }

        [Test]
        public void RejectedArmIsRetriedOnTheNextLifecycleChange()
        {
            // A pad-rejected arm (e.g. controller not connected yet) must not be recorded as
            // applied: the next lifecycle change retries it.
            WyvrnRuntimeData data = new WyvrnRuntimeData();
            data.EffectJson.Add("{}"); // EffectId 0 -> stiffness A
            data.EffectJson.Add("{}"); // EffectId 1 -> plain B
            data.Commands.Add(MakeStiffnessPlay("A", 0, -1, WyvrnHapticSide.Global));
            data.Commands.Add(MakePlay("B", 1, 0, WyvrnHapticPriority.High, WyvrnHapticMixing.Merge));
            data.Commands.Add(MakeStop("StopA", "A"));

            MockInterhapticsRuntime runtime = new MockInterhapticsRuntime();
            HapticVoicePool pool = new HapticVoicePool(runtime, 1);
            pool.Preload(data);
            // A -> material 1, B -> material 2.

            runtime.FailTriggerStarts = 2; // both sides' first arm gets rejected
            pool.PlayCommand(data.Commands[0], 0.0);
            Assert.AreEqual(2, runtime.TriggerStarts.Count, "both arms attempted");

            pool.PlayCommand(data.Commands[1], 0.0); // any lifecycle change retries the arms
            Assert.AreEqual(2, runtime.CountTriggerStarts(1, true), "both arms retried");
            Assert.AreEqual(2, runtime.CountTriggerStarts(1, false), "both arms retried (right)");

            pool.PlayCommand(data.Commands[2], 0.0); // StopA releases the now-applied claims
            Assert.AreEqual(2, runtime.TriggerStops.Count, "released after successful retry");

            pool.Dispose();
        }

        [Test]
        public void StopAllStopsVoicesAndReleasesTriggersIdempotently()
        {
            // StopAll() is what the render worker calls when haptics are switched off at the
            // master control. Silencing is not enough there: adaptive-trigger resistance is
            // deliberately independent of the vibration gain, so a latched claim would keep
            // the trigger stiff. StopAll must stop the voices AND release both triggers.
            WyvrnRuntimeData data = new WyvrnRuntimeData();
            data.EffectJson.Add("{}"); // EffectId 0 -> stiffness-carrying, looping
            data.EffectJson.Add("{}"); // EffectId 1 -> plain vibration
            data.Commands.Add(MakeStiffnessPlay("Hold", 0, -1, WyvrnHapticSide.Global));
            data.Commands.Add(MakePlay("Plain", 1, -1, WyvrnHapticPriority.High, WyvrnHapticMixing.Merge));

            MockInterhapticsRuntime runtime = new MockInterhapticsRuntime();
            HapticVoicePool pool = new HapticVoicePool(runtime, 1);
            pool.Preload(data);

            pool.PlayCommand(data.Commands[0], 0.0); // Hold -> arms L2 + R2
            pool.PlayCommand(data.Commands[1], 0.0); // Plain -> a second live voice
            Assert.AreEqual(2, runtime.TriggerStarts.Count, "both triggers armed before the gain drops");
            Assert.AreEqual(2, pool.GetActiveVoiceCount(), "two voices live before the gain drops");

            pool.StopAll();
            Assert.AreEqual(1, runtime.StoppedAllCount, "HAR told to stop everything");
            Assert.AreEqual(0, pool.GetActiveVoiceCount(), "no voice left playing");
            Assert.AreEqual(2, runtime.TriggerStops.Count, "both triggers released");
            Assert.Contains(true, runtime.TriggerStops, "L2 released");
            Assert.Contains(false, runtime.TriggerStops, "R2 released");

            // Idempotent: a second call must not re-issue trigger releases, or every frame at
            // gain 0 would spam scePadSetTriggerEffect.
            pool.StopAll();
            Assert.AreEqual(2, runtime.TriggerStops.Count, "no duplicate trigger releases");

            pool.Dispose();
        }

        [Test]
        public void AdaptiveTriggerToggleKeepsClaimsLatched()
        {
            // The adaptive triggers have their own on/off, independent of playback and of the
            // vibration gain. Disabling releases the pad but keeps the claims latched, so
            // re-enabling re-arms them; playback itself is never disturbed.
            WyvrnRuntimeData data = new WyvrnRuntimeData();
            data.EffectJson.Add("{}");
            data.Commands.Add(MakeStiffnessPlay("Hold", 0, -1, WyvrnHapticSide.Global));

            MockInterhapticsRuntime runtime = new MockInterhapticsRuntime();
            HapticVoicePool pool = new HapticVoicePool(runtime, 1);
            pool.Preload(data);

            pool.PlayCommand(data.Commands[0], 0.0); // arms L2 + R2 with material 1
            Assert.AreEqual(2, runtime.TriggerStarts.Count, "both triggers armed");

            pool.SetAdaptiveTriggersEnabled(false);
            Assert.AreEqual(2, runtime.TriggerStops.Count, "disabling releases both triggers");
            Assert.AreEqual(1, pool.GetActiveVoiceCount(), "disabling does not stop the voice");
            Assert.AreEqual(2, runtime.TriggerStarts.Count, "disabling issues no new arms");

            // Redundant toggles must not reach the pad at all.
            pool.SetAdaptiveTriggersEnabled(false);
            Assert.AreEqual(2, runtime.TriggerStops.Count, "re-disabling is a no-op");

            // While disabled, a newly played stiffness event claims but must not arm.
            pool.PlayCommand(data.Commands[0], 1.0);
            Assert.AreEqual(2, runtime.TriggerStarts.Count, "no arming while disabled");

            pool.SetAdaptiveTriggersEnabled(true);
            Assert.AreEqual(4, runtime.TriggerStarts.Count, "re-enabling re-arms from the surviving claims");

            pool.Dispose();
        }

        [Test]
        public void DistinctTargetSetsGetIndependentVoicePools()
        {
            // HAR's AddTargetToEvent is additive with no clear, so a material id is bound to
            // one target-set for life. The same .haps played Global, Left and Right must
            // therefore resolve to three independent pools - never one pool whose sides get
            // conflated.
            WyvrnRuntimeData data = new WyvrnRuntimeData();
            data.EffectJson.Add("{}");
            data.Commands.Add(MakeStiffnessPlay("Global", 0, 0, WyvrnHapticSide.Global));
            data.Commands.Add(MakeStiffnessPlay("Left", 0, 0, WyvrnHapticSide.Left));
            data.Commands.Add(MakeStiffnessPlay("Right", 0, 0, WyvrnHapticSide.Right));

            MockInterhapticsRuntime runtime = new MockInterhapticsRuntime();
            HapticVoicePool pool = new HapticVoicePool(runtime, 1);
            pool.Preload(data);

            // One voice per distinct (effect, target-set): three materials, not one.
            Assert.AreEqual(4, runtime.NextId, "three distinct target-sets loaded three materials");

            pool.PlayCommand(data.Commands[0], 0.0);
            pool.PlayCommand(data.Commands[1], 0.0);
            pool.PlayCommand(data.Commands[2], 0.0);

            Assert.AreEqual(3, runtime.Played.Count, "each target-set plays its own material");
            Assert.AreEqual(1, runtime.Played[0], "Global uses the first material");
            Assert.AreEqual(2, runtime.Played[1], "Left uses the second material");
            Assert.AreEqual(3, runtime.Played[2], "Right uses the third material");

            pool.Dispose();
        }
    }
}
