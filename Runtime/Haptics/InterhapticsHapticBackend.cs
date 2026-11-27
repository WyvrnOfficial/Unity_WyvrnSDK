// Copyright 2017-2025 Razer, Inc. All Rights Reserved.

#if UNITY_PS5

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

using Unity.Profiling;

using UnityEngine;
using UnityEngine.Profiling;

namespace WyvrnSDK.Haptics
{
    /// <summary>
    /// PS5 IWyvrnHapticBackend.
    ///
    /// The HAR synthesis is expensive and must not run on the main thread, so all HAR
    /// access lives on a dedicated render worker. The main thread only snapshots the
    /// baked data (a ScriptableObject read, done once at Initialize), starts and stops
    /// the worker, and enqueues event names. HAR is therefore touched by exactly one
    /// thread and needs no internal locking.
    ///
    /// Inert when the baked data is missing or the HAR PRX fail to load: the worker
    /// comes up not-ready, IsInitialized returns false, and SetEventName is dropped.
    /// </summary>
    public sealed class InterhapticsHapticBackend : IWyvrnHapticBackend
    {
        /// <summary>HAR material ids loaded per effect, giving polyphony and voice stealing.</summary>
        private const int VoicesPerEffect = 4;

        /// <summary>
        /// The worker wakes every ~8 ms (~120 Hz) to drain queued triggers, and renders at
        /// ~60 Hz gated on elapsed time so the rate holds despite sleep jitter. Tune
        /// RenderIntervalSeconds if the feel needs it.
        /// </summary>
        private const int QueueWaitMilliseconds = 8;

        private const double RenderIntervalSeconds = 1.0 / 60.0;

        /// <summary>How long Shutdown waits for the worker to finish its teardown before giving up.</summary>
        private const int WorkerJoinTimeoutMilliseconds = 2000;

        // Profiler markers for the PS5 haptics path. The render worker registers itself
        // with the profiler (Profiler.BeginThreadProfiling), so its samples show up as
        // their own thread rather than being dropped - which is the only way to see what
        // HAR synthesis actually costs per frame on a devkit. All of this compiles out of
        // a non-development build.
        private static readonly ProfilerMarker MarkerSetEvent = new ProfilerMarker("Wyvrn.Haptics.SetEvent");
        private static readonly ProfilerMarker MarkerRenderLoop = new ProfilerMarker("Wyvrn.Haptics.RenderLoop");
        private static readonly ProfilerMarker MarkerHarRender = new ProfilerMarker("Wyvrn.Haptics.HARRender");

        private Renderer renderer;
        private Thread thread;

        // Authoritative copies of the three controls, main thread only (like renderer /
        // thread). They live on the backend rather than the worker because the backend
        // spans the whole SDK lifetime while the worker is destroyed and rebuilt by
        // every Shutdown()/Initialize() pair - and HAR resets its own global intensity to
        // 1.0 on each Init() - so they must be held somewhere that outlives both and
        // re-asserted on bring-up.
        private bool hapticsEnabled = true;
        private int vibrationGain = 100;
        private bool adaptiveTriggersEnabled = true;

        public bool IsInitialized
        {
            get { return renderer != null && renderer.IsReady; }
        }

        public bool AreHapticsEnabled
        {
            get { return hapticsEnabled; }
        }

        public int VibrationGain
        {
            get { return vibrationGain; }
        }

        public bool AreAdaptiveTriggersEnabled
        {
            get { return adaptiveTriggersEnabled; }
        }

        public bool Initialize()
        {
            if (thread != null)
            {
                return true;
            }

            // Snapshot the baked data on the MAIN THREAD (UnityEngine.Object access is not
            // thread-safe); the worker then runs purely on the self-contained plain copy.
            WyvrnHapticData dataAsset = Resources.Load<WyvrnHapticData>(WyvrnHapticData.ResourcePath);
            WyvrnRuntimeData runtimeData;
            if (dataAsset != null)
            {
                runtimeData = WyvrnRuntimeData.Build(dataAsset);
            }
            else
            {
                runtimeData = new WyvrnRuntimeData();
                WyvrnHapticsLog.Warning(string.Format(
                    "haptic data 'Resources/{0}' not found; no events will play. Run Tools > WYVRN > Import WYVRN Haptics.",
                    WyvrnHapticData.ResourcePath));
            }

            // Seed the worker from the cached controls so values set before InitSDK still apply.
            renderer = new Renderer(runtimeData, hapticsEnabled, vibrationGain, adaptiveTriggersEnabled);

            // Background thread: it must never keep the process alive if the title tears
            // down without reaching Shutdown().
            thread = new Thread(renderer.Run);
            thread.Name = "WyvrnHapticRenderer";
            thread.IsBackground = true;
            try
            {
                thread.Priority = System.Threading.ThreadPriority.AboveNormal;
            }
            catch (PlatformNotSupportedException)
            {
                // Priority is advisory; carry on at the default.
            }

            try
            {
                thread.Start();
            }
            catch (SystemException exception)
            {
                thread = null;
                renderer = null;
                WyvrnHapticsLog.Warning("failed to start the haptic render worker. " + exception.Message);
                return false;
            }

            WyvrnHapticsLog.Info("Interhaptics haptic backend started.");
            return true;
        }

