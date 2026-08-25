# Contributing

## Local setup

Use Windows 10 or later with Node.js 20+ and the .NET 10 SDK installed. Clone the repository, then run:

```powershell
npm test
npm run package
```

The package command publishes the native audio helper into the plugin folder and writes a `.streamDeckPlugin` installer to `release/`.

## Development workflow

1. Make a focused change in the Node plugin, helper, manifest, or property inspector.
2. Run `npm test`.
3. Run `npm run package` when the manifest or runtime code changes.
4. Install the generated package in OpenDeck and test the action on the AKP05E.
5. Commit the source change; do not commit generated helper binaries or release packages.

The plugin talks to OpenDeck through the Stream Deck WebSocket protocol. Do not add direct AKP05E HID handling unless the project scope changes explicitly.

## Pull requests

Describe the Windows version, OpenDeck version, hardware tested, and any limitations. For actions that affect power or audio routing, include the safety behavior and the expected fallback when Windows reports no matching device or session.
