# Changelog

All notable changes to the OSR2+ Plugin for Vido will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [4.4.0] - 2026-03-01

### Changed

- Updated to Vido.Core 0.13.0 and Vido.Haptics 3.0.0.

### Performance

- **vido-301** — Removed LINQ allocations from TCode output hot path; cached stroke config, active-fill flag, loop-based axis lookups.
- **vido-302** — Replaced per-update dictionary for external axis positions with fixed flat arrays, eliminating hash-lookup and allocation overhead.
- **vido-303** — Added `Send(ReadOnlySpan<byte>)` to transport interface; Serial and UDP transports send without allocating strings.
- **vido-304** — Replaced per-tick `List<string>` command assembly with a reusable byte buffer for allocation-free TCode formatting.
- **vido-305** — Verified UDP send path is allocation-free end-to-end; added allocation-focused transport tests.
- **vido-306** — Pre-allocated all Skia paint/path/typeface objects in the funscript visualizer to eliminate per-frame rendering allocations.
- **vido-307** — Added dirty-flag gating to the visualizer so it only repaints when data actually changes.
- **vido-308** — Pre-allocated `SKMaskFilter` blur for beat-bar glow rendering; reused across frames instead of created/disposed each tick.
- **vido-309** — Removed LINQ and intermediate collection allocations from event-driven paths in `BeatBarViewModel` and `Osr2PlusPlugin`.
- **vido-310** — Replaced per-call dictionary allocations in script-loading flows with a reusable field-backed dictionary.
- **vido-311** — Extracted three duplicate nested `RelayCommand` implementations into one shared class.
- **vido-312** — Replaced DOM-based (`JsonDocument`) funscript parsing with streaming `Utf8JsonReader`; added BOM/encoding normalization.
- **vido-313** — Added pre-count pass for funscript action arrays so lists are initialized with exact capacity.
- **vido-314** — Made beat-bar fullscreen margin adjustment event-driven (`SizeChanged`) instead of recalculating every render tick.

### Tests

- Test suite expanded from 641 to 736 tests covering all optimization work.

## [4.1.0] - 2026-02-26

### Maintenance / Validation

- Included in vido-series cross-repo ticket validation runs (vido-113 through vido-118).
- Adopted strict completion gate expectation: zero build warnings and zero test warnings when repository is part of ticket validation scope.

### Added

- **External beat source API** — `IExternalBeatSource` interface allowing third-party plugins to provide beat data to the BeatBar
- **BeatBar external modes** — BeatBar mode selector dynamically includes modes registered by external plugins (e.g. Pulse)
- **External axis positions** — `ExternalAxisPositionsEvent` allows external plugins to drive L0 stroke positions directly
- **External beat events** — `ExternalBeatEvent` notifies the BeatBar of beats detected by external sources
- **Funscript suppression** — `SuppressFunscriptEvent` lets external plugins disable funscript auto-loading when active
- **BeatBar mode persistence** — selected BeatBar mode (including external modes) is saved and restored across sessions
- **Pending mode restoration** — if a saved external BeatBar mode isn't yet available at startup, selection is deferred until the source registers
- **Beat rate divisor** — control bar ComboBox for beat rate selection (1, 1/2, 1/4, 1/8) affecting external beat sources

### Changed

- Updated funscript file icons to text-based labels (FS, R0, R1, R2) _(previously released as 2.0.0)_

## [2.0.0] - 2026-02-23

### Changed

- Updated funscript file icons to text-based labels (FS, R0, R1, R2)

## [1.0.0] - 2025-07-22

### Added

- **Device connectivity** via Serial (COM port) and UDP, with configurable baud rate and port settings
- **Quick Connect** toolbar button for one-click connect and disconnect
- **Automatic funscript loading** that matches `.funscript`, `.twist.funscript`, `.roll.funscript`, and `.pitch.funscript` files to the current video
- **Multi-axis funscript support** — single funscript files containing embedded data for multiple axes
- **Manual script assignment** with per-axis Browse and Clear controls; manual overrides persist across video changes
- **Four-axis TCode control** — Stroke (L0), Twist (R0), Roll (R1), and Pitch (R2)
- **Per-axis range sliders** (Min/Max) to limit device travel on each axis
- **11 fill modes** for generating motion patterns on axes without funscripts: None, Random, Triangle, Sine, Saw, Sawtooth Reverse, Square, Pulse, Ease In/Out, Grind, and Figure 8
- **Sync with Stroke** option — secondary axis fill patterns advance only when the Stroke axis is moving
- **Fill speed control** (0.1–5.0 Hz) for each axis
- **Position offset** — adjustable center position for Stroke (±50%) and Twist (0–179°)
- **Axes homing on connect** — all axes gradually move to their center position over 2 seconds when connecting, respecting configured position offsets
- **Test mode** for verifying device response without video playback; supports changing fill modes and settings while active
- **Funscript Visualizer** with Graph mode (color-coded polyline per axis) and Heatmap mode (speed-based color gradient for Stroke)
- **Visualizer window duration** options: 30 seconds, 1 minute, 2 minutes, 5 minutes
- **Status bar** showing connection state (mode, port/COM, and status)
- **Configurable TCode output rate** (30–200 Hz)
- **Global funscript timing offset** (-500ms to +500ms) for compensating device latency
- **Persistent settings** — all axis configurations and plugin settings saved automatically between sessions
- **641 unit tests** covering services, view models, pattern generation, parsing, transport, and rendering
