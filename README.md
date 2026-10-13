# Logitech T650 Windows Precision Touchpad Bridge

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

The **Logitech T650 Bridge** modernizes the Logitech T650 Wireless Rechargeable Touchpad on Windows 10 and Windows 11. 

While the T650 natively speaks Logitech's proprietary HID++ 2.0 protocol over the 2.4 GHz Unifying Receiver, Windows defaults to basic 2-button mouse emulation and relies on obsolete SetPoint software. This bridge unlocks the hardware's multi-touch sensor (up to 5 contact points) and connects it directly into modern Windows workflows.

---

## Two Product Editions

The project builds two specialized editions tailored for different system environments:

### 1. Precision Touchpad (PTP) Edition
* **Target:** Systems with the [VirtualPTP Driver](https://github.com/brunnobb/VirtualPTP) installed.
* **Mechanism:** Streams multi-touch frames over a high-speed Named Pipe (`\\.\pipe\T650VirtualPtpPipe`) directly into the Windows Touch Stack.
* **Experience:** 100% native Windows Precision Touchpad experience: Configure two, three, and four-finger gestures directly in **Windows Settings $\rightarrow$ Bluetooth & devices $\rightarrow$ Touchpad**.

### 2. Standalone Admin Edition (UIPI Bypass)
* **Target:** Systems without test-signing enabled or where a virtual driver cannot be installed.
* **Mechanism:** Synthesizes Windows multi-touch gesture events directly via `SendInput`.
* **UIAccess Privilege:** Compiled with Microsoft `uiAccess="true"` and signed with an Authenticode certificate. Grants native User Interface Privilege Isolation (UIPI) bypass, enabling smooth gestures over elevated Administrator applications (Task Manager, Registry Editor, Command Prompt) without UAC prompts.

---

## Architecture & Hardware Protocol

```text
┌────────────────────────────────────────────────────────┐
│  Logitech T650 Hardware                                │
│  - 2.4 GHz Unifying RF (VID: 0x046D, PID: 0xC52B)       │
└──────────────────────────┬─────────────────────────────┘
                           │ 20-byte HID++ 2.0 Reports
┌──────────────────────────▼─────────────────────────────┐
│  T650Bridge Daemon                                     │
│  - Feature 0x6100 (Touchpad Raw XY) Activation         │
│  - Hotplug Supervisor & Wireless Power-Cycle Detection │
│  - 20-byte Packet Assembler (Contacts, Down/Up/Hover)  │
│  - System Tray Management & Diagnostics                │
└────────────┬───────────────────────────────┬───────────┘
             │ (PTP Edition)                 │ (Admin Edition)
             ▼                               ▼
┌──────────────────────────┐   ┌──────────────────────────┐
│ VirtualPTP Driver Pipe   │   │ Windows SendInput Engine │
│ (\\.\pipe\...)           │   │ (uiAccess="true" UIPI)   │
└──────────────────────────┘   └──────────────────────────┘
```

---

## Directory Structure

```text
T650Bridge/
├── T650Bridge.sln              # Visual Studio solution file
├── src/                        # C# source code and project
│   ├── T650Bridge.csproj       # Multi-flavor .NET project file
│   ├── Program.cs              # Entry point, CLI args, & main runloop
│   ├── app.ptp.manifest        # Application manifest for PTP edition
│   ├── app.admin.manifest      # Manifest with uiAccess="true"
│   ├── T650.ico                # Application & Tray icons
│   ├── Gesture/                # Standalone gesture synthesis engine
│   ├── Hidpp/                  # Logitech Unifying HID++ 2.0 protocol
│   ├── Ptp/                    # Virtual PTP driver named pipe client
│   ├── Touch/                  # Multi-touch frame decoding & assembly
│   └── UI/                     # System tray icon and balloon notifications
├── Certificates/               # Code-signing certificates for Authenticode
│   └── T650TestCert.cer        # Authenticode test certificate
├── Installer/                  # WiX Toolset v3.14 MSI installer packages
│   ├── T650Bridge.wxs          # PTP Edition WiX installer
│   ├── T650Bridge-Admin.wxs    # Standalone Admin Edition WiX installer
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
To compile both flavors, sign the binaries, and produce both MSI installers:
```powershell
pwsh -File "Build-All.ps1"
```

To build only a specific flavor:
```powershell
pwsh -File "Build-All.ps1" -Flavor Ptp
pwsh -File "Build-All.ps1" -Flavor Admin
```

### Version Bumping
To bump version across the project file and both WiX installers:
```powershell
pwsh -File "Bump-Version.ps1" -NewVersion "1.1.7"
```

Generated artifacts in `Dist/`:
* `T650Bridge-PTP-Setup-v<Version>.msi` (and `T650Bridge-PTP-Setup.msi`)
* `T650Bridge-Admin-Setup-v<Version>.msi` (and `T650Bridge-Admin-Setup.msi`)
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

Both installers configure Windows Startup via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`:
* **PTP Edition:** Registers `"Logitech T650 Bridge"`.
* **Admin Edition:** Registers `"Logitech T650 Bridge Admin"`.
* Both appear in Windows Task Manager $\rightarrow$ **Startup apps** tab where you can enable or disable them at will.

---

## License

MIT License. See [LICENSE](LICENSE) for details.