        public void Shutdown()
        {
            if (thread != null)
            {
                // Signal, then wait for the worker to run its own teardown so HAR is only
                // ever touched from that one thread.
                renderer.RequestStop();
                if (!thread.Join(WorkerJoinTimeoutMilliseconds))
                {
                    WyvrnHapticsLog.Warning("the haptic render worker did not stop in time; abandoning it.");
                }
                thread = null;
            }
            renderer = null;
        }

        public void SetEventName(string eventName)
        {
            using (MarkerSetEvent.Auto())
            {
                SetEventNameInternal(eventName);
            }
        }

        private void SetEventNameInternal(string eventName)
        {
            if (!hapticsEnabled)
            {
                // Switched off skips the event outright rather than playing it silently: no
                // queue traffic, no voice churn, no HAR synthesis for something nobody can
                // feel. Note this is the master switch only - a vibration gain of 0 still
                // plays the event, because it may carry a Stiffness track that arms a trigger.
                WyvrnHapticsLog.Trace(string.Format("Trace [Backend]: SetEventName('{0}') SKIPPED - haptics are off.", eventName));
                return;
            }

            WyvrnHapticsLog.Trace(string.Format("Trace [Backend]: SetEventName('{0}') -> forwarding to render worker.", eventName));

            if (renderer != null)
            {
                renderer.Enqueue(eventName);
            }
            else
            {
                WyvrnHapticsLog.Trace(string.Format("Trace [Backend]: SetEventName('{0}') DROPPED - no render worker.", eventName));
            }
        }

        public void SetHapticsEnabled(bool enabled)
        {
            hapticsEnabled = enabled;

            WyvrnHapticsLog.Trace(string.Format("Trace [Backend]: haptics -> {0}{1}.",
                enabled ? "ON" : "OFF", renderer != null ? string.Empty : " (cached; no render worker yet)"));

            if (renderer != null)
            {
                renderer.SetPendingHapticsEnabled(enabled);
            }
        }

        public void SetVibrationGain(int gain0To100)
        {
            // Clamped here as well as at the public API boundary, so the cache always holds
            // the value that will actually be applied whichever way the backend was reached.
            vibrationGain = WyvrnHapticGain.Clamp(gain0To100);

            WyvrnHapticsLog.Trace(string.Format("Trace [Backend]: vibration gain -> {0}/100{1}.",
                vibrationGain, renderer != null ? string.Empty : " (cached; no render worker yet)"));

            if (renderer != null)
            {
                renderer.SetPendingVibrationGain(vibrationGain);
            }
        }

        public void SetAdaptiveTriggersEnabled(bool enabled)
        {
            adaptiveTriggersEnabled = enabled;

            WyvrnHapticsLog.Trace(string.Format("Trace [Backend]: adaptive triggers -> {0}{1}.",
                enabled ? "ON" : "OFF", renderer != null ? string.Empty : " (cached; no render worker yet)"));

            if (renderer != null)
            {
                renderer.SetPendingTriggersEnabled(enabled);
            }
        }

        /// <summary>
        /// Owns the HAR runtime + voice pool and runs them on its own thread. The main
        /// thread only enqueues event names; this thread drains them and renders, so HAR is
        /// only ever touched here — single-threaded, hence no locking. Bring-up, loop and
        /// teardown all run on the worker.
        /// </summary>
        private sealed class Renderer
        {
            private readonly WyvrnRuntimeData data;
            private readonly ConcurrentQueue<string> eventQueue = new ConcurrentQueue<string>();
            private readonly AutoResetEvent wakeEvent = new AutoResetEvent(false);
            private readonly Stopwatch clock = new Stopwatch();

            private IInterhapticsRuntime runtime;
            private HapticVoicePool pool;

            private volatile bool ready;
            private volatile bool stopRequested;

