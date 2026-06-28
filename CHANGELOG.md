# Changelog

All notable changes to DisplayPilot are documented here.

## [1.6.4] — 2026-06-28

### Security
- **Activity log folder** — opening the log in Explorer validates paths stay under `%LOCALAPPDATA%\DisplayPilot\` before invoking `explorer.exe`, blocking argument injection and shell execution of untrusted locations

## [1.6.2] — 2026-06-28

### Fixed
- **Profile manager** — opening from Settings no longer fails silently; profile rows resolve theme resources correctly instead of throwing on `FontFamily`
- **Missing profiles** — profiles were still on disk but invisible when the manager crashed; backup restore from `settings.json.bak` runs automatically when the live file has no profiles
- **Settings UI** — hairline separator added below "Run setup wizard again" in Backup & restore

## [1.6.1] — 2026-06-28

### Fixed
- **Panel crash** — opening the panel on the Advanced tab no longer throws "Collection was modified during enumeration" when resolution/refresh combos populate

## [1.6.0] — 2026-06-28

### Added
- **Profile manager** — dedicated Auto-swap profiles window for viewing, adding, editing, and deleting game profiles
- **Shared profile editor** — reusable editor control with process picker, launcher target, monitor combo, restore-on-exit, and test profile

### Changed
- **Settings** — profile list and editor removed; single "Open profile manager" link remains
- **Navigation** — Panel Advanced tab "Manage ›" and tray "Manage game profiles…" open the profile manager instead of Settings

## [1.5.4] — 2026-06-28

### Fixed
- **Settings profile editor** — editor no longer scrolls inside the profiles list; list hides while editing and the window grows so Backup & restore and Updates stay visible
- **Monitor dropdown** — profile editor shows monitor nickname or hardware name instead of the internal type name

## [1.5.3] — 2026-06-26

### Fixed
- **Settings window** — Backup & restore and Updates sections no longer clip behind the Close footer; profiles list scrolls only when it overflows

## [1.5.2] — 2026-06-26

### Fixed
- **What's new banner** — reading or dismissing release notes for the current version now persists across panel opens and app restarts; banner only reappears after upgrading to a newer version
- **Update banner** — clicking "What's new" on the update banner now dismisses it the same way as the close button

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
