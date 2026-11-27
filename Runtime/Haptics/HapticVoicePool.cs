// Copyright 2017-2025 Razer, Inc. All Rights Reserved.

using System;
using System.Collections.Generic;

namespace WyvrnSDK.Haptics
{
    /// <summary>
    /// Maps WYVRN events to HAR playback.
    ///
    /// HAR keys playback by material id and restarts a live id, so a single id cannot
    /// overlap itself. The pool therefore loads each effect under several ids
    /// (voices) for polyphony, reclaims a voice once its playback length elapses, and
    /// steals the lowest-priority voice when an effect is saturated.
    ///
    /// Voices are keyed by (effect, target-set): HAR's AddTargetToEvent is additive and
    /// has no clear, so a material id is bound to one target-set for its lifetime and
    /// its targets are applied once at preload. The same .haps used with Global vs Left
    /// vs Right therefore gets independent material ids and never conflates sides.
    ///
    /// It also arbitrates across every live voice: the highest-priority event playing
    /// silences all lower-priority events for its duration (priority ducking), and
    /// equal-priority events either merge or, when the newest is an Override event,
    /// play alone. Ducking mutes via SetIntensity(0) while the event keeps playing, so
    /// a ducked event's timeline still advances and it resumes mid-stream when the
    /// dominant event ends. Owned by the PS5 render worker; not thread-safe — every
    /// call must come from that one worker thread.
    ///
    /// A stiffness-carrying event LATCHES the DualSense adaptive trigger(s) its Hand
    /// sides address (Global = both, Left/Right = that one): playing the event arms
    /// the trigger with its stiffness envelope, and only interrupting the event (or
    /// stop-all / teardown) releases it — the natural end of playback does not,
    /// because the envelope maps trigger travel, not time, and so has no duration.
    /// Designers author an explicit OFF command (Interrupts_Commands) to clear it.
    /// Newest claim per trigger wins; a released trigger falls back to the newest
    /// surviving claim, and a trigger with no claim left is switched off.
    /// </summary>
    public sealed class HapticVoicePool : IDisposable
    {
        /// <summary>Adaptive-trigger claim bits (DualSense L2 / R2).</summary>
        private const byte TriggerLeft = 1 << 0;
        private const byte TriggerRight = 1 << 1;

        private sealed class Voice
        {
            public int MaterialId = -1;
            public double BusyUntilSeconds;
            public WyvrnHapticPriority Priority = WyvrnHapticPriority.VeryLow;
            public WyvrnHapticMixing Mixing = WyvrnHapticMixing.Merge;
            /// <summary>Intended intensity when this voice is audible (the event's Gain).</summary>
            public float Gain = 1.0f;
            /// <summary>Last intensity sent to HAR; -1 forces the next arbitration pass to set it.</summary>
            public float AppliedIntensity = -1.0f;
            /// <summary>Monotonic play order, so Override picks the newest equal-priority voice.</summary>
            public ulong Sequence;
            public bool Looping;
            /// <summary>Playing in HAR right now (possibly ducked to intensity 0).</summary>
            public bool Active;
        }

        /// <summary>
        /// A latched adaptive-trigger claim. Independent of voices: it is asserted when
        /// a stiffness-carrying event plays and removed only when that event is
        /// interrupted, so it outlives the voice's playback window.
        /// </summary>
        private sealed class TriggerClaim
        {
            /// <summary>WYVRN event that asserted the claim (what Interrupts_Commands releases).</summary>
            public string EventName;
            /// <summary>Material whose stiffness envelope is applied (any of the effect's pooled ids).</summary>
            public int MaterialId = -1;
            /// <summary>Triggers addressed (TriggerLeft / TriggerRight bits).</summary>
            public byte Mask;
            /// <summary>Monotonic assert order; the newest claim per trigger wins.</summary>
            public ulong Sequence;
        }

