# Logitech T650 Windows Bridge - AI Agent Guide

> **Project:** Logitech T650 Windows Precision Touchpad Bridge  
> **Repository:** `git@github.com:brunnobb/Logitech-T650-Windows-Bridge.git`  
> **Target Framework:** .NET 10.0 (`net10.0-windows`)  
> **Language:** C# 13 / .NET SDK

---

## 1. Project Overview & Architecture

The **Logitech T650 Bridge** is a high-performance Windows daemon that unlocks multi-touch hardware streaming from the Logitech T650 Wireless Touchpad over the 2.4 GHz Unifying Receiver (`VID 0x046D`, `PID 0xC52B`).

It reverse-engineers Logitech's proprietary HID++ 2.0 protocol and translates raw 20-byte multi-touch reports into modern Windows gestures or injects them directly into the Virtual PTP driver.

```text
┌────────────────────────────────────────────────────────┐
│  Logitech T650 Hardware                                │
│  - 2.4 GHz Unifying RF (VID: 0x046D, PID: 0xC52B)       │
└──────────────────────────┬─────────────────────────────┘
                           │ 20-byte HID++ 2.0 Reports
┌──────────────────────────▼─────────────────────────────┐
│  T650Bridge Daemon (C# .NET 10)                        │
│  - Feature 0x6100 (Touchpad Raw XY) Activation         │
│  - Multi-touch landing debouncer & confidence filter   │
│  - Disambiguated tap-to-drag engine                    │
│  - Settings & Diagnostic Log GUI Window                │
│  - Customizable 3/4-finger shortcuts & sensitivity     │
└────────────┬───────────────────────────────┬───────────┘
             │ (PTP Driver Mode)             │ (Standalone Mode)
             ▼                               ▼
┌──────────────────────────┐   ┌──────────────────────────┐
│ VirtualPTP Driver IOCTL  │   │ Windows SendInput Engine │
│ (Microsoft PTP TLC)      │   │ (C# Precision Gestures)  │
└──────────────────────────┘   └──────────────────────────┘
```

---

## 2. Directory Structure

```text
T650Bridge/
├── T650Bridge.sln              # Visual Studio solution file
├── src/                        # C# source code and project
│   ├── T650Bridge.csproj       # Unified .NET project file
│   ├── Program.cs              # Entry point, CLI args, & main runloop
│   ├── app.manifest            # Application manifest
│   ├── T650.ico                # Application & Tray icons
│   ├── Config/                 # Per-user configuration (BridgeSettings.cs)
│   ├── Gesture/                # Precision gesture engine & ShortcutExecutor
│   ├── Hidpp/                  # Logitech Unifying HID++ 2.0 protocol
│   ├── Ptp/                    # Virtual PTP driver IOCTL client
│   ├── Touch/                  # Multi-touch landing debouncer & frame decoding
│   └── UI/                     # Settings window, TrayIconManager, and logging
├── Certificates/               # Code-signing certificates for Authenticode
│   └── T650TestCert.cer        # Authenticode test certificate
├── Installer/                  # WiX Toolset v3.14 MSI installer packages
│   ├── T650Bridge.wxs          # WiX installer
│   ├── License.rtf             # Distribution license
│   ├── T650.ico                # Application icon
│   └── Build-Bridge-Installer.ps1 # WiX compiler script
├── Build-All.ps1               # Complete compile, sign, and package script
├── Bump-Version.ps1            # Automated SemVer bumper
└── Dist/                       # Output MSI installers and SHA-256 hashes
```

---

## 3. Versioning & Release Guidelines (AI Agent Reference)

When an AI prompt requests a release, version bump, or installer update for T650Bridge:

### 1. Versioning Standard
* **Git Version (SemVer):** `MAJOR.MINOR.PATCH` (e.g. `1.1.8`).
* **Assembly & WiX Version (4-Part):** `MAJOR.MINOR.PATCH.BUILD` (e.g. `1.1.8.0`).
* **Windows Installer Rule:** MSI only evaluates `MAJOR.MINOR.PATCH` for upgrade checks. Every release must increment at least `PATCH` or `MINOR`.

### 2. Canonical Version File Locations
When changing versions, synchronize these files:

| File | Target Element / Property | Example Value |
| :--- | :--- | :--- |
| [`src/T650Bridge.csproj`](file:///c:/Workspace/T650-Windows-PTP/T650Bridge/src/T650Bridge.csproj) | `<Version>`, `<AssemblyVersion>`, `<FileVersion>` | `<Version>1.1.8</Version>`<br>`<AssemblyVersion>1.1.8.0</AssemblyVersion>` |
| [`Installer/T650Bridge.wxs`](file:///c:/Workspace/T650-Windows-PTP/T650Bridge/Installer/T650Bridge.wxs) | `<Product Version="..." ...>` | `Version="1.1.8.0"` |

### 3. Strict WiX Rules
1. **Dynamic Product Id:** Always keep `Product Id="*"` so WiX generates a new `ProductCode` GUID on every release.
2. **Never change UpgradeCodes:**
   * Bridge UpgradeCode: `355A1666-3125-4EC3-82E0-7F4B02825E0B`
   * Changing `UpgradeCode` breaks seamless automatic upgrades.
3. **MajorUpgrade:** Keep `<MajorUpgrade DowngradeErrorMessage="A newer version of [ProductName] is already installed." />`.

### 4. Automated Version Bump Command
```powershell
pwsh -File "Bump-Version.ps1" -NewVersion "1.1.8"
```

---

## 4. Build, Signing, and Packaging Tutorial

### Step 4.1: Build .NET Solution
```powershell
dotnet build "T650Bridge.sln" -c Release
```

### Step 4.2: Package WiX MSI Installer
```powershell
pwsh -File "Installer\Build-Bridge-Installer.ps1" -Version "1.1.8"
```

Output artifacts in `Dist/`:
* `T650Bridge-Setup-v<Version>.msi` (and `T650Bridge-Setup.msi`)
* `SHA256SUMS.txt`

---

## 5. Runtime & Diagnostics

### Command-Line Arguments
* `--silent`: Starts minimized directly to the System Tray without opening the settings window. Used by Windows Startup.
* `--console`: Keeps the diagnostics console window open with real-time packet hex dump and gesture state logging.
* `--standalone`: Force Standalone software gesture engine.
* `--ptp`: Force Virtual PTP Driver injection mode.
* `--auto`: Automatic mode (connects to PTP driver if present, falls back to standalone).

### Startup Registry Verification
* Registry Key: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
* Entry Name: `"Logitech T650 Bridge"`
* Target: `"C:\Program Files\Logitech T650 PTP\Bridge\T650Bridge.exe" --silent`
* Visible in Windows Task Manager $\rightarrow$ **Startup apps** tab as **"Logitech T650 Bridge"**.
