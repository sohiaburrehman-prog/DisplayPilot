# Changelog

All notable changes to DisplayPilot are documented here.

## [1.7.4] — 2026-07-01

### Fixed
- **Stale tray notification after swap-back** — primary swap and set-primary tray balloons now replace any visible balloon instead of being suppressed by the 4-second throttle, so swap-back shows the correct current primary monitor

## [1.7.3] — 2026-07-01

### Fixed
- **Monitor card clicks on 4K/high-DPI** — single- and double-click on monitor cards and the arrangement map now reliably set a display as primary; inline Rename links no longer steal clicks from the card button
- **Tray context menu text on 4K** — menu fonts scale with the monitor DPI (PerMonitorV2) so all items, including action entries and section labels, render at a readable size on 150%/200% scaling

## [1.7.2] — 2026-07-01

### Fixed
- **Swap button on 4K displays** — swap label now uses explicit 13.5sp typography (matching other controls), wraps instead of ellipsizing in a fixed width, and shows a concise "Swap 1 ↔ 2" caption with full monitor names in the tooltip

## [1.7.1] — 2026-06-30

### Fixed
- **Profile manager empty state** — "No profiles yet" message and list content now appear when the editor is closed; `IsEditing` no longer treated the hidden editor control as active
- **Add profile** — editor panel moved outside the profiles scroll area so "+ Add profile" shows the inline editor instead of collapsing the entire tab content

## [1.7.0] — 2026-06-30

### Added
- **Layout presets** — save primary monitor plus per-display resolution/refresh; apply, rename, or delete presets from the profile manager
- **Profile manager UX** — search/filter profiles; duplicate profile; "Last triggered" timestamp per profile
- **Smarter game detection feedback** — tray tooltip shows the active matched profile; "Active now" badge on profile rows
- **Tray quick wins** — "Re-apply last profile" menu item remembers the last manual or auto-swap profile
- **Interactive Help** — `--interactive-help` CLI for exploring help topics from the terminal
- **Portable zip on every release** — GitHub Actions workflow builds `DisplayPilot-Portable.zip` alongside `DisplayPilot-Setup.exe`
- **winget manifest** — `packaging/winget/` updated for v1.7.0 submission

### Changed
- **Process watcher** — caches grouped process lists; exposes current active profile for tray and UI
- **README screenshots** — improved dark-theme placeholder mockups with capture checklist
- Settings schema bumped to v4 (`LayoutPresets`, `LastUsedProfileId`, `LastTriggeredUtc`)

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
