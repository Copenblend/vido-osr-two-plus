# OSR2+ Plugin for Vido

The OSR2+ plugin lets you control OSR2+ stroker devices directly from Vido. It synchronizes funscript files with video playback, sending TCode commands over Serial or UDP in real time.

## Features

- Automatic funscript loading matched to the current video
- Multi-axis support: Stroke (L0), Twist (R0), Roll (R1), Pitch (R2)
- Per-axis range, fill patterns, position offset, and speed controls
- Serial (COM port) and UDP device connections
- Quick Connect toolbar button for one-click connect/disconnect
- Funscript visualizer with Graph and Heatmap views
- Configurable TCode output rate and global timing offset
- Test mode for verifying device response without video playback

## Installation

1. Open Vido and go to **Settings → Plugins**
2. Click **Install from Registry** and select the OSR2+ plugin
3. Restart Vido when prompted

For manual installation, download the plugin zip and extract it to:
```
%APPDATA%\Vido\plugins\com.vido.osr2-plus\
```
Then restart Vido.

## Getting Started

1. **Power on** your OSR2+ device
2. Click the **OSR2+ Quick Connect** button in the toolbar (or open the OSR2+ sidebar panel and click **Connect**)
3. Open a video that has matching `.funscript` files — the plugin loads them automatically
4. Press play — the device moves in sync with the video

## Connection Setup

### UDP (Default)

UDP is the recommended connection mode. It works with devices running TCode firmware that accept UDP commands (e.g., via ESP32 WiFi).

1. Open the **OSR2+ sidebar** panel
2. Set the mode to **UDP**
3. Enter the **port number** (default: 7777) — this must match your device's configured port
4. Click **Connect**

### Serial (COM Port)

Use Serial for USB-connected devices.

1. Open the **OSR2+ sidebar** panel
2. Set the mode to **Serial**
3. Click **Refresh** to scan for available COM ports
4. Select your device's **COM port** from the dropdown
5. Select the matching **baud rate** (default: 115200 — check your firmware settings)
6. Click **Connect**

When you connect, the plugin gradually moves all axes to their center position over 2 seconds. This ensures the device starts from a safe, known state.

## Sidebar Panel

The sidebar panel contains your connection controls and global settings:

- **Connection Mode** — Switch between UDP and Serial
- **UDP Port** — The port number for UDP connections
- **COM Port / Baud Rate** — Serial connection settings (visible in Serial mode)
- **Refresh** — Rescan for available COM ports
- **Connect / Disconnect** — Toggle the device connection
- **Output Rate (Hz)** — How many TCode commands per second are sent to the device (30–200 Hz). Higher values give smoother motion but increase CPU/network usage. Default: 100 Hz.
- **Global Offset (ms)** — Shift all funscript timing earlier (negative) or later (positive), from -500ms to +500ms. Useful if your device feels slightly ahead or behind the video.
- **Axis Settings** — Opens the Axis Control panel
- **Visualizer** — Opens the Funscript Visualizer panel

## Axis Control

The Axis Control panel (right panel) shows a card for each of the four axes. Each card has the following controls:

### Enable/Disable

Toggle an axis on or off. Disabled axes receive no commands and the device ignores that axis.

### Range Slider (Min/Max)

A dual-thumb slider that controls the minimum and maximum range for the axis. Reducing the range limits how far the device moves on that axis. For example, setting Stroke to 20–80% keeps the device within the middle 60% of its travel.

### Fill Mode

Fill modes generate movement patterns on axes that don't have a funscript loaded. They also run during test mode. Available fill modes:

| Fill Mode | Description |
|-----------|-------------|
| **None** | No fill — the axis stays at its center position when idle |
| **Random** | Smooth random movement with cosine-interpolated transitions |
| **Triangle** | Linear up-and-down waveform |
| **Sine** | Smooth sinusoidal wave |
| **Saw** | Linear ramp up, instant drop |
| **Sawtooth Reverse** | Instant snap up, linear ramp down |
| **Square** | Instant alternation between min and max |
| **Pulse** | Holds at extremes with quick transitions |
| **Ease In/Out** | Sine-like with sharper acceleration at the extremes |
| **Grind** | Pitch follows the stroke inversely — when the stroke goes up, pitch goes down (Pitch axis only) |
| **Figure 8** | Creates a figure-8 motion path using stroke position and direction (Roll/Pitch axes) |

### Sync with Stroke

When enabled, the fill pattern only advances when the Stroke (L0) axis is actively moving. When L0 stops, the fill freezes. This keeps secondary axis patterns in sync with the main stroke action. Available on all axes except Stroke (since Stroke is the sync source).

### Fill Speed (Hz)

Controls how fast the fill pattern runs, from 0.1 Hz (very slow) to 3.0 Hz (very fast). Only visible when a fill mode other than None is selected.

### Position Offset

Adjusts the physical center position of the axis:

- **Stroke (L0):** Offset ranges from -50% to +50%. A negative value shifts the center downward; positive shifts it upward.
- **Twist (R0):** Offset ranges from 0° to 179°. This rotates the neutral position of the twist axis.

The offset is applied to all commands including homing, fill patterns, and funscript playback.

### Script Assignment

