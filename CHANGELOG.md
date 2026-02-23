# Changelog

All notable changes to the OSR2+ Plugin for Vido will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
