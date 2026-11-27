# Changelog

All notable changes to the Razer WYVRN SDK for Unity are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

Versions match `package.json` (`com.razer.wyvrnsdk`), and each release is tagged `vX.Y.Z` in git, so a UPM git reference can pin it (for example `#v2.1.0`).

## [2.0.22] - 2025-06-27

First documented release of the WYVRN SDK for Unity on Windows.

### Included

- `WyvrnAPI.CoreInitSDK`, `CoreSetEventName` and `CoreUnInit`: name game events, and the external WYVRN configuration in Razer Synapse plays the AI, Chroma RGB and Sensa HD haptics for them.
- No dependency on a separate WyvrnSDK DLL: the SDK uses the Chroma runtime installed with Razer Synapse (`RzChromatic`), and checks its file version on any Windows language.
- Windows 32-bit and 64-bit.
- Sample scene (**Assets → Razer Wyvrn SDK - Setup Sample Scenes**) and the `Game Sample Application` haptic folder.
- WYVRN Design Template and documentation links in the README.

Earlier versions 2.0.12, 2.0.20 and 2.0.21 are also tagged in git (`v2.0.12`, `v2.0.20`, `v2.0.21`) but have no entries here.
