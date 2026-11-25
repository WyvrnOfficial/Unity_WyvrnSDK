# Interhaptics PS5 runtime libraries

The PS5 haptics path calls the Interhaptics **HAR** engine and its **DualSense
provider** through P/Invoke. Those binaries are licensed separately from this SDK
and are therefore **not committed** to this repository (see `.gitignore`).

## What to put here

Drop these two files from your Interhaptics HAR build into this folder:

| File | Purpose |
|------|---------|
| `HAR.prx` | Haptic Audio Renderer: parses `.haps`, synthesises vibration, evaluates the Stiffness envelope. |
| `Provider_DualSensePS5.prx` | DualSense output: palm actuators and the L2/R2 adaptive triggers. |

Then, in the Unity Inspector, select each `.prx` and tick **PS5** under
*Platform settings* so Unity stages it into the player.

## If they are missing

Nothing breaks. `InterhapticsRuntime.Initialize()` catches the
`DllNotFoundException` from the first entry-point call, reports it once, and the
runtime stays inert: `WyvrnAPI.CoreInitSDK` still succeeds, `IsInitialized()`
returns `false`, and events are dropped silently. A title therefore builds, boots
and plays on PS5 without them — just with no haptics.

## Adaptive triggers specifically

`startTriggerEffect` / `stopTriggerEffect` are newer than the rest of the
provider surface. If the `Provider_DualSensePS5.prx` you drop in predates them,
the runtime detects it at bring-up (one probe call), logs a warning, and disables
*only* the adaptive triggers — vibration keeps working.