        /// <summary>
        /// A voice pool is identified by its effect plus the exact set of targets bound
        /// to its material ids (an order-independent signature of the (region, side) pairs).
        /// </summary>
        private struct PoolKey : IEquatable<PoolKey>
        {
            public int EffectId;
            public uint TargetSignature;

            public PoolKey(int effectId, uint targetSignature)
            {
                EffectId = effectId;
                TargetSignature = targetSignature;
            }

            public bool Equals(PoolKey other)
            {
                return EffectId == other.EffectId && TargetSignature == other.TargetSignature;
            }

            public override bool Equals(object obj)
            {
                return obj is PoolKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (EffectId * 397) ^ (int)TargetSignature;
            }
        }

        private readonly IInterhapticsRuntime runtime;
        private readonly int voicesPerEffect;
        private ulong nextSequence;
        private readonly Dictionary<PoolKey, List<Voice>> voicesByPool = new Dictionary<PoolKey, List<Voice>>();
        private readonly Dictionary<string, List<int>> activeVoicesByEvent = new Dictionary<string, List<int>>();
        /// <summary>Latched trigger claims, keyed by (event, material); released only by interrupts.</summary>
        private readonly List<TriggerClaim> triggerClaims = new List<TriggerClaim>();
        /// <summary>Material currently applied to each adaptive trigger (0 = L2, 1 = R2); -1 = released.</summary>
        private readonly int[] appliedTriggerMaterial = { -1, -1 };

        /// <summary>
        /// Gates whether latched claims actually reach the pad. False releases both
        /// triggers while preserving the claims, so they can be re-armed later.
        /// </summary>
        private bool adaptiveTriggersEnabled = true;

        /// <summary>Scratch list reused by the arbitration pass so a steady state allocates nothing.</summary>
        private readonly List<Voice> liveScratch = new List<Voice>();

        /// <summary>Scratch list for pruning emptied event keys out of activeVoicesByEvent.</summary>
        private readonly List<string> emptiedEventKeys = new List<string>();

        public HapticVoicePool(IInterhapticsRuntime interhapticsRuntime, int voicesPerEffectCount)
        {
            runtime = interhapticsRuntime;
            voicesPerEffect = Math.Max(1, voicesPerEffectCount);
        }

        /// <summary>Order-independent signature of an event's (region, side) target set.</summary>
        private static uint MakeTargetSignature(List<WyvrnHapticTarget> targets)
        {
            // Order-independent: each (region, side) maps to one bit. region in [0,4], side in [0,2].
            uint signature = 0;
            if (targets != null)
            {
                for (int i = 0; i < targets.Count; ++i)
                {
                    int bit = (int)targets[i].Region * 3 + (int)targets[i].Side;
                    signature |= 1u << bit;
                }
            }
            return signature;
        }

        /// <summary>Adaptive triggers a stiffness-carrying event claims, from its Hand sides; 0 without stiffness.</summary>
        private static byte MakeTriggerMask(WyvrnRuntimeEvent effectEntry)
        {
            if (!effectEntry.HasStiffness || effectEntry.Targets == null)
            {
                return 0;
            }

            byte mask = 0;
            for (int i = 0; i < effectEntry.Targets.Count; ++i)
            {
                WyvrnHapticTarget target = effectEntry.Targets[i];

                // Only the hand region maps to the controller, hence to its triggers.
                if (target.Region != WyvrnHapticRegion.Hand)
                {
                    continue;
                }
                switch (target.Side)
                {
                    case WyvrnHapticSide.Left:
                        mask |= TriggerLeft;
                        break;
                    case WyvrnHapticSide.Right:
                        mask |= TriggerRight;
                        break;
                    default:
                        mask |= TriggerLeft | TriggerRight;
                        break;
                }
            }
            return mask;
        }

