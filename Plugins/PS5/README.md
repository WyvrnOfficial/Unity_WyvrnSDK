# Interhaptics PS5 runtime libraries

The PS5 haptics path calls the Interhaptics **HAR** engine and its **DualSense
provider** through P/Invoke. Those binaries are licensed separately from this SDK
and are therefore **not committed** to this repository (see `.gitignore`).

## What to put here

Copy these two files out of the Interhaptics Engine SDK for PS5
(`InterhapticsEngine_PS5/bin/SDK<version>/`) into this folder:

| File | From | Purpose |
|------|------|---------|
| `HAR.prx` | `bin/SDK<version>/HapticEngine/` | Haptic Audio Renderer: parses `.haps`, synthesises vibration, evaluates the Stiffness envelope. |
| `Provider_DualSensePS5.prx` | `bin/SDK<version>/HapticProviders/` | DualSense output: palm actuators and the L2/R2 adaptive triggers. |

Then, in the Unity Inspector, select each `.prx` and tick **PS5** under
*Platform settings* so Unity stages it into the player.

Pick the `SDK<version>` folder matching the **PS5 SDK your Unity PS5 module is
built against** — mixing vintages is the usual cause of a load failure.

Only the `.prx` are needed. The `_stub.a` / `_stub_weak.a` import libraries
alongside them are for link-time binding, which is how the *Unreal* plugin
consumes HAR; Unity does not need them.

This was verified against a real PS5 player build (Unity 6000.3.17f1, Prospero
SDK 12.000). IL2CPP compiles each `[DllImport]` into a **runtime** lookup —

```cpp
il2cpp_codegen_resolve_pinvoke<PInvokeFunc>(IL2CPP_NATIVE_STRING("HAR"), "AddHM", ...)
```

— so nothing is resolved at link time and no stub library is involved. The build
succeeds with the PRX absent, and both are staged to `Build/Media/Plugins/` when
present. Two consequences worth knowing:

- The library name in `InterhapticsRuntime.cs` must match the **staged file name**,
  which is why the `lib` prefix below matters.
- A missing or misnamed library surfaces as a `DllNotFoundException` on the first
  call, which is exactly the failure the runtime catches to stay inert.

### A note on the `lib` prefix

The Interhaptics SDK changed the file naming between vintages:

| SDK | Engine | Provider |
|-----|--------|----------|
| 7, 8, 9 | `libHAR.prx` | `libProvider_DualSensePS5.prx` |
| 11, 12 | `HAR.prx` | `Provider_DualSensePS5.prx` |

`InterhapticsRuntime.cs` imports them as `"HAR"` and
`"Provider_DualSensePS5"`, matching the newer naming. If you are on an SDK 7–9
vintage, either **drop the `lib` prefix when copying the files in**, or change the
two `const string` library names at the top of `InterhapticsRuntime.cs` — they are
declared in one place precisely so this is a one-line change.

## If they are missing

Nothing breaks. `InterhapticsRuntime.Initialize()` catches the
`DllNotFoundException` from the first entry-point call, reports it once, and the
runtime stays inert: `WyvrnAPI.CoreInitSDK` still succeeds, `IsInitialized()`
returns `false`, and events are dropped silently. A title therefore builds, boots
and plays on PS5 without them — just with no haptics. The same applies if the
library name does not resolve, so check the Unity log for that warning before
assuming a deeper problem.

## Adaptive triggers specifically

`startTriggerEffect` / `stopTriggerEffect` are newer than the rest of the
provider surface. If the provider you drop in predates them, the runtime detects
it at bring-up with a single probe call, logs a warning, and disables *only* the
adaptive triggers — vibration keeps working. (Both symbols are in fact exported by
every provider in SDK 7 through 12, so this is insurance rather than an expected
path.)

## API surface

The entry points this SDK binds are declared in `InterhapticsEngine.h` and
`SharedTypes.h` in the Interhaptics SDK's `include/` folder, with one exception:
`GetVibrationLength` is exported by `HAR.prx` but is not in the public header (it
lives in `InterhapticsEngineInternal.h` in the HAR source tree). The voice pool
uses it to know when a one-shot's playback window has elapsed.
