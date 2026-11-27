# Changelog

All notable changes to the Razer WYVRN SDK for Unity are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

Versions match `package.json` (`com.razer.wyvrnsdk`), and each release is tagged `vX.Y.Z` in git, so a UPM git reference can pin it (for example `#v2.1.0`).

## [2.1.0] - 2026-11-26

### Added

- **PS5 haptics (DualSense).** On PS5, `CoreInitSDK`, `CoreSetEventName` and `CoreUnInit` now drive the Interhaptics HAR engine and DualSense provider instead of the Chroma SDK. WYVRN events play as vibration on the left and right sides and as adaptive-trigger resistance. Game code is the same as on PC.
- Haptic voice pool that follows the WYVRN rules for priority, `Merge` / `Override` mixing, looping, named interrupts (`Interrupts_Commands`), `Global` / `Left` / `Right` spatialization and the `Hand` target.
- Threaded render worker ("WYVRN → Haptic Renderer" in the Profiler), with profiler markers.
- Player settings for a game's options menu: `SetHapticsEnabled` / `GetHapticsEnabled`, `SetVibrationGain` / `GetVibrationGain` (0–100, vibration only) and `SetAdaptiveTriggersEnabled` / `GetAdaptiveTriggersEnabled`. They return `RZRESULT_NOT_SUPPORTED` on other platforms and can be called before `CoreInitSDK`.
- `WyvrnAPI.IsInitialized()`: on PS5, `true` once haptics are live.
- Editor importer: **Tools → WYVRN → Import WYVRN Haptics…** reads `WYVRN.config` and the `.haps` files and bakes them into `Assets/Resources/Wyvrn` (`WyvrnHapticData` plus one `WyvrnHapticEffect` per effect). It detects trigger-resistance (Stiffness) tracks.
- Headless import for build machines: `-executeMethod WyvrnSDK.Haptics.EditorTools.WyvrnHapticsImportMenu.ImportFromCommandLine -wyvrnHapticFolder <folder>` (use with `-quit`).
- **Project Settings → WYVRN → Haptics Import** for the haptic folder root and app folder.
- Build-time check that warns when building for PS5 with no imported haptics, and logs how many commands were included.
- `Tests/GameSampleHapticsDebug.cs`: a step-by-step devkit harness for the `Debug_*` events in the sample `WYVRN.config`.
- Sample haptics in `HapticFolders/Game Sample Application`: `Effect1`–`Effect16` `.haps` files and a `WYVRN.config` that adds the `Debug_*` test events.
- 48 EditMode tests (voice pool, config parser, `.haps` reader, controls, gain, import settings, JSON, runtime data) in a new `Tests/Editor` assembly.
- `Plugins/PS5/README.md` and `.gitignore`: where the PS5 libraries come from, and a rule that keeps `.prx` / `.a` binaries out of the repository.

### Changed

- The Windows path is unchanged. On PS5, Chroma and AI calls are compiled out and only the haptics part of an event plays.
- `README.md`: new PS5 section with setup, the 3-step integration and the PS5 library placement (`Assets/Plugins/PS5` with PS5-only `.prx.meta`).
- `package.json`: version 2.1.0, PS5 / DualSense keywords and description.

### Requirements

- PS5 needs `HAR.prx` and `Provider_DualSensePS5.prx` from InterhapticsEngine_PS5 v1.4.3 or later (the `bin/SDK<version>` folder that matches your PS5 SDK). They are licensed separately and not included. Without them the PS5 build runs, with haptics switched off.

### Known issues

- Events sent before the haptic engine has finished loading (right after `CoreInitSDK`) are dropped.
- With PS5 as the active build target, the editor uses the PS5 path, so Play mode has no Chroma and no haptics.
- Baked haptic assets use different field names from earlier internal builds, so re-import after updating.

## [2.0.22] - 2025-06-27

First documented release of the WYVRN SDK for Unity on Windows.

### Included

- `WyvrnAPI.CoreInitSDK`, `CoreSetEventName` and `CoreUnInit`: name game events, and the external WYVRN configuration in Razer Synapse plays the AI, Chroma RGB and Sensa HD haptics for them.
- No dependency on a separate WyvrnSDK DLL: the SDK uses the Chroma runtime installed with Razer Synapse (`RzChromatic`), and checks its file version on any Windows language.
- Windows 32-bit and 64-bit.
- Sample scene (**Assets → Razer Wyvrn SDK - Setup Sample Scenes**) and the `Game Sample Application` haptic folder.
- WYVRN Design Template and documentation links in the README.
Earlier versions 2.0.12, 2.0.20 and 2.0.21 are also tagged in git (`v2.0.12`, `v2.0.20`, `v2.0.21`) but have no entries here.