        /// <summary>
        /// Leaves the pad neutral: an applied trigger effect outlives playback on the
        /// device, so it must be released explicitly on teardown. The backend disposes
        /// the pool before shutting the runtime down, so the runtime is still valid here.
        /// </summary>
        public void Dispose()
        {
            for (int side = 0; side < 2; ++side)
            {
                if (appliedTriggerMaterial[side] != -1)
                {
                    runtime.StopTriggerEffect(side == 0);
                    appliedTriggerMaterial[side] = -1;
                }
            }
        }

        /// <summary>Loads every distinct (effect, target-set) in data, voicesPerEffect times, via the runtime.</summary>
        public void Preload(WyvrnRuntimeData data)
        {
            for (int i = 0; i < data.Commands.Count; ++i)
            {
                List<WyvrnRuntimeEvent> effects = data.Commands[i].Effects;
                for (int j = 0; j < effects.Count; ++j)
                {
                    WyvrnRuntimeEvent effectEntry = effects[j];
                    if (effectEntry.EffectId < 0 || effectEntry.EffectId >= data.EffectJson.Count)
                    {
                        continue;
                    }

                    PoolKey key = new PoolKey(effectEntry.EffectId, MakeTargetSignature(effectEntry.Targets));
                    if (voicesByPool.ContainsKey(key))
                    {
                        continue;
                    }

                    List<Voice> voices = new List<Voice>(voicesPerEffect);
                    for (int index = 0; index < voicesPerEffect; ++index)
                    {
                        int materialId = runtime.AddMaterial(data.EffectJson[effectEntry.EffectId]);
                        if (materialId < 0)
                        {
                            break;
                        }

                        // Bind targets once, here: HAR's AddTargetToEvent is additive with no clear,
                        // so a material id keeps one fixed target-set for its whole lifetime.
                        for (int t = 0; t < effectEntry.Targets.Count; ++t)
                        {
                            runtime.AddTarget(materialId, effectEntry.Targets[t].Region, effectEntry.Targets[t].Side);
                        }

                        Voice voice = new Voice();
                        voice.MaterialId = materialId;
                        voices.Add(voice);
                    }
                    voicesByPool.Add(key, voices);
                }
            }
        }

