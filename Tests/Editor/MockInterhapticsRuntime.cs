// Copyright 2017-2025 Razer, Inc. All Rights Reserved.

using System.Collections.Generic;

using WyvrnSDK.Haptics;

namespace WyvrnSDK.Haptics.Tests
{
    /// <summary>
    /// Records the HAR calls the voice pool issues so the tests can assert on them.
    /// Stands in for the PS5 InterhapticsRuntime, which is why the pool talks to an
    /// interface at all.
    /// </summary>
    internal sealed class MockInterhapticsRuntime : IInterhapticsRuntime
    {
        public int NextId = 1;
        public readonly List<int> Played = new List<int>();
        public readonly List<int> Stopped = new List<int>();
        public int StoppedAllCount;
        public readonly Dictionary<int, float> LastIntensity = new Dictionary<int, float>();

        /// <summary>Last SetGlobalIntensity factor; -1 means it was never called.</summary>
        public float LastGlobalIntensity = -1.0f;

        /// <summary>(material id, leftTrigger) per StartTriggerEffect call, in order.</summary>
        public readonly List<KeyValuePair<int, bool>> TriggerStarts = new List<KeyValuePair<int, bool>>();

        /// <summary>leftTrigger per StopTriggerEffect call, in order.</summary>
        public readonly List<bool> TriggerStops = new List<bool>();

        /// <summary>Fail this many upcoming StartTriggerEffect calls (simulates a pad-rejected arm).</summary>
        public int FailTriggerStarts;

        /// <summary>Per-material GetLength override, in seconds; unlisted materials return 1.0.</summary>
        public readonly Dictionary<int, double> LengthOverrides = new Dictionary<int, double>();

        public bool Initialize()
        {
            return true;
        }

        public void Shutdown()
        {
        }

        public bool IsAvailable
        {
            get { return true; }
        }

        public int AddMaterial(string materialJson)
        {
            return NextId++;
        }

        public void SetIntensity(int materialId, float intensity)
        {
            LastIntensity[materialId] = intensity;
        }

        public void SetGlobalIntensity(float intensity)
        {
            LastGlobalIntensity = intensity;
        }

        public void SetLoop(int materialId, int numLoops)
        {
        }

        public void AddTarget(int materialId, WyvrnHapticRegion region, WyvrnHapticSide side)
        {
        }

        public void Play(int materialId, double timeSeconds)
        {
            Played.Add(materialId);
        }

        public void Stop(int materialId)
        {
            Stopped.Add(materialId);
        }

        public void StopAll()
        {
            ++StoppedAllCount;
        }

        public double GetLength(int materialId)
        {
            double length;
            return LengthOverrides.TryGetValue(materialId, out length) ? length : 1.0;
        }

        public void Render(double timeSeconds)
        {
        }

        public bool StartTriggerEffect(int materialId, bool leftTrigger)
        {
            TriggerStarts.Add(new KeyValuePair<int, bool>(materialId, leftTrigger));
            if (FailTriggerStarts > 0)
            {
                --FailTriggerStarts;
                return false;
            }
            return true;
        }

        public void StopTriggerEffect(bool leftTrigger)
        {
            TriggerStops.Add(leftTrigger);
        }

        /// <summary>Intensity last applied to materialId, or 0 when it was never set.</summary>
        public float IntensityOf(int materialId)
        {
            float intensity;
            return LastIntensity.TryGetValue(materialId, out intensity) ? intensity : 0.0f;
        }

        /// <summary>How many times materialId was armed on the given trigger.</summary>
        public int CountTriggerStarts(int materialId, bool leftTrigger)
        {
            int count = 0;
            for (int i = 0; i < TriggerStarts.Count; ++i)
            {
                if (TriggerStarts[i].Key == materialId && TriggerStarts[i].Value == leftTrigger)
                {
                    ++count;
                }
            }
            return count;
        }
    }

    /// <summary>Builders for the runtime snapshots the pool consumes.</summary>
    internal static class WyvrnTestData
    {
        public static WyvrnRuntimeEvent MakeEvent(
            int effectId, int loop, WyvrnHapticPriority priority, WyvrnHapticMixing mixing)
        {
            WyvrnRuntimeEvent runtimeEvent = new WyvrnRuntimeEvent();
            runtimeEvent.EffectId = effectId;
            runtimeEvent.Gain = 1.0f;
            runtimeEvent.Loop = loop;
            runtimeEvent.Priority = priority;
            runtimeEvent.Mixing = mixing;
            runtimeEvent.Targets.Add(new WyvrnHapticTarget(WyvrnHapticRegion.Hand, WyvrnHapticSide.Global));
            return runtimeEvent;
        }

        public static WyvrnRuntimeCommand MakePlay(
            string name, int effectId, int loop, WyvrnHapticPriority priority, WyvrnHapticMixing mixing)
        {
            WyvrnRuntimeCommand command = new WyvrnRuntimeCommand();
            command.EventName = name;
            command.Effects.Add(MakeEvent(effectId, loop, priority, mixing));
            return command;
        }

        public static WyvrnRuntimeCommand MakeStop(string name, string target)
        {
            WyvrnRuntimeCommand command = new WyvrnRuntimeCommand();
            command.EventName = name;
            command.InterruptCommands.Add(target);
            return command;
        }

        /// <summary>The bare-string "All" stop-all form (vs MakeStop, which lists a literal event name).</summary>
        public static WyvrnRuntimeCommand MakeStopAll(string name)
        {
            WyvrnRuntimeCommand command = new WyvrnRuntimeCommand();
            command.EventName = name;
            command.InterruptAll = true;
            return command;
        }

        /// <summary>A play command whose effect carries a Stiffness track, hand-targeted on side.</summary>
        public static WyvrnRuntimeCommand MakeStiffnessPlay(string name, int effectId, int loop, WyvrnHapticSide side)
        {
            WyvrnRuntimeCommand command = MakePlay(name, effectId, loop, WyvrnHapticPriority.High, WyvrnHapticMixing.Merge);
            command.Effects[0].HasStiffness = true;
            command.Effects[0].Targets[0] = new WyvrnHapticTarget(WyvrnHapticRegion.Hand, side);
            return command;
        }
    }
}