            // Written by the main thread, consumed by the worker; last write wins, so
            // dragging a settings slider coalesces instead of queueing, and each control is
            // applied independently and idempotently - a torn read across the three just
            // converges on the next tick. Unlike Enqueue these are NOT dropped before the
            // worker is ready: bring-up applies whatever is pending once HAR is up.
            private volatile int pendingVibrationGain;
            private volatile bool pendingHapticsEnabled;
            private volatile bool pendingTriggersEnabled;

            // Worker only: what has actually been pushed. -1 forces the first gain apply
            // through (HAR comes up at 1.0 and must be re-asserted); the bools start at the
            // default-on state, which is exactly the state a freshly initialised HAR is in.
            private int appliedVibrationGain = -1;
            private bool appliedHapticsEnabled = true;
            private bool appliedTriggersEnabled = true;

            private double timeSeconds;
            private double lastSeconds;

            public Renderer(WyvrnRuntimeData runtimeData, bool hapticsEnabled, int gain0To100, bool triggersEnabled)
            {
                data = runtimeData;
                pendingHapticsEnabled = hapticsEnabled;
                pendingVibrationGain = gain0To100;
                pendingTriggersEnabled = triggersEnabled;
            }

            public bool IsReady
            {
                get { return ready; }
            }

            /// <summary>Main thread: hand an event to the worker (lock-free). Dropped until the worker is ready.</summary>
            public void Enqueue(string eventName)
            {
                if (ready)
                {
                    eventQueue.Enqueue(eventName);
                    WyvrnHapticsLog.Trace(string.Format("Trace [Enqueue]: '{0}' queued for render worker (main thread).", eventName));
                }
                else
                {
                    WyvrnHapticsLog.Trace(string.Format("Trace [Enqueue]: '{0}' DROPPED - render worker not ready yet.", eventName));
                }
            }

            public void SetPendingHapticsEnabled(bool enabled)
            {
                pendingHapticsEnabled = enabled;
                Wake();
            }

            public void SetPendingVibrationGain(int gain0To100)
            {
                pendingVibrationGain = gain0To100;
                Wake();
            }

            public void SetPendingTriggersEnabled(bool enabled)
            {
                pendingTriggersEnabled = enabled;
                Wake();
            }

            /// <summary>Main thread: ask the loop to exit, and wake it so it does so promptly.</summary>
            public void RequestStop()
            {
                stopRequested = true;
                Wake();
            }

            /// <summary>Thread entry point: bring HAR up, run the loop, tear HAR down. All on this thread.</summary>
            public void Run()
            {
                // Register with the profiler so this thread's markers are recorded instead
                // of silently dropped; it is how the HAR cost per frame becomes visible.
                Profiler.BeginThreadProfiling("WYVRN", "Haptic Renderer");
                try
                {
                    if (Initialize())
                    {
                        Loop();
                    }
                }
                catch (Exception exception)
                {
                    // An unhandled exception on a background thread would take the title down.
                    // Haptics are not worth that: report it and leave the pad silent.
                    WyvrnHapticsLog.Warning("the haptic render worker stopped on an exception: " + exception);
                }
                finally
                {
                    Teardown();
                    wakeEvent.Close();
                    Profiler.EndThreadProfiling();
                }
            }

            private bool Initialize()
            {
                runtime = new InterhapticsRuntime();
                if (!runtime.Initialize())
                {
                    // HAR PRX absent or failed to load: stay inert.
                    runtime = null;
                    WyvrnHapticsLog.Info("Interhaptics runtime unavailable; haptics inert.");
                    return false;
                }

                pool = new HapticVoicePool(runtime, VoicesPerEffect);
                WyvrnHapticsLog.Info(string.Format("preloading {0} command(s).", data.Commands.Count));
                pool.Preload(data);

                // Re-assert the controls: HAR's Quit() destroys the manager holding the global
                // intensity and Init() builds a fresh one back at 1.0, so without this the
                // vibration gain silently reverts to full on every backend bring-up.
                ApplyPendingSettings();

                clock.Start();
                lastSeconds = clock.Elapsed.TotalSeconds;
                ready = true;
                WyvrnHapticsLog.Info("Interhaptics render worker initialized.");
                return true;
            }