        /// <summary>Applies command's Interrupts_Commands stops, then plays each of its effects.</summary>
        public void PlayCommand(WyvrnRuntimeCommand command, double nowSeconds)
        {
            if (WyvrnHapticsLog.VerboseEnabled)
            {
                WyvrnHapticsLog.Trace(string.Format(
                    "Trace [VoicePool]: PlayCommand('{0}') - effects={1} interruptAll={2} namedInterrupts={3}.",
                    command.EventName, command.Effects.Count, command.InterruptAll ? 1 : 0, command.InterruptCommands.Count));
            }

            // The bare-string "All" form (InterruptAll) stops every event; named interrupts
            // stop only the events they name (an event literally named "All" would match here).
            if (command.InterruptAll)
            {
                runtime.StopAll();
                DeactivateAllVoices();
                triggerClaims.Clear();
            }

            for (int i = 0; i < command.InterruptCommands.Count; ++i)
            {
                string interrupt = command.InterruptCommands[i];
                List<int> active;
                if (activeVoicesByEvent.TryGetValue(interrupt, out active))
                {
                    // Copy the ids first: DeactivateVoice mutates activeVoicesByEvent.
                    int[] materialIds = active.ToArray();
                    for (int m = 0; m < materialIds.Length; ++m)
                    {
                        runtime.Stop(materialIds[m]);
                        DeactivateVoice(materialIds[m]);
                    }
                    activeVoicesByEvent.Remove(interrupt);
                }

                // Release the interrupted event's latched trigger claims too. Deliberately
                // independent of the voice lookup above: a one-shot's voices may have retired
                // long ago while its latch is still holding a trigger.
                triggerClaims.RemoveAll(claim => claim.EventName == interrupt);
            }

            for (int i = 0; i < command.Effects.Count; ++i)
            {
                WyvrnRuntimeEvent effectEntry = command.Effects[i];

                List<Voice> voices = null;
                if (effectEntry.EffectId >= 0)
                {
                    voicesByPool.TryGetValue(
                        new PoolKey(effectEntry.EffectId, MakeTargetSignature(effectEntry.Targets)), out voices);
                }
                if (voices == null || voices.Count == 0)
                {
                    WyvrnHapticsLog.Trace(string.Format(
                        "Trace [VoicePool]: '{0}' effect id={1} has no preloaded voices; skipped.",
                        command.EventName, effectEntry.EffectId));
                    continue;
                }

                bool looping = effectEntry.Loop < 0;

                // Playing a stiffness-carrying event LATCHES its trigger claim, independent of
                // the voice below (which only spans the vibration window): the claim upserts on
                // (event, material) so replays re-assert ownership without stacking, and only
                // an interrupt releases it. Any pooled id works - they share one envelope.
                byte triggerMask = MakeTriggerMask(effectEntry);
                if (triggerMask != 0)
                {
                    int claimMaterial = voices[0].MaterialId;
                    TriggerClaim claim = null;
                    for (int c = 0; c < triggerClaims.Count; ++c)
                    {
                        if (triggerClaims[c].EventName == command.EventName && triggerClaims[c].MaterialId == claimMaterial)
                        {
                            claim = triggerClaims[c];
                            break;
                        }
                    }
                    if (claim == null)
                    {
                        claim = new TriggerClaim();
                        claim.EventName = command.EventName;
                        claim.MaterialId = claimMaterial;
                        triggerClaims.Add(claim);
                    }
                    claim.Mask = triggerMask;
                    claim.Sequence = nextSequence++;
                }

                // Re-triggering a still-playing looping event restarts its own voice instead of
                // layering another identical infinite loop, which would pile up active voices and
                // load the per-frame renderer. One-shots keep their polyphony.
                Voice voice = looping ? FindLiveVoiceForEvent(voices, command.EventName) : null;
                bool alreadyTracked = voice != null;
                if (voice == null)
                {
                    voice = AcquireVoice(voices, effectEntry.Priority);
                }
                if (voice == null)
                {
                    // Pool saturated with strictly higher-priority voices: the new play would be
                    // ducked to silence anyway, so drop it rather than evict a dominant event.
                    WyvrnHapticsLog.Trace(string.Format(
                        "Trace [VoicePool]: '{0}' effect id={1} saturated by higher-priority voices; dropped.",
                        command.EventName, effectEntry.EffectId));
                    continue;
                }

                // Targets are already bound to this material id at preload; just (re)start it.
                runtime.SetLoop(voice.MaterialId, effectEntry.Loop);
                runtime.Play(voice.MaterialId, nowSeconds);
                if (WyvrnHapticsLog.VerboseEnabled)
                {
                    WyvrnHapticsLog.Trace(string.Format(
                        "Trace [VoicePool]: '{0}' effect id={1} -> material {2}, {3}, priority={4} (playing).",
                        command.EventName, effectEntry.EffectId, voice.MaterialId,
                        looping ? "loop" : "one-shot", (int)effectEntry.Priority));
                }

                voice.Active = true;
                voice.Looping = looping;
                voice.Priority = effectEntry.Priority;
                voice.Mixing = effectEntry.Mixing;
                voice.Gain = effectEntry.Gain;
                voice.AppliedIntensity = -1.0f; // let arbitration apply the correct intensity below
                voice.Sequence = nextSequence++;
                voice.BusyUntilSeconds = looping
                    ? double.MaxValue
                    : nowSeconds + runtime.GetLength(voice.MaterialId);

                if (!alreadyTracked)
                {
                    List<int> tracked;
                    if (!activeVoicesByEvent.TryGetValue(command.EventName, out tracked))
                    {
                        tracked = new List<int>();
                        activeVoicesByEvent.Add(command.EventName, tracked);
                    }
                    tracked.Add(voice.MaterialId);
                }
            }

            // New voices may out-prioritize (or be out-prioritized by) what is already playing.
            UpdateArbitration();
            UpdateTriggerEffects();
        }

