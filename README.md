# Windows Essentials for OpenDeck

Windows controls for OpenDeck and Stream Deck-compatible hardware, built for the AJAZZ AKP05E without accessing its HID protocol directly. OpenDeck remains responsible for the hardware; this plugin provides the actions and Windows integration.

## Features

- **Master Volume** — encoder rotation changes the Windows master volume and pressing the encoder toggles mute. The native Windows volume overlay is preserved.
- **Microphone Volume** — encoder rotation changes the default communications microphone level and pressing toggles mute.
- **App Volume** — choose an active application in the property inspector, then adjust or mute all of its current audio sessions.
- **Audio Output** — choose two output devices and toggle between them from a keypad button.
- **Audio Output Selector** — rotate through active output devices and press to make the displayed device the Windows default.
- **Media: Play / Pause**, **Media: Previous**, and **Media: Next** — Windows multimedia transport controls. Play/Pause reads the current global playback state when it appears and after each press.
- **Power Action** — choose Lock PC, Sleep PC, Restart PC, or Shut Down PC. Restart and shutdown require a second press within three seconds; the confirmation state uses a dedicated warning icon.

All property inspectors and user-facing manifest text are in English. The plugin keeps a small native Windows audio helper running while OpenDeck is active and rebuilds it automatically during packaging.

## Requirements

- Windows 10 or later (x64)
- OpenDeck with the Stream Deck plugin protocol enabled
- Node.js 20 or later on `PATH` when running the plugin from source
- .NET SDK 10 when building the bundled helper

## Install a release package

1. Build or download `Windows-Essentials-0.15.1.streamDeckPlugin`.
2. In OpenDeck, open the plugin installer and select the package.
3. Add actions from **Windows Essentials** to the AKP05E layout.
4. Open an action's property inspector when it has configurable options.

For local development, OpenDeck can also load the unpacked folder `net.parrec.deck.windows-essentials.sdPlugin` if its plugin settings allow it.

## Build and test from source

```powershell
npm test
npm run package
```

`npm run package` publishes the C# helper and creates the installer under `release/`. Generated binaries and release packages are intentionally ignored by Git.

## Project layout

```text
net.parrec.deck.windows-essentials.sdPlugin/
  bin/plugin.cjs             Stream Deck/OpenDeck protocol and action logic
  imgs/                       Action and state SVG icons
  propertyInspector/         OpenDeck configuration panels
  manifest.json               Plugin metadata and action declarations
audio-helper/                 C# helper source and project file
scripts/package.ps1           Windows package builder
test/                         Node.js tests for manifest and action behavior
```

The Node plugin deliberately uses the protocol layer instead of implementing the AKP05E HID protocol. This keeps the project compatible with OpenDeck's device abstraction and with other supported Stream Deck-style hardware.

## Safety notes

Lock and sleep execute immediately. Restart and Shut Down are protected by a two-step confirmation: press once, then press again within three seconds. If the second press does not arrive, the action returns to its normal icon automatically.

The audio actions operate on the devices and sessions exposed by Windows. If Windows reports no active microphone, application session, or output device, the corresponding action remains available but cannot change that unavailable resource.

## Development status

Version 0.15.1 is the first polished v0 release candidate for everyday use. The next major work is visual refinement, broader device testing, and preparing a public GitHub repository and OpenDeck marketplace submission. A separate Codex integration can be added later without changing the reliable system-volume path.

## License

This project is licensed under the [MIT License](LICENSE). It is provided as-is, without warranty; Windows, OpenDeck, device firmware, audio drivers, and third-party dependencies may behave differently across systems.
