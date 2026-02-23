# Changelog

All notable changes to the OSR2+ Plugin for Vido are documented here.

## 1.0.0 — 2026-02-23

Initial release of the OSR2+ haptic device plugin for Vido.

### Connection

- **UDP transport** — Send TCode commands to a configurable UDP port (default 7777). Works with MultiFunPlayer relay or any compatible UDP endpoint.
- **Serial (COM port) transport** — Direct serial connection at configurable baud rate (9600–250000, default 115200) with 8N1 framing.
- **Auto-detection** — COM ports are discovered automatically; a Refresh button rescans available ports.
- **Quick Connect toolbar button** — One-click connect/disconnect toggle with green (connected) or red (disconnected) highlight indicator.
- **Status bar indicator** — Displays live connection state with color coding: `UDP:7777:Connected`, `COM:COM3:Disconnected`, etc.
- Settings lock while connected to prevent accidental misconfiguration.

### Funscript

- **Automatic loading** — Funscript files matching the video filename are loaded automatically using community naming convention (`.funscript`, `.twist.funscript`, `.roll.funscript`, `.pitch.funscript`).
- **Multi-axis format** — Embedded multi-axis funscript files are supported (single file with `axes` array).
- **Manual per-axis loading** — Open any `.funscript` file for a specific axis via file dialog. Manual assignments override auto-loaded scripts.
- **Global timing offset** — Adjustable -500 to +500 ms offset to compensate for audio/video latency.

### TCode Output

- **Dedicated output thread** — High-priority background thread with sub-millisecond timing precision using `Stopwatch` + `SpinWait` hybrid sleep.
- **Configurable output rate** — 30–200 Hz (default 100 Hz) controls how many TCode commands are sent per second.
- **Dirty-value tracking** — Only transmits axes whose position changed since the last command.
- **Multi-axis batching** — All changed axes sent space-separated on a single line with interval parameter.
- **Time extrapolation** — Anchored sync-point system with stopwatch-based interpolation between UI position ticks for smooth motion.
- **Playback speed tracking** — Adapts to Vido's playback speed changes in real time.

### Axes

- **Four axes supported** — L0 (Stroke), R0 (Twist), R1 (Roll), R2 (Pitch).
- **Per-axis amplitude range** — Dual-thumb range slider with draggable middle region to set min/max output bounds.
- **Per-axis enable/disable** — Toggle each axis independently.
- **Position offset** — L0 supports -50% to +50% midpoint shift; R0 supports 0°–179° rotational offset. Both provide immediate live feedback.
- **Safety defaults** — R2 (Pitch) limited to 0–75% range by default; Grind fill capped at 70% pitch.

### Fill Modes

- **10 fill patterns** — None, Random, Triangle, Sine, Saw, Sawtooth Reverse, Square, Pulse, Ease In/Out, Grind, Figure 8.
- **Sync with Stroke** — Fill patterns can synchronize with L0 stroke speed and direction using cumulative stroke distance tracking.
- **Independent speed** — When not synced, fills run at a configurable 0.1–3.0 Hz rate per axis.
- **Smooth transitions** — Ramp-up animation when activating fill modes; return-to-center animation when deactivating.
- **Axis-specific modes** — Grind (R2 only) inversely follows stroke position. Figure 8 (R1/R2) creates Lissajous paths.

### Test Mode

- **Per-axis test** — Oscillate any axis at configurable speed (0.1–3.0 Hz) using its selected fill mode pattern.
- **Smooth ramp-up** — Test amplitude and speed ramp up gradually via exponential smoothing.
- **Safety interlocks** — Test disabled when disconnected or during video playback; auto-stops when playback begins.

### Visualizer

- **Graph mode** — Multi-axis polyline overlay with color-coded lines (L0 blue, R0 purple, R1 orange, R2 green) and legend.
- **Heatmap mode** — Speed-based color gradient visualization for L0 (Stroke) axis.
- **Configurable time window** — 30 seconds, 1 minute, 2 minutes, or 5 minutes (default 60 seconds).
- **Playback cursor** — Scrolling indicator bar tracks current video position.
- Rendered with SkiaSharp for smooth performance.

### UI

- **Sidebar panel** — Connection settings, output rate, global offset, and panel launchers.
- **Axis Control right panel** — Four collapsible cards with full per-axis configuration.
- **Funscript Visualizer bottom panel** — Graph and heatmap visualization modes.
- **File icons** — Color-coded icons for `.funscript`, `.twist.funscript`, `.roll.funscript`, and `.pitch.funscript` in the file explorer.
- **Dark theme** — Styled to match Vido's Dark Modern theme with accent color (#007ACC).
- **Custom RangeSlider control** — Dual-thumb slider with axis-colored track fill.

### Persistence

- All connection, axis, and visualizer settings are saved between sessions via the Vido plugin settings store.
- Last-shown right panel is remembered across sessions.