        /// <summary>
        /// Stops every voice and releases both adaptive triggers - the same teardown the
        /// bare-string "All" interrupt performs. Used when haptics are switched off
        /// wholesale, where nothing may be left playing or resisting.
        /// </summary>
        public void StopAll()
        {
            WyvrnHapticsLog.Trace("Trace [VoicePool]: StopAll - stopping every voice and releasing both adaptive triggers.");

            runtime.StopAll();
            DeactivateAllVoices();
            triggerClaims.Clear();
            // With no claims left this switches off whichever triggers were armed, and is a
            // no-op when none were.
            UpdateTriggerEffects();
        }

        /// <summary>
        /// Enables or disables the DualSense adaptive triggers independently of playback.
        /// Disabling releases whatever is armed; the underlying claims stay latched, so
        /// re-enabling re-arms whatever is still claimed. Events keep playing (and keep
        /// vibrating) either way - this gates only the physical trigger resistance.
        /// </summary>
        public void SetAdaptiveTriggersEnabled(bool enabled)
        {
            if (adaptiveTriggersEnabled == enabled)
            {
                return;
            }

            WyvrnHapticsLog.Trace("Trace [VoicePool]: adaptive triggers " + (enabled ? "ENABLED" : "DISABLED") + ".");

            adaptiveTriggersEnabled = enabled;
            // Claims are left untouched, so disabling releases the pad and enabling re-arms
            // from whatever is still claimed.
            UpdateTriggerEffects();
        }

        /// <summary>Reclaims voices whose playback length has elapsed. Call once per frame.</summary>
        public void Tick(double nowSeconds)
        {
            bool reclaimedAny = false;
            foreach (KeyValuePair<PoolKey, List<Voice>> pair in voicesByPool)
            {
                List<Voice> voices = pair.Value;
                for (int i = 0; i < voices.Count; ++i)
                {
                    Voice voice = voices[i];
                    if (voice.Active && !voice.Looping && voice.BusyUntilSeconds > 0.0 && voice.BusyUntilSeconds <= nowSeconds)
                    {
                        voice.Active = false;
                        voice.BusyUntilSeconds = 0.0;
                        voice.AppliedIntensity = -1.0f;
                        ForgetVoice(voice.MaterialId);
                        reclaimedAny = true;
                    }
                }
            }

            // A dominant voice that just ended must un-duck the lower-priority voices it was
            // masking. Trigger claims are deliberately untouched: a latch outlives its
            // voice's playback window and only an interrupt releases it.
            if (reclaimedAny)
            {
                UpdateArbitration();
            }
        }

        /// <summary>Number of voices currently playing across all pools (for profiling).</summary>
        public int GetActiveVoiceCount()
        {
            int count = 0;
            foreach (KeyValuePair<PoolKey, List<Voice>> pair in voicesByPool)
            {
                List<Voice> voices = pair.Value;
                for (int i = 0; i < voices.Count; ++i)
                {
                    if (voices[i].Active)
                    {
                        ++count;
                    }
                }
            }
            return count;
        }

        /// <summary>Returns a free voice, or steals one — but never evicts a strictly higher-priority voice.</summary>
        private Voice AcquireVoice(List<Voice> voices, WyvrnHapticPriority incomingPriority)
        {
            // Prefer a free (inactive) voice.
            for (int i = 0; i < voices.Count; ++i)
            {
                if (!voices[i].Active)
                {
                    ForgetVoice(voices[i].MaterialId);
                    return voices[i];
                }
            }

            // All voices busy: steal the lowest-priority / earliest-finishing one.
            Voice candidate = null;
            for (int i = 0; i < voices.Count; ++i)
            {
                Voice voice = voices[i];
                bool betterSteal = candidate == null
                    || voice.Priority < candidate.Priority
                    || (voice.Priority == candidate.Priority && voice.BusyUntilSeconds < candidate.BusyUntilSeconds);
                if (betterSteal)
                {
                    candidate = voice;
                }
            }

            // Never let a lower-priority newcomer evict a strictly higher-priority live voice.
            if (candidate == null || candidate.Priority > incomingPriority)
            {
                return null;
            }

            ForgetVoice(candidate.MaterialId);
            return candidate;
        }

