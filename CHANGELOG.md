# Changelog

All notable changes to DisplayPilot are documented here.

## [Unreleased]

### Added
- **Transactional display scenes** — capture primary display, resolution, refresh, desktop position, rotation, and HDR for every active monitor; preflight exact changes before applying and automatically roll back the complete scene when any step fails
- **Scene preview and scripting** — preview scenes in the profile manager or with `--preview-scene`; `--list-scenes`, `--apply-scene`, and `--dry-run` provide JSON-compatible automation while the old preset commands remain aliases
- **Richer profile matching** — optionally constrain a profile to an exact executable path and/or a case-insensitive main-window-title substring
- **Profile diagnostics** — “Explain current state” and `--explain-profiles` show why every profile matched or failed, which conflict rank it holds, why the winner controls the display, and which profile will take over next
- **Scene safety confirmation** — interactive scene changes show a 15-second “Keep these changes?” countdown and restore the prior complete display state on timeout or explicit revert
- **Scene-aware profiles** — profiles can apply saved scenes, stack by the existing conflict policy, and restore the complete pre-automation scene after the stack exits
- **Complete scene CLI** — capture, rename, delete, export, and import individual scenes; use `--temporary <seconds>` to apply a scene and restore automatically

### Changed
- Settings schema upgraded to v7. Existing mode-only layout presets remain importable and apply without changing their live position, rotation, or HDR state
- Process polling only reads executable paths and window titles for profiles that use advanced constraints; full process inspection occurs on demand for diagnostics

## [1.7.16] — 2026-07-12

### Changed
- **Settings redesign** — reorganized the window into General, Shortcuts, Automation, and Data & support tabs; added Windows startup control and height clamping for smaller work areas
- **Profile manager clarity** — named priority levels replace unexplained numeric input, and simultaneously matched profiles now distinguish the controlling winner from profiles waiting underneath it
- **Display preset reliability** — preset capture/apply operations run without freezing the window, block accidental double execution, and describe their resolution/refresh scope accurately
- **Keyboard and accessibility polish** — added dialog ownership, default/cancel actions, visible keyboard focus, automation labels, and live status announcements across settings, profiles, and monitor controls

### Fixed
- Hotkey changes are validated before saving, conflicting or unavailable bindings show directly in Settings, and failed registrations roll back cleanly
- Profile search no longer performs an expensive monitor enumeration on every keystroke
- Manual update checks no longer open the browser automatically, and failed checks no longer suppress the next scheduled retry
- Settings and profile windows now remain usable on shorter displays instead of expanding beyond the work area

## [1.7.15] — 2026-07-10

### Added
- **HDR toggle per monitor** — an HDR checkbox on each monitor card in the flyout's Advanced tab (shown only when the display reports HDR support). Uses the Windows 11 24H2 `SET_HDR_STATE` DisplayConfig API with automatic fallback to the legacy advanced-color toggle on older builds (Win10 1709+)
- **HDR via CLI** — `--set-hdr <monitor> on|off` (0-based index, friendly name, or device name); `--list-monitors` now includes `HdrSupported` / `HdrEnabled` per display
- **Profile conflict rules** — profiles now have a numeric priority, with selectable highest-priority or most-recently-activated winner policies and stack-safe restoration when overlapping apps exit
- **Expanded scripting CLI** — list and apply profiles/layout presets by name or stable ID, change projection mode, and add `--json` to any command for a consistent success/error envelope

### Fixed
- Launcher child detection now verifies WMI PID ancestry instead of selecting the first unrelated process that appears, and detected games remain active after their launcher exits
- Disabling or deleting an active winning profile now promotes the next match or restores the original session primary
- Layout presets preflight all modes and roll back earlier changes when an apply fails
- Settings imports no longer report success when the settings file could not be written
- Single-instance synchronization uses one Windows session scope, and installer/winget metadata now matches v1.7.15

## [1.7.14] — 2026-07-09