Each axis card shows the currently loaded funscript file. Scripts are loaded automatically when you open a video (see Funscript File Naming below). You can also:

- **Browse** — Manually select a funscript file for any axis
- **Clear** — Remove a manually assigned script (auto-loading resumes for that axis)

Manual overrides persist until cleared — they won't be replaced when you load a new video.

### Test Mode

Click the **Test** button to run all enabled axes through their configured fill patterns without needing video playback. This is useful for verifying device response and tuning fill settings.

- You can change fill modes and settings while test mode is active — changes take effect immediately
- Test mode stops automatically when you play a video or disconnect the device
- Click **Stop** to end test mode manually

## Funscript File Naming

The plugin automatically loads funscript files that match your video filename:

| Axis | File Pattern | Example |
|------|-------------|---------|
| Stroke (L0) | `video.funscript` | `scene01.funscript` |
| Twist (R0) | `video.twist.funscript` | `scene01.twist.funscript` |
| Roll (R1) | `video.roll.funscript` | `scene01.roll.funscript` |
| Pitch (R2) | `video.pitch.funscript` | `scene01.pitch.funscript` |

Place the funscript files in the same folder as the video file. The plugin also supports **multi-axis funscript files** — a single `.funscript` file that contains data for multiple axes embedded inside it.

## Funscript Visualizer

The Funscript Visualizer appears in the bottom panel and shows your loaded funscript data in real time as the video plays. Two visualization modes are available:

### Graph Mode

Displays all loaded axes as color-coded polyline graphs. A vertical cursor shows the current playback position. Each axis is drawn in its configured color:

- **Stroke (L0)** — Blue
- **Twist (R0)** — Purple
- **Roll (R1)** — Orange
- **Pitch (R2)** — Green

### Heatmap Mode

Shows a speed-based color gradient for the Stroke (L0) axis. The heatmap uses the community-standard color scale where cooler colors represent slower movement and warmer colors represent faster movement. A vertical cursor marks the current playback position.

### Window Duration

Control how much time the visualizer shows at once. Available options: 30 seconds, 1 minute, 2 minutes, or 5 minutes. This can be changed in the visualizer panel or in Settings.

## Settings

All plugin settings can also be configured from **Vido → Settings → OSR2+**:

| Setting | Default | Description |
|---------|---------|-------------|
| Default Connection Mode | UDP | Whether to use UDP or Serial by default |
| Default UDP Port | 7777 | UDP port for device communication |
| Default Baud Rate | 115200 | Serial baud rate |
| TCode Output Rate | 100 Hz | Commands per second sent to the device (30–200) |
| Global Funscript Offset | 0 ms | Timing offset for all funscripts (-500 to +500) |
| Visualizer Window Duration | 60s | How much time the visualizer shows |

Per-axis settings (range, fill mode, speed, sync, offset) are saved automatically and persist between sessions.

## Status Bar

The status bar at the bottom of Vido shows the current connection state:

- **OSR2+:Not Connected** — No connection attempt made yet
- **UDP:7777:Connected** — Connected via UDP on port 7777
- **COM:COM3:Connected** — Connected via Serial on COM3
- **UDP:7777:Disconnected** / **COM:Disconnected** — Connection failed or was lost

## Troubleshooting

### Device not responding

- Check that your device is powered on and the firmware is running
- Verify the connection mode matches your setup (UDP for WiFi, Serial for USB)
- For Serial: make sure the correct COM port is selected and no other application is using it
- For UDP: confirm the port number matches your device's firmware configuration
- Try lowering the output rate to 60 Hz — some devices perform better at lower rates

### Funscripts not loading automatically

- Check that the funscript files are in the same folder as the video
- Verify the file naming matches the expected pattern (e.g., `video.funscript`, `video.twist.funscript`)
- Use the **Browse** button on each axis card to manually assign scripts

### Motion feels out of sync

- Adjust the **Global Offset** in the sidebar. Negative values make the device react earlier; positive values delay it.
- Start with small adjustments (±50ms) and fine-tune from there.

### Device moves too far / not enough

- Use the **Range Slider** on each axis card to limit the travel range
- Reduce the Max value to limit extreme positions

### COM port not showing up

- Click **Refresh** to rescan for ports
- Make sure your device drivers are installed (often CH340 or FTDI drivers)
- Check that the USB cable supports data transfer (not charge-only)

## FAQ

**Q: Can I use this with devices other than the OSR2+?**
A: Any device that accepts TCode commands over Serial or UDP should work. The four axes (L0, R0, R1, R2) are standard TCode axes.

**Q: What baud rate should I use?**
A: The default (115200) works for most devices. Check your firmware configuration if unsure. Higher baud rates like 250000 are faster but not supported by all hardware.

**Q: Can I use multiple funscript files at once?**
A: Yes — each axis loads its own funscript file automatically based on the file naming convention. You can also use multi-axis funscript files that contain all axes in one file.

**Q: What happens if I disconnect during playback?**
A: The plugin stops sending commands immediately. Your device will stop at its last position. Reconnecting will home all axes back to center.

**Q: Does the plugin work with streaming/online videos?**
A: The plugin responds to whatever funscripts are loaded. If your funscript files are available locally and named to match the video, they will load regardless of the video source.