        /// <summary>Finds the voice holding materialId across all pools, or null.</summary>
        private Voice FindVoice(int materialId)
        {
            foreach (KeyValuePair<PoolKey, List<Voice>> pair in voicesByPool)
            {
                List<Voice> voices = pair.Value;
                for (int i = 0; i < voices.Count; ++i)
                {
                    if (voices[i].MaterialId == materialId)
                    {
                        return voices[i];
                    }
                }
            }
            return null;
        }

        /// <summary>Finds a live voice in voices currently owned by eventName (for restarting a loop), or null.</summary>
        private Voice FindLiveVoiceForEvent(List<Voice> voices, string eventName)
        {
            List<int> active;
            if (!activeVoicesByEvent.TryGetValue(eventName, out active))
            {
                return null;
            }
            for (int i = 0; i < voices.Count; ++i)
            {
                if (voices[i].Active && active.Contains(voices[i].MaterialId))
                {
                    return voices[i];
                }
            }
            return null;
        }

        /// <summary>Stops tracking a stopped voice: clears its busy/loop state and active-event entries.</summary>
        private void DeactivateVoice(int materialId)
        {
            Voice voice = FindVoice(materialId);
            if (voice != null)
            {
                // Reset busy/loop state so the voice is reclaimable; a looping voice would
                // otherwise stay BusyUntil=MaxValue forever and leak (never freed by Tick).
                voice.Active = false;
                voice.Looping = false;
                voice.BusyUntilSeconds = 0.0;
                voice.AppliedIntensity = -1.0f;
            }
            ForgetVoice(materialId);
        }

        /// <summary>Stops tracking every voice (the "All" interrupt).</summary>
        private void DeactivateAllVoices()
        {
            foreach (KeyValuePair<PoolKey, List<Voice>> pair in voicesByPool)
            {
                List<Voice> voices = pair.Value;
                for (int i = 0; i < voices.Count; ++i)
                {
                    Voice voice = voices[i];
                    voice.Active = false;
                    voice.Looping = false;
                    voice.BusyUntilSeconds = 0.0;
                    voice.AppliedIntensity = -1.0f;
                }
            }
            activeVoicesByEvent.Clear();
        }

        /// <summary>Re-applies priority ducking / Override muting across all live voices.</summary>
        private void UpdateArbitration()
        {
            // Gather every voice currently playing across all pools.
            liveScratch.Clear();
            foreach (KeyValuePair<PoolKey, List<Voice>> pair in voicesByPool)
            {
                List<Voice> voices = pair.Value;
                for (int i = 0; i < voices.Count; ++i)
                {
                    if (voices[i].Active)
                    {
                        liveScratch.Add(voices[i]);
                    }
                }
            }
            if (liveScratch.Count == 0)
            {
                return;
            }

            // The highest priority playing dominates; everything below it is ducked.
            WyvrnHapticPriority maxPriority = liveScratch[0].Priority;
            for (int i = 0; i < liveScratch.Count; ++i)
            {
                if (liveScratch[i].Priority > maxPriority)
                {
                    maxPriority = liveScratch[i].Priority;
                }
            }

            // Among the top-priority voices, find the newest. If it is an Override event it
            // plays alone (silencing older equal-priority voices); otherwise they all merge.
            Voice newest = null;
            for (int i = 0; i < liveScratch.Count; ++i)
            {
                Voice voice = liveScratch[i];
                if (voice.Priority == maxPriority && (newest == null || voice.Sequence > newest.Sequence))
                {
                    newest = voice;
                }
            }
            bool overrideMixing = newest != null && newest.Mixing == WyvrnHapticMixing.Override;

            for (int i = 0; i < liveScratch.Count; ++i)
            {
                Voice voice = liveScratch[i];

                bool audible;
                if (voice.Priority < maxPriority)
                {
                    audible = false;
                }
                else
                {
                    audible = !overrideMixing || voice == newest;
                }

                float desired = audible ? voice.Gain : 0.0f;
                if (voice.AppliedIntensity != desired)
                {
                    runtime.SetIntensity(voice.MaterialId, desired);
                    voice.AppliedIntensity = desired;
                }
            }

            liveScratch.Clear();
        }

