# DisplayPilot — Language / Stack Options

Comparison of realistic stacks for rewriting this Windows tray utility (primary-monitor switching via DisplayConfig + CDS fallback). Current codebase: **C# / .NET 8 WinForms**, ~2k LOC, heavy P/Invoke to `user32`, `gdi32`, and display APIs.

## Summary table

| Option | Binary size | Startup | GUI quality | Display API access | Port effort |
|--------|-------------|---------|-------------|-------------------|-------------|
| **C# + WinUI 3 / WPF** | ~5 MB FDD + runtime | Fast (FDD) | Excellent | Full (same P/Invoke or CsWin32) | **Low–medium** |
| **Rust + windows-rs + tray** | ~1–3 MB static | Very fast | Basic–good (native or egui) | Full via `windows` crate | **Medium–high** |
| **Tauri 2** | ~8–15 MB | Good | Excellent (web UI) | Full in Rust backend | **Medium** |
| **Go + systray** | ~5–10 MB static | Fast | Basic | Full via `syscall` / `golang.org/x/sys/windows` | **Medium** |
| **C++ Win32** | ~0.5–2 MB | Fastest | Basic–good (raw or ImGui) | Native | **High** |

---

## 1. Stay C# — WinUI 3 or WPF

**Pros**
- Reuse all display logic (`DisplayManager`, `DisplayInterop`, tray fallbacks) with minimal changes.
- WinUI 3 / WPF give modern styling, animations, and crisp dark themes without fighting WinForms layout.
- Same ecosystem: `NotifyIcon` alternatives exist; hotkeys and `Shell_NotifyIcon` P/Invoke unchanged.
- Fastest path to a polished GUI while keeping .NET 8 framework-dependent publish (~5 MB).

**Cons**
- Still requires .NET Desktop Runtime unless self-contained (~170 MB).
- WinUI 3 has a steeper learning curve and heavier package footprint than WPF.
- WinForms tray quirks (Windows 11 overflow) may persist unless you reimplement tray host carefully.

**Fit:** **Best incremental upgrade.** Keep core services, replace UI layer only. WPF if you want stability; WinUI 3 if you want Fluent Design native controls.

---

## 2. Rust + windows-rs + tray crate

**Pros**
- Tiny single binary (~1–3 MB), no runtime, instant startup.
- `windows` crate maps cleanly to existing P/Invoke signatures.
- Crates like `tray-icon`, `muda`, `global-hotkey` cover systray + menu + hotkey.
- Strong memory safety for long-running tray process.

**Cons**
- Full rewrite of UI (egui, iced, slint, or native Win32 dialogs).
- DisplayConfig structs and error paths are verbose in Rust.
- Smaller talent pool / slower iteration if you're used to C#.

**Fit:** **Best raw performance and footprint** if you accept rebuilding UI from scratch. Good for a minimal panel + tray menu without fancy chrome.

---

## 3. Tauri 2 (Rust backend + web frontend)

**Pros**
- Polished UI with HTML/CSS/React/Svelte — easiest path to a beautiful control panel.
- Rust backend handles display APIs and tray; frontend stays declarative.
- Cross-platform potential (macOS/Linux display APIs differ, but shell is shared).
- Reasonable bundle size for a desktop app with web UI.

**Cons**
- WebView2 dependency on Windows (usually preinstalled on Win11).
- Two-language project; debugging spans JS + Rust.
- Tray integration on Windows is solid but less battle-tested than pure Win32 for edge cases (icon promotion, TaskbarCreated).
- Slightly heavier than pure Rust or C++ for a tiny utility.

**Fit:** **Best GUI polish** if the main complaint is WinForms look-and-feel. More moving parts than WPF for a small utility.

---

## 4. Go + systray (e.g. getlantern/systray)

**Pros**
- Simple concurrency model; quick to scaffold tray + menu.
- Single static binary, no runtime.
- `golang.org/x/sys/windows` for DisplayConfig P/Invoke-style calls.

**Cons**
- GUI options are weak: mostly native dialogs or embedding a webview — no first-class modern UI.
- Systray libraries vary in Windows 11 behavior.
- Manual struct packing for DisplayConfig is error-prone.
- Garbage-collected runtime less ideal for P/Invoke-heavy low-level code (usually fine in practice).

**Fit:** **Fast tray-only MVP**, not ideal if you want a rich mini control panel.

---

## 5. C++ Win32 (optionally ImGui)

**Pros**
- Smallest binary, fastest startup, zero runtime.
- Direct access to every Windows display API — no marshalling layer.
- Full control over tray icon, DWM rounded corners, and hotkeys.

**Cons**
- Highest development and maintenance cost.
- Manual COM, RAII, and string handling.
- Modern UI requires ImGui, WinUI XAML Islands, or significant custom drawing.

**Fit:** **Maximum control and minimal footprint** when you want no dependencies and accept C++ maintenance.

---

## Recommendation

Given typical complaints with this app (**GUI polish**, **tray visibility on Windows 11**, **startup weight**):

### Phased approach (recommended)

1. **Short term — stay C#, upgrade UI to WPF** (or WinUI 3 if you want Fluent). Port `DisplayManager` and native interop as-is; rebuild `MiniControlForm` in XAML. Keeps publish size ~5 MB FDD, lowest risk, addresses GUI directly.
2. **Tray hardening in place** — regardless of stack, keep/invest in `Shell_NotifyIcon` fallback and `TrayIconSettings` promotion (already in C#).
3. **Full rewrite only if** you need a sub-3 MB single exe with no .NET runtime → **Rust + windows-rs**, accepting a simpler UI or using **slint/iced**.

### If picking one stack today

**C# + WPF** — best effort-to-value: modern UI, reuse 80% of logic, familiar tooling, fast iteration. Avoid a full rewrite until WPF refresh still leaves runtime size or tray issues unresolved.

### If runtime size is the top priority

**Rust + windows-rs + tray-icon** — ship a lean tray-first tool; add a minimal settings window with egui or slint later.

---

## Migration scope estimate (from current C#)

| Component | Reusable as-is | Must rewrite |
|-----------|----------------|--------------|
| DisplayConfig / CDS logic | C#, Rust, C++, Go (port structs) | — |
| Tray + hotkey host | Partial patterns | UI framework-specific |
| Mini control panel | — | Any new stack |
| Startup registry / logging | Trivial in any language | — |
| Custom controls (cards, toggles) | — | All UI |

**Rough effort:** WPF UI swap ~1–2 weeks part-time; full Rust/Tauri rewrite ~3–6 weeks part-time including UI parity and tray edge cases.