### Added
- **Window rescue after auto-swap** — games that pick their display during engine init (before the primary swap lands, e.g. 007 First Light) are now handled: after a profile swap, DisplayPilot waits for the game's main window to appear and settle, then moves it onto the target monitor automatically. Borderless/fullscreen windows cover the target exactly; windowed apps are centered; maximized windows re-maximize on the target. One retry a few seconds later covers titles that re-assert their display. Per-profile toggle in the profile editor (on by default); manual "Apply profile" from the tray also rescues an already-running game's window

## [1.7.13] — 2026-07-05

### Fixed
- **Arrangement map scrollbar (dual monitor)** — two-display setups now scale to fit the flyout map width instead of height-only scaling (~349 px tile span vs ~326 px viewport on typical 1920×1080 pairs). Horizontal scroll stays enabled only for three or more monitors when the desktop exceeds the viewport

## [1.7.12] — 2026-07-05

### Fixed
- **Arrangement map scrollbar** — layout now measures actual tile bounds (including insets) instead of the scaled desktop span alone; when content fits the flyout map viewport within a 2 px tolerance, tiles are centered and the horizontal scrollbar stays hidden. Fixes an unnecessary left-to-right scrollbar on multi-monitor setups (e.g. 4K + ultrawide + edge display) introduced in v1.7.5

## [1.7.11] — 2026-07-05

### Performance
- **GetMonitors** — merged friendly-name and bounds DisplayConfig lookups into `GetDisplayConfigDetails` (PR #18); halves expensive `QueryActiveConfig` Win32 calls and reduces micro-stutters when monitor enumeration runs on a timer

## [1.7.10] — 2026-07-05

### Fixed
- **Settings window height** — `FitHeightToContent` now adds non-client window chrome (title bar + frame) to the measured inner layout; sizing runs on `ContentRendered` so header/footer/content heights are final. Eliminates the ~20–40 px scrollbar and clipped “Check now” button on 4K at 100%/150% DPI

## [1.7.9] — 2026-07-04

### Performance
- **Process watcher** — skip redundant `GetMonitors()` when the active profile is unchanged (PR #17); WMI push detection on process creation triggers an immediate poll so auto-swap matches games within ~1 s instead of waiting for the next timer tick

### Fixed
- **Settings window height** — default Height 840 / MinHeight 720; window auto-sizes to measured content on open so HOTKEYS through UPDATES and Close fit without a scrollbar at 100% and 150% DPI; ScrollViewer only when manually resized smaller

## [1.7.8] — 2026-07-02

### Fixed
- **Settings window height** — default size raised further (700→720, MinHeight 560→680) so all sections fit without scrolling at 100% DPI on 1080p+; ScrollViewer remains for smaller screens

## [1.7.7] — 2026-07-02

### Fixed
- **Settings window** — default height raised (520→700) and minimum height raised (420→560) so HOTKEYS through UPDATES and Close are visible without manual resize; middle content scrolls on smaller screens

## [1.7.6] — 2026-07-02

### Added
- **Theme preference** — System (follows Windows light/dark and accent), Dark, or Light; live palette updates from Settings
- **Win+P projection modes** — PC screen only, Duplicate, Extend, and Second screen only from the tray context menu
- **Panel polish** — swap animation on the two-monitor arrangement map; primary tile pulse after set-primary or swap
- **First-run tray hint** — one-time balloon after setup pointing users to the tray icon and hotkey

## [1.7.5] — 2026-07-01

### Fixed
- **Arrangement map on mixed-DPI / ultrawide setups** — monitor bounds now come from DisplayConfig (Windows' source of truth for virtual-desktop topology) instead of DEVMODE alone; the map uses explicit content sizing inside a horizontal scroll viewer so displays to the left or right of the primary (e.g. 4K beside an ultrawide) stay reachable via mouse wheel or the scrollbar instead of being clipped at the wrong edge

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
