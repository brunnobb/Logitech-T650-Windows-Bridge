# Logitech T650 Windows Precision Touchpad Bridge

[![Release](https://img.shields.io/badge/Release-v1.1.9-blue.svg)](https://github.com/brunnobb/Logitech-T650-Windows-Bridge/releases)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

The **Logitech T650 Bridge** modernizes the Logitech T650 Wireless Rechargeable Touchpad on Windows 10 and Windows 11. 

While the T650 natively speaks Logitech's proprietary HID++ 2.0 protocol over the 2.4 GHz Unifying Receiver, Windows defaults to basic 2-button mouse emulation and relies on obsolete SetPoint software. This bridge unlocks the hardware's raw multi-touch sensor (up to 5 contact points) and connects it directly into modern Windows workflows.

---

## Operating Modes: Virtual PTP vs. Standalone

The bridge is a single, unified high-performance application featuring runtime mode selection:

* **Virtual PTP Mode (Tier 2 Driver Injection):** Feeds raw multi-touch reports into the [VirtualPTP Driver](https://github.com/brunnobb/VirtualPTP) over a low-latency Named Pipe (`\\.\pipe\T650VirtualPtpPipe`), transforming the T650 into a native Windows Precision Touchpad.
* **Standalone Mode (Tier 1 Direct Software Engine):** Synthesizes mouse pointer motion, scroll wheel ticks, and custom multi-touch gesture shortcuts directly via `SendInput`.
* **Automatic Mode (Recommended):** Probes for the Virtual PTP driver. If present, it injects into Windows PTP; if the driver is not installed, it falls back seamlessly to Standalone mode without manual configuration.

### Advantages & Disadvantages Comparison

| Feature | Virtual PTP Mode (Tier 2 Driver) | Standalone Mode (Tier 1 Software) |
| :--- | :--- | :--- |
| **System Integration** | **100% Native Windows Precision Touchpad**.<br>Recognized by Windows as a built-in touchpad in `Settings -> Bluetooth & devices -> Touchpad`. | **Software Input Synthesis**.<br>Runs as a standalone background application using Windows `SendInput`. |
| **Scrolling & Zoom** | **Fluid Touch Physics & Inertia**.<br>Pixel-smooth scrolling, momentum flicking, and native pinch-to-zoom in Edge, Chrome, Word, and Excel. | **Standard Wheel Simulation**.<br>Simulates smooth mouse wheel ticks. No native pinch-to-zoom. |
| **OS Gesture Animations** | **Fluid Interactive Animations**.<br>Virtual desktop sliding tracks finger position 1:1 in real time; Task View opens proportionally to swipe motion. | **Shortcut Triggered**.<br>Fires keyboard shortcut chords (`Ctrl+Win+Left/Right`, `Win+Tab`) upon completing a swipe. |
| **Shortcut Customization** | **Fixed by Windows**.<br>Gestures are configured via Microsoft's presets in Windows Settings. | **Fully Customizable**.<br>Any 3-finger or 4-finger swipe or tap can be bound to any custom key combination (or left empty). |
| **Driver Installation** | **Requires Driver Installation**.<br>Requires installing the Virtual PTP Driver package (`VirtualPtpDriver-Setup.msi`). | **Zero Driver Installation**.<br>Only needs `T650Bridge-Setup.msi`. |
| **Windows Test-Signing** | **Requires Test-Signing (`testsigning on`)**.<br>Because the virtual driver is test-signed with a self-signed certificate. | **Works on Stock Windows**.<br>Zero driver signing constraints; works with Secure Boot enabled. |
| **Admin Windows Support** | **Native Windows Touch Stack**.<br>Windows touch subsystem natively interacts with all windows. | **UIAccess UIPI Bypass**.<br>Bridge executable is built with `uiAccess="true"` and Authenticode-signed, bypassing UIPI across elevated Administrator windows without UAC prompts. |

---

## Settings & Features Comparison Matrix

When running the Bridge, certain settings affect the raw hardware feed (impacting both modes), while others only apply to the internal Standalone gesture engine because Windows takes over those functions in Virtual PTP mode:

| Feature / Setting in Bridge | Affects Virtual PTP Mode? | Affects Standalone Mode? | How it is Controlled in Virtual PTP Mode |
| :--- | :---: | :---: | :--- |
| **Multi-Touch Landing Debounce** (`DebounceEnabled`, `LandingDebounceMs`) | **YES** | **YES** | **Bridge Engine:** Buffers initial touchdown for 15–40 ms so multi-finger contacts arrive together before Windows gesture recognizer sees them. |
| **Suppress Landing Confidence** (`SuppressLandingConfidence`) | **YES (Crucial)** | *Internal* | **Microsoft PTP Specification:** Injects `Confidence = 0` during initial touchdown so Windows won't trigger premature Start menu or Search taps before multi-finger swipes. |
| **Cursor Speed / Sensitivity** (`CursorSensitivity`) | **NO** | **YES** | **Controlled by Windows:**<br>`Windows Settings -> Bluetooth & devices -> Touchpad -> Cursor speed`. The driver passes raw high-resolution coordinates directly to Windows. |
| **Tap to Drag** (`TapToDragEnabled`) | **NO** | **YES** | **Controlled by Windows:**<br>`Windows Settings -> Bluetooth & devices -> Touchpad -> Taps -> "Tap twice and drag to multi-select"`. |
| **Double Click Timeout** (`DoubleClickTimeoutMs`) | **NO** | **YES** | **Controlled by Windows:**<br>`Control Panel -> Mouse -> Buttons -> Double-Click Speed`. |
| **Custom Gesture Shortcuts** (3 & 4-finger swipes & clicks) | **NO** | **YES** | **Controlled by Windows:**<br>`Windows Settings -> Bluetooth & devices -> Touchpad -> Three-finger / Four-finger gestures`. |
| **Integration Mode** (`Automatic / Standalone / PtpDriver`) | **YES** | **YES** | Switches Bridge between feeding the PTP Named Pipe (`\\.\pipe\T650VirtualPtpPipe`) vs the internal C# engine. |
| **Diagnostic Logging & System Tray** (`LogTouchFrames`, autostart, etc.) | **YES** | **YES** | Manages diagnostics, frame inspection, and background operation. |

---

## Architecture & Hardware Protocol

```text
┌────────────────────────────────────────────────────────┐
│  Logitech T650 Hardware                                │
│  - 2.4 GHz Unifying RF (VID: 0x046D, PID: 0xC52B)       │
└──────────────────────────┬─────────────────────────────┘
                           │ 20-byte HID++ 2.0 Reports
┌──────────────────────────▼─────────────────────────────┐
│  T650Bridge Daemon (uiAccess="true" UIPI Bypass)       │
│  - Feature 0x6100 (Touchpad Raw XY) Activation         │
│  - Hotplug Supervisor & Wireless Power-Cycle Detection │
│  - Landing Debounce & Contact Continuity Engine        │
│  - GUI Settings Window & Tray Diagnostics              │
└────────────┬───────────────────────────────┬───────────┘
             │ (PTP Mode)                    │ (Standalone Mode)
             ▼                               ▼
┌──────────────────────────┐   ┌──────────────────────────┐
│ VirtualPTP Driver Pipe   │   │ Windows SendInput Engine │
│ (\\.\pipe\...)           │   │ (Customizable Shortcuts) │
└──────────────────────────┘   └──────────────────────────┘
```

---

## Directory Structure

```text
T650Bridge/
├── T650Bridge.sln              # Visual Studio solution file
├── src/                        # C# source code and project
│   ├── T650Bridge.csproj       # Unified .NET project file
│   ├── Program.cs              # Entry point, CLI args, & main runloop
│   ├── app.manifest            # Application manifest with uiAccess="true"
│   ├── T650.ico                # Application & Tray icons
│   ├── Config/                 # Bridge settings & persistence (settings.json)
│   ├── Gesture/                # Standalone gesture synthesis engine
│   ├── Hidpp/                  # Logitech Unifying HID++ 2.0 protocol
│   ├── Ptp/                    # Virtual PTP driver named pipe client
│   ├── Touch/                  # Multi-touch frame decoding & landing debounce
│   └── UI/                     # System tray icon and Settings window
├── Certificates/               # Code-signing certificates for Authenticode
│   └── T650TestCert.cer        # Authenticode test certificate
├── Installer/                  # WiX Toolset v3.14 MSI installer packages
│   ├── T650Bridge.wxs          # WiX installer definition
│   ├── License.rtf             # Distribution license
│   └── Build-Bridge-Installer.ps1 # WiX compiler script
├── Build-All.ps1               # Complete compile, sign, and package script
├── Bump-Version.ps1            # Automated SemVer bumper
└── Dist/                       # Output MSI installers and SHA-256 hashes
```

---

## Prerequisites

1. **.NET 10.0 SDK** (or .NET 9.0/8.0)
2. **WiX Toolset v3.14** (`C:\Program Files (x86)\WiX Toolset v3.14\bin`)
3. **Windows 10/11 SDK** (`signtool.exe` for Authenticode signing)
4. **PowerShell 7** (`pwsh`)

---

## Building and Packaging

### Automated One-Step Build & Package
To compile, sign the binary, and produce the MSI installer:
```powershell
pwsh -File "Build-All.ps1"
```

### Version Bumping
To bump version across the project file and WiX installer:
```powershell
pwsh -File "Bump-Version.ps1" -NewVersion "1.1.9"
```

Generated artifacts in `Dist/`:
* `T650Bridge-Setup-v1.1.9.msi` (and `T650Bridge-Setup.msi`)
* `SHA256SUMS.txt`

---

## Command-Line Arguments

The compiled executable `T650Bridge.exe` supports the following flags:

| Flag | Description |
| :--- | :--- |
| `--silent` | Starts minimized directly to the Windows Notification Area (System Tray). (Default for autostart) |
| `--console` | Keeps the interactive diagnostics console visible for troubleshooting and real-time packet inspection. |

---

## Auto-Start Configuration

The installer configures Windows Startup via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`:
* Entry name: `"Logitech T650 Bridge"`
* Target: `"C:\Program Files\Logitech T650 PTP\Bridge\T650Bridge.exe" --silent`
* Appears in Windows Task Manager $\rightarrow$ **Startup apps** tab where you can enable or disable it.

---

## License

MIT License. See [LICENSE](LICENSE) for details.
