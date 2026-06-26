# Changelog

All notable changes to DisplayPilot are documented here.

## [1.5.1] — 2026-06-26

### Fixed
- **Settings window** — removed unnecessary scrollbar in the default state; only the profiles list/editor area scrolls when content overflows

## [1.5.0] — 2026-06-26

### Added
- **CLI** — headless commands: `--list-monitors`, `--set-primary`, `--export-settings`, `--import-settings`, `--help`
- **Tray quick actions** — QUICK ACTIONS section with swap (dual monitor), cycle primary, and apply-profile submenu
- **Smarter launcher matching** — watches for game processes spawned after Steam/Epic/etc.; “Suggest running games” in profile editor
- **What's new banner** — dismissible release notes after upgrade; update banner links to changelog
- **winget manifest** — local package under `packaging/winget/` for community submission
- **README screenshots** — placeholder images and capture guide in `docs/screenshots/`

### Changed
- Launcher profiles with a resolved game exe no longer activate on the launcher alone — they wait for the game process
- Settings schema bumped to v3 (`LastSeenVersion`, `MatchLauncherChildren`)

## [1.4.1]

Maintenance and stability release.

## [1.4.0]

Resolution/refresh switching, monitor nicknames, setup wizard improvements.

## [1.3.0]

Hotkeys, auto-swap profiles, activity log, optional update check.