            private void Loop()
            {
                double renderAccumulator = 0.0;

                while (!stopRequested)
                {
                    // Stopwatch is the monotonic clock here: UnityEngine.Time is main-thread only.
                    double now = clock.Elapsed.TotalSeconds;
                    double delta = now - lastSeconds;
                    lastSeconds = now;
                    timeSeconds += delta;
                    renderAccumulator += delta;

                    // Pick up control changes from the main thread before anything renders.
                    ApplyPendingSettings();

                    // Drain queued main-thread triggers every tick (~120 Hz) so events dispatch promptly.
                    string eventName;
                    while (eventQueue.TryDequeue(out eventName))
                    {
                        WyvrnHapticsLog.Trace(string.Format("Trace [Worker]: received '{0}' from queue (worker thread).", eventName));
                        if (!appliedHapticsEnabled)
                        {
                            // The backend already refuses to enqueue while haptics are off; this
                            // catches events that were in flight when they were switched off.
                            WyvrnHapticsLog.Trace(string.Format("Trace [Worker]: '{0}' SKIPPED - haptics are off.", eventName));
                            continue;
                        }

                        WyvrnRuntimeCommand command = data.FindCommand(eventName);
                        if (command != null)
                        {
                            if (WyvrnHapticsLog.VerboseEnabled)
                            {
                                WyvrnHapticsLog.Trace(string.Format(
                                    "Trace [Worker]: '{0}' matched a command ({1} effect(s)); dispatching at t={2:F3}s.",
                                    eventName, command.Effects.Count, timeSeconds));
                            }
                            pool.PlayCommand(command, timeSeconds);
                        }
                        else
                        {
                            WyvrnHapticsLog.Trace(string.Format("Trace [Worker]: '{0}' matched NO command - unknown event, ignored.", eventName));
                        }
                    }

                    // Reclaim / arbitrate / render at HAR's native frame rate, time-gated so the
                    // rate holds despite sleep jitter.
                    if (renderAccumulator >= RenderIntervalSeconds)
                    {
                        renderAccumulator -= RenderIntervalSeconds;
                        if (renderAccumulator > RenderIntervalSeconds)
                        {
                            renderAccumulator = 0.0; // drop backlog after a hitch rather than burst-render
                        }

                        using (MarkerRenderLoop.Auto())
                        {
                            pool.Tick(timeSeconds);
                            using (MarkerHarRender.Auto())
                            {
                                runtime.Render(timeSeconds);
                            }
                        }
                    }

                    wakeEvent.WaitOne(QueueWaitMilliseconds);
                }
            }

            private void Teardown()
            {
                // Worker thread: tear HAR down here so it is single-threaded end to end.
                // Dispose the pool before the runtime: the pool holds a reference to the runtime
                // and releases the adaptive triggers through it.
                ready = false;
                if (pool != null)
                {
                    pool.Dispose();
                    pool = null;
                }
                if (runtime != null)
                {
                    runtime.Shutdown();
                    runtime = null;
                }
            }

            private void Wake()
            {
                // Apply now rather than at the end of the ~8 ms wait.
                try
                {
                    wakeEvent.Set();
                }
                catch (ObjectDisposedException)
                {
                    // The worker has already finished and closed the handle; nothing to wake.
                }
            }

            /// <summary>Worker thread only: push any changed control into HAR, skipping redundant calls.</summary>
            private void ApplyPendingSettings()
            {
                if (runtime == null)
                {
                    return;
                }

                int wantedGain = pendingVibrationGain;
                if (wantedGain != appliedVibrationGain)
                {
                    appliedVibrationGain = wantedGain;
                    float scalar = WyvrnHapticGain.ToScalar(wantedGain);
                    runtime.SetGlobalIntensity(scalar);
                    if (WyvrnHapticsLog.VerboseEnabled)
                    {
                        WyvrnHapticsLog.Trace(string.Format("Trace [Worker]: vibration gain {0}/100 (factor {1:F2}).", wantedGain, scalar));
                    }
                }

                if (pool == null)
                {
                    return;
                }

                // Triggers before the master switch: if both changed in the same tick, an
                // enable followed by a switch-off still ends with the pad released.
                bool wantedTriggers = pendingTriggersEnabled;
                if (wantedTriggers != appliedTriggersEnabled)
                {
                    appliedTriggersEnabled = wantedTriggers;
                    pool.SetAdaptiveTriggersEnabled(wantedTriggers);
                }

                bool wantedEnabled = pendingHapticsEnabled;
                if (wantedEnabled != appliedHapticsEnabled)
                {
                    appliedHapticsEnabled = wantedEnabled;
                    WyvrnHapticsLog.Trace("Trace [Worker]: haptics " + (wantedEnabled ? "ENABLED" : "DISABLED") + ".");
                    if (!wantedEnabled)
                    {
                        // Off is a hard off: stop every voice and release both triggers. New
                        // events stay skipped until haptics are switched back on.
                        pool.StopAll();
                    }
                }
            }
        }
    }
}

#endif // UNITY_PS5
