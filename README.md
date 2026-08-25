# Windows Essentials for OpenDeck

Minimal plugin for Windows that uses the Stream Deck plugin protocol implemented by OpenDeck. It does **not** access the AJAZZ AKP05E directly: OpenDeck and its device plugin handle the hardware.

## Current actions

Add **Windows Essentials → Master Volume** to an encoder.

- Rotate clockwise: raises the Windows master volume.
- Rotate counter-clockwise: lowers it.
- Press: toggles Windows mute.

The plugin sends Windows multimedia keys through the built-in `user32.dll` API. It needs no external audio utility or driver.

- **Media: Play / Pause**, **Previous** and **Next**: basic multimedia transport.
- **Lock PC**: locks the current Windows session.
- **Microphone Volume**: an encoder action for the default microphone. Rotate it to adjust the level; press it to mute or unmute.

The Play / Pause button asks Windows for the actual playback state when it appears, and again after a press. Its icon therefore starts correctly even when music was already playing.

## Verify and install

From this folder:

```powershell
npm test
npm run package
```

Install `release/Windows-Essentials-0.8.0.streamDeckPlugin` from OpenDeck's plugin installer. If OpenDeck lets you select an unpacked plugin folder, `net.parrec.deck.windows-essentials.sdPlugin` is also the development folder.

OpenDeck must be able to find Node.js 20 or later on your `PATH`; this computer currently has Node 24.

`npm run package` also compiles the bundled Windows audio helper, so a fresh clone can build the installer without committed binary artifacts.

## Current scope and next additions

The plugin keeps its native Windows audio helper running while OpenDeck is active. It triggers Windows' own multimedia volume keys so the normal on-screen overlay appears, then waits only until Windows has applied that exact change before updating the deck. This avoids both process-start delay and a one-tick visual offset.

Planned actions share the same protocol and can be added beside the existing ones:

1. microphone volume/mute;
2. app and output-device controls;
3. a separate `Codex` action family, backed by a small local bridge to the Codex desktop app. Keeping that bridge separate means Codex integration cannot affect the reliable system-volume action.
