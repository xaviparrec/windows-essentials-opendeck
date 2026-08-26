# Display Control requirements

## Goal

Add a configurable `Display Control` action to Windows Essentials. The action must let a user select one Windows display and toggle whether that display participates in the desktop, without opening Windows Settings each time.

## Phase 1 scope

- Add one keypad action named `Display Control` under **Windows Essentials**.
- Configure one target display in the property inspector.
- Show each display using its Windows-friendly name and a stable identifier, for example:
  - `Display 1 — Dell U2723QE`
  - `Display 2 — LG UltraGear`
- Allow an optional custom label such as `Main`, `Left`, or `TV`.
- Pressing the action toggles the selected display:
  - active display → disable it;
  - disabled display → enable it.
- On `willAppear`, query the real display state and show the correct icon/title.
- Persist the stable display identifier and custom label through OpenDeck settings.
- If the selected display is temporarily unavailable, show a clear unavailable state and keep the configuration intact.

## Phase 2 scope

- Add an operation selector to the same action:
  - `Toggle display`
  - `Set as primary display`
- `Set as primary display` must preserve the other connected displays and their layout where Windows permits it.
- The action must show which configured display is currently primary.

## Technical constraints

- Keep all Windows display enumeration and changes in the existing C# helper.
- Use stable Windows display identity (device path/EDID-derived identity), not only the display number.
- Use Windows Display Configuration APIs (`QueryDisplayConfig` / `SetDisplayConfig`) rather than shelling out to an external utility.
- Do not implement direct AJAZZ HID handling; OpenDeck remains responsible for the device.
- Keep the property inspector and manifest text in English.

## Acceptance criteria

- A user with three displays can configure two separate buttons for two separate monitors.
- Pressing either button toggles only its configured monitor.
- The icon is correct after OpenDeck starts and after an external display change.
- A monitor being disconnected and reconnected does not silently erase its configuration.
- Tests cover manifest declarations, persisted settings shape, and action state handling.
- The feature is tested on the project's baseline setup: Windows with an AJAZZ AKP05E and three displays.

## Out of scope for the first implementation

- Brightness control.
- Per-monitor refresh-rate or resolution changes.
- Display profiles and multi-monitor presets.
- Automatic screen selection based on the foreground application.
