# Changelog

## [0.16.1] - 2026-08-26

### Fixed

- Play/Pause now polls the real Windows playback state while visible, so external Spotify or media-key changes update the icon.
- App Volume recovers the current process ID from the saved application name after an application or OpenDeck restart.

## [0.16.0] - 2026-08-25

### Changed

- Refined the first icon group with a consistent white-and-accent visual system.
- Added cyan audio accents, green media accents, and amber power accents while keeping icons readable at AKP05E size.

## [0.15.1] - 2026-08-25

### Added

- Complete English release documentation and development instructions.
- Explicit safety documentation for restart and shutdown confirmation.

### Changed

- Translated remaining manifest descriptions and tooltips to English.
- Removed the placeholder GitHub URL from the manifest until the public repository exists.
- Updated the package and installer version to 0.15.1.

## [0.15.0]

- Added the configurable Power Action with Lock, Sleep, Restart and Shut Down.
- Added double-press confirmation for restart and shutdown.
- Stabilized application volume and audio-output selection workflows.
