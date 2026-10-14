# Logitech T650 Windows Precision Touchpad Bridge

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

The **Logitech T650 Bridge** modernizes the Logitech T650 Wireless Rechargeable Touchpad on Windows 10 and Windows 11. 

While the T650 natively speaks Logitech's proprietary HID++ 2.0 protocol over the 2.4 GHz Unifying Receiver, Windows defaults to basic 2-button mouse emulation and relies on obsolete SetPoint software. This bridge unlocks the hardware's multi-touch sensor (up to 5 contact points) and connects it directly into modern Windows workflows.

---

## Unified Application Architecture

The bridge is a single, unified high-performance application featuring:

* **Flexible Integration Modes:** Configured directly in the Bridge Settings window:
  - **Automatic (Recommended):** Uses the low-latency Named Pipe (`\\.\pipe\T650VirtualPtpPipe`) if the [VirtualPTP Driver](https://github.com/brunnobb/VirtualPTP) is installed, or falls back seamlessly to the standalone gesture engine.
  - **PTP Driver:** Direct feed into Windows Touch Stack for native Windows Precision Touchpad gestures.
  - **Standalone:** High-performance direct input synthesis via `SendInput`.
* **Native UIPI Bypass (`uiAccess="true"`):** Built with `uiAccess="true"` and signed with an Authenticode certificate. When installed to `Program Files`, it grants native User Interface Privilege Isolation (UIPI) bypass, enabling smooth gestures over elevated Administrator applications (Task Manager, Registry Editor, Command Prompt) without UAC prompts.
* **Custom Gesture Shortcuts:** Fully customize 3-finger and 4-finger gestures (taps, drags up/down/left/right) with any keyboard shortcut combination.
* **Contact Debouncing & Sensitivity Tuning:** Built-in contact-stabilization buffer to eliminate contact count skew and customizable trackpad sensitivity.

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
pwsh -File "Bump-Version.ps1" -NewVersion "1.1.8"
```

Generated artifacts in `Dist/`:
* `T650Bridge-Setup-v<Version>.msi` (and `T650Bridge-Setup.msi`)
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

