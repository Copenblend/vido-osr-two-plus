# OSR2+ Plugin for Vido

Control your OSR2+ stroker device directly from Vido. Load funscript files, sync playback with haptic motion, and configure multi-axis output — all from the player UI.

---

## Features

### Connection

Connect to your OSR2+ device via **UDP** or **Serial (COM port)**.

| Setting | Default | Range |
|---------|---------|-------|
| Connection Mode | UDP | UDP, Serial |
| UDP Port | 7777 | 1–65535 |
| Baud Rate | 115200 | 9600, 19200, 38400, 57600, 115200, 250000 |

- **Quick Connect** — Toolbar button toggles the connection on/off. A green highlight indicates connected state; red indicates disconnected.
- **Status Bar** — Shows current connection status with color coding (e.g., `UDP:7777:Connected`).
- **COM Port Detection** — Serial ports are auto-detected. Use the Refresh button to rescan.
- Connection settings are locked while connected to prevent misconfiguration.

### Funscript Loading

Funscript files are loaded automatically when a video is opened. The plugin follows the community naming convention:

| File Pattern | Axis |
|-------------|------|
| `video.funscript` | L0 (Stroke) |
| `video.twist.funscript` | R0 (Twist) |
| `video.roll.funscript` | R1 (Roll) |
| `video.pitch.funscript` | R2 (Pitch) |

**Multi-axis format** is also supported — a single `.funscript` file can contain embedded axis data in the `axes` array.

Scripts can be loaded manually per-axis using the **Open** button in the Axis Control panel. Manual assignments take priority over auto-loaded scripts.

#### Global Funscript Offset

Shift all script timing by **-500 to +500 ms** to compensate for audio/video latency. Negative values make the script play earlier; positive values add delay.

### Supported Axes

| Axis | Name | Color | Default Range | Offset |
|------|------|-------|---------------|--------|
| L0 | Stroke | Blue (#007ACC) | 0–100% | -50% to +50% (shifts midpoint) |
| R0 | Twist | Purple (#B800CC) | 0–100% | 0°–179° (rotational offset) |
| R1 | Roll | Orange (#CC5200) | 0–100% | — |
| R2 | Pitch | Green (#14CC00) | 0–75% | — |

Each axis has independent **Min/Max amplitude** controls via a dual-thumb range slider. Drag the track between the thumbs to shift the entire range.

R2 (Pitch) defaults to a reduced 0–75% range for safety.

### TCode Output

Commands are generated on a dedicated high-priority background thread with sub-millisecond timing precision.

| Setting | Default | Range |
|---------|---------|-------|
| Output Rate | 100 Hz | 30–200 Hz |

- **Dirty-value tracking** — Only axes whose TCode value changed are included in each command.
- **Multi-axis commands** — All changed axes are sent on a single line, space-separated.
- **Format** — `L0500I10` means "move L0 to position 500 over 10 ms".

### Fill Modes (Pattern Generation)

When no funscript is loaded for a rotation axis (R0, R1, R2), a fill pattern can generate motion automatically.

| Mode | Description |
|------|-------------|
| None | No fill — axis is idle unless scripted |
| Random | Cosine-interpolated random targets |
| Triangle | Linear ascending/descending wave |
| Sine | Smooth sinusoidal oscillation |
| Saw | Linear ramp up → cosine drop |
| Sawtooth Reverse | Cosine rise → linear ramp down |
| Square | Cosine transitions between high/low dwells |
| Pulse | Quick cosine transitions with held extremes |
| Ease In/Out | Cubic easing applied to a triangle base |
| Grind | Pitch inversely follows stroke position (R2 only) |
| Figure 8 | Lissajous figure-8 path (R1, R2 only) |

L0 (Stroke) does not support fill modes — it is always driven by funscript data.

#### Sync with Stroke

When enabled, the fill pattern advances in sync with the L0 stroke speed and direction. When disabled, the fill runs at an independent configurable speed (0.1–3.0 Hz).

Grind and Figure 8 modes always sync with stroke automatically.

#### Smooth Transitions

- **Ramp-up** — When a fill mode is activated, output ramps up smoothly from midpoint to prevent sudden jumps.
- **Return-to-center** — When a fill mode is deactivated, the axis glides smoothly back to the midpoint.

### Test Mode

Each axis has a **Test** button that oscillates the axis using its configured fill mode pattern. Test speed is adjustable from 0.1 to 3.0 Hz.

- L0 always uses a Triangle waveform in test mode.
- Test is disabled when no device is connected or a video is playing.
- Test stops automatically when video playback starts.

### Funscript Visualizer (Bottom Panel)

Two visualization modes are available for the loaded scripts:

- **Graph** — Multi-axis overlay with color-coded polylines and legend. Shows all active axes simultaneously.
- **Heatmap** — Speed-based color gradient for the L0 (Stroke) axis.

The time window is configurable: 30 seconds, 1 minute, 2 minutes, or 5 minutes (default: 60 seconds). The view scrolls through the script in sync with video playback.

### File Icons

Funscript files are shown with color-coded icons in the Vido file explorer:

- `.funscript` — Blue (Stroke)
- `.twist.funscript` — Purple (Twist)
- `.roll.funscript` — Orange (Roll)
- `.pitch.funscript` — Green (Pitch)

---

## UI Layout

### Sidebar

Open the OSR2+ sidebar panel from the activity bar. It contains:

- Connection mode selector (UDP / Serial)
- Transport-specific settings (port, baud rate, COM port)
- Connect / Disconnect button
- Output Rate slider
- Global Offset slider
- Buttons to open the Axis Control and Funscript Visualizer panels

### Axis Control (Right Panel)

Four collapsible axis cards (L0, R0, R1, R2), each showing:

- Enable/Disable toggle
- Range slider (min/max amplitude)
- Fill Mode selector
- Sync with Stroke toggle and fill speed slider
- Funscript file loader
- Position Offset slider (L0 and R0 only)
- Test section with speed slider and Test/Stop button

### Status Bar

Displays connection status with color coding at the bottom of the window.

---

## Settings

All settings are persisted between sessions via the Vido plugin settings store. They can be configured from the sidebar panel, the axis control panel, or from Vido's Settings → Extensions page.

---

## Requirements

- Vido 0.1.0 or later
- OSR2+ device (or compatible TCode device)
- UDP endpoint (e.g., MultiFunPlayer relay) or direct Serial connection

## License

MIT
