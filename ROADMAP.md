# DisplayPilot roadmap

This roadmap is directional. Items move into a release only after design, safety, and hardware testing.

## 1.8 — Safe scenes and scene-aware automation

- [x] Confirm risky interactive scene changes with a countdown and automatically restore on timeout
- [x] Let profiles apply a complete saved scene instead of changing only the primary display
- [x] Preserve and restore the full pre-automation scene when the active profile stack exits
- [x] Capture, rename, delete, export, import, preview, and temporarily apply scenes from the CLI
- [x] Keep CLI JSON envelopes consistent for scripting
- [x] Complete mixed-resolution, mixed-refresh, orientation, HDR, missing-display, and failed-apply hardware verification

## 1.9 — Window layouts

- Save window positions and sizes alongside a display scene
- Restore apps by executable, window title, or app identity after displays settle
- Offer per-app exclusions and a preview showing which windows will move
- Handle minimized, maximized, borderless, virtual-desktop, and elevated windows safely

## 2.0 — Rules and triggers

- Compose triggers for process, time, power source, dock/device arrival, network, and foreground app
- Add conditions, delays, cooldowns, and explicit rule ordering
- Simulate a rule against current state and explain why it would or would not run
- Keep rule execution transactional with audit history and one-click rollback

## Later

- DDC/CI brightness, contrast, input-source, and monitor power actions with capability probing
- Per-scene colour profile and Windows night-light coordination
- Scene scheduling and calendar integration
- Optional scene sync/export bundles with secrets-free conflict handling
- Signed PowerShell module and richer shell completion
- Accessibility pass for high contrast, screen readers, reduced motion, and touch
- Diagnostics bundle with redaction, display topology history, and recovery guidance
- Community translations and contribution-ready localization workflow

## Not planned without a safety design

- Automatic overclocking or unsupported display timings
- Silent destructive replacement of scenes or profiles during import
- Cloud accounts or telemetry required for core display switching
