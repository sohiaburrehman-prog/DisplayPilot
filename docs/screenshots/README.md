# DisplayPilot screenshots

Placeholder PNGs ship in-repo so the GitHub README renders correctly. **Replace them with real captures before a marketing release.**

## Capture checklist

Use this checklist for every release that updates README screenshots:

- [ ] **Dark theme** — app uses the built-in dark theme; match your desktop wallpaper for a clean crop
- [ ] **Consistent monitor layout** — same number of monitors and nicknames across all four shots
- [ ] **No personal data** — blur or avoid email, file paths, or game titles you do not want public
- [ ] **Tight crop** — window only, ~1200–1600 px wide; PNG format
- [ ] **Exact filenames** — overwrite files in this folder (case-sensitive on GitHub)

## How to capture (Windows)

1. Open DisplayPilot and navigate to the screen you want.
2. Press **Win+Shift+S** (Snipping Tool region capture).
3. Draw a rectangle around the window or panel.
4. Click the preview notification → **Save as** using the filename from the table below.
5. Save into `docs/screenshots/` (this folder).
6. Open `README.md` in preview to confirm images render.
7. Commit and push.

## Required files

| Filename | What to capture |
| --- | --- |
| `panel-displays.png` | Flyout panel — **Displays** tab: monitor cards, arrangement map, resolution dropdowns |
| `panel-advanced.png` | Flyout panel — **Advanced** tab: hotkeys, profile summary, log link |
| `settings-profiles.png` | **Profile manager** window — profile list with search, duplicate, and optional "Active now" badge |
| `wizard.png` | First-run setup wizard on monitor selection or finish step |

## Regenerate placeholders

If you only need better placeholders (no live UI):

```powershell
powershell -ExecutionPolicy Bypass -File tools\CreatePlaceholders.ps1
```

Placeholders are labeled **PLACEHOLDER** in the image. Swap them for real captures when you can run DisplayPilot on a multi-monitor setup.