        /// <summary>Re-arms / releases the DualSense adaptive triggers from the latched claims.</summary>
        private void UpdateTriggerEffects()
        {
            // Per trigger, the newest latched claim wins (mirroring how Override picks the
            // newest voice); releasing it falls back to the newest survivor, and a trigger
            // with no claim left is switched off. Claims are independent of voices, so
            // neither the natural end of playback nor ducking disturbs an armed trigger.
            for (int side = 0; side < 2; ++side)
            {
                byte sideBit = side == 0 ? TriggerLeft : TriggerRight;

                TriggerClaim owner = null;
                for (int i = 0; i < triggerClaims.Count; ++i)
                {
                    TriggerClaim claim = triggerClaims[i];
                    if ((claim.Mask & sideBit) != 0 && (owner == null || claim.Sequence > owner.Sequence))
                    {
                        owner = claim;
                    }
                }

                // The provider holds one effect per trigger and the latest call wins, so a
                // changed owner is a single re-arm; an unchanged one needs no call (the
                // stiffness envelope is static per material).
                // Disabling the triggers is modelled as "no owner": the claims survive, they
                // just stop reaching the pad until the triggers are switched back on.
                int ownerMaterial = (adaptiveTriggersEnabled && owner != null) ? owner.MaterialId : -1;
                if (ownerMaterial == appliedTriggerMaterial[side])
                {
                    continue;
                }

                if (ownerMaterial != -1)
                {
                    if (runtime.StartTriggerEffect(ownerMaterial, side == 0))
                    {
                        appliedTriggerMaterial[side] = ownerMaterial;
                        WyvrnHapticsLog.Trace(string.Format("Trace [VoicePool]: adaptive trigger {0} -> material {1}.",
                            side == 0 ? "L2" : "R2", ownerMaterial));
                    }
                    else
                    {
                        // Pad rejected the arm (e.g. no controller yet). Keep the recorded pad
                        // state unchanged so the next lifecycle change retries this owner.
                        WyvrnHapticsLog.Trace(string.Format("Trace [VoicePool]: adaptive trigger {0} arm FAILED for material {1}; will retry.",
                            side == 0 ? "L2" : "R2", ownerMaterial));
                    }
                }
                else
                {
                    runtime.StopTriggerEffect(side == 0);
                    appliedTriggerMaterial[side] = -1;
                    WyvrnHapticsLog.Trace("Trace [VoicePool]: adaptive trigger " + (side == 0 ? "L2" : "R2") + " released.");
                }
            }
        }

        /// <summary>Drops materialId from every event's active-voice list and prunes emptied keys.</summary>
        private void ForgetVoice(int materialId)
        {
            emptiedEventKeys.Clear();
            foreach (KeyValuePair<string, List<int>> pair in activeVoicesByEvent)
            {
                pair.Value.Remove(materialId);
                if (pair.Value.Count == 0)
                {
                    emptiedEventKeys.Add(pair.Key);
                }
            }
            // Prune emptied event keys so the map can't grow unbounded. Collected first:
            // a Dictionary cannot be mutated while it is being enumerated.
            for (int i = 0; i < emptiedEventKeys.Count; ++i)
            {
                activeVoicesByEvent.Remove(emptiedEventKeys[i]);
            }
            emptiedEventKeys.Clear();
        }
    }
}
