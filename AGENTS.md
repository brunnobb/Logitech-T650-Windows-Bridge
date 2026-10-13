# Logitech T650 Windows Bridge - AI Agent Guide

> **Project:** Logitech T650 Windows Precision Touchpad Bridge  
> **Repository:** `git@github.com:brunnobb/Logitech-T650-Windows-Bridge.git`  
> **Target Framework:** .NET 10.0 (`net10.0-windows`)  
> **Language:** C# 13 / .NET SDK

---

## 1. Project Overview & Architecture

The **Logitech T650 Bridge** is a high-performance Windows daemon that unlocks multi-touch hardware streaming from the Logitech T650 Wireless Touchpad over the 2.4 GHz Unifying Receiver (`VID 0x046D`, `PID 0xC52B`).

It reverse-engineers Logitech's proprietary HID++ 2.0 protocol and translates raw 20-byte multi-touch reports into modern Windows gestures across two specialized deployment flavors.

```text
┌────────────────────────────────────────────────────────┐
│  Logitech T650 Hardware                                │
│  - 2.4 GHz Unifying RF (VID: 0x046D, PID: 0xC52B)       │
└──────────────────────────┬─────────────────────────────┘
                           │ 20-byte HID++ 2.0 Reports
┌──────────────────────────▼─────────────────────────────┐
│  T650Bridge Daemon (C# .NET 10)                        │
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

## 2. Product Editions / Flavors

### Flavor 1: Precision Touchpad Client (`Flavor=Ptp`)
* **Purpose:** For systems with the [VirtualPTP Driver](https://github.com/brunnobb/VirtualPTP) installed.
* **Mechanism:** Connects to the virtual driver's named pipe (`\\.\pipe\T650VirtualPtpPipe`) and streams raw `PtpReport` contact frames into the Windows Touch Stack.
* **Result:** Unlocks the native Windows Precision Touchpad settings page (**Settings $\rightarrow$ Bluetooth & devices $\rightarrow$ Touchpad**).
* **Manifest:** Uses `src/app.ptp.manifest`.

### Flavor 2: Standalone Admin Edition (`Flavor=Admin`)
* **Purpose:** For standalone environments where a virtual driver cannot be installed (e.g. secure corporate machines without test-signing enabled).
* **Mechanism:** Directly synthesizes mouse and gesture events using Windows `SendInput`.
* **Microsoft UIAccess Privilege:** Compiled with `uiAccess="true"` (`src/app.admin.manifest`) and digitally signed with an Authenticode certificate. Grants native User Interface Privilege Isolation (UIPI) bypass, allowing gestures to interact with Administrator windows (Task Manager, Regedit, elevated cmd/pwsh) without UAC prompts.
* **Manifest:** Uses `src/app.admin.manifest`.

---

## 3. Protocol Reverse-Engineering (HID++ 2.0)

1. **Receiver Enumeration:** Uses `HidSharp` to discover the Logitech Unifying Receiver (VID `0x046D`, PID `0xC52B`).
2. **Feature Discovery:**
   * Queries Root Feature `0x0000` to find the Feature Index for Feature `0x6100` (`TOUCHPAD_RAW_XY`).
   * On device index `0x01` (the T650), this is typically Feature Index `0x0F`.
3. **Activation Command:**
   * Sends `[0x10, 0x01, feature_index, 0x00, 0x05, 0x00, 0x00]` (`setRawReportState(0x05)`).
   * Switches device out of basic 2-button mouse mode into raw 14-bit streaming mode.
4. **Packet Decoding:**
   * Parses 20-byte reports (`TouchFrame.cs` / `FrameAssembler.cs`).
   * Extracts contact count (up to 5 fingers), finger IDs, status bits (down/up/hover), and coordinates (`0..2832` $\times$ `0..2364`).
5. **Wireless Resilience:**
   * Heartbeat daemon keeps connection alive.
   * Detects wireless power-cycles (`0x00` disconnection event) and automatically re-executes the unlock handshake on reconnect.

---

## 4. Directory Structure

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
│   ├── T650.ico                # Application icon
│   └── Build-Bridge-Installer.ps1 # WiX compiler script
├── Build-All.ps1               # Complete compile, sign, and package script
├── Bump-Version.ps1            # Automated SemVer bumper
└── Dist/                       # Output MSI installers and SHA-256 hashes
```

---

## 5. Versioning & Release Guidelines (AI Agent Reference)

When an AI prompt requests a release, version bump, or installer update for T650Bridge:

### 1. Versioning Standard
* **Git Version (SemVer):** `MAJOR.MINOR.PATCH` (e.g. `1.1.7`).
* **Assembly & WiX Version (4-Part):** `MAJOR.MINOR.PATCH.BUILD` (e.g. `1.1.7.0`).
* **Windows Installer Rule:** MSI only evaluates `MAJOR.MINOR.PATCH` for upgrade checks. Every release must increment at least `PATCH` or `MINOR`.

### 2. Canonical Version File Locations
When changing versions, synchronize these files:

| File | Target Element / Property | Example Value |
| :--- | :--- | :--- |
| [`src/T650Bridge.csproj`](file:///c:/Workspace/T650-Windows-PTP/T650Bridge/src/T650Bridge.csproj) | `<Version>`, `<AssemblyVersion>`, `<FileVersion>` | `<Version>1.1.7</Version>`<br>`<AssemblyVersion>1.1.7.0</AssemblyVersion>` |
| [`Installer/T650Bridge.wxs`](file:///c:/Workspace/T650-Windows-PTP/T650Bridge/Installer/T650Bridge.wxs) | `<Product Version="..." ...>` | `Version="1.1.7.0"` |
| [`Installer/T650Bridge-Admin.wxs`](file:///c:/Workspace/T650-Windows-PTP/T650Bridge/Installer/T650Bridge-Admin.wxs) | `<Product Version="..." ...>` | `Version="1.1.7.0"` |

### 3. Strict WiX Rules
1. **Dynamic Product Id:** Always keep `Product Id="*"` so WiX generates a new `ProductCode` GUID on every release.
2. **Never change UpgradeCodes:**
   * Bridge PTP Edition: `355A1666-3125-4EC3-82E0-7F4B02825E0B`
   * Bridge Admin Edition: `8E3B6899-28BC-4537-88EB-8B9075ACD421`
   * Changing `UpgradeCode` breaks seamless automatic upgrades.
3. **MajorUpgrade:** Keep `<MajorUpgrade DowngradeErrorMessage="A newer version of [ProductName] is already installed." />`.

### 4. Automated Version Bump Command
```powershell
pwsh -File "Bump-Version.ps1" -NewVersion "1.1.7"
```

---

## 6. Build, Signing, and Packaging Tutorial

### Step 6.1: Build .NET Solution
```powershell
dotnet build "T650Bridge.sln" -c Release
```

### Step 6.2: Publish & Sign Both Flavors
```powershell
# 1. Publish PTP Flavor:
dotnet publish "src/T650Bridge.csproj" -c Release -p:Flavor=Ptp -r win-x64 --self-contained false -o "Installer/bin/publish-ptp"

# 2. Publish Admin Flavor:
dotnet publish "src/T650Bridge.csproj" -c Release -p:Flavor=Admin -r win-x64 --self-contained false -o "Installer/bin/publish-admin"

# 3. Authenticode sign binaries (required for uiAccess="true"):
$signtool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"
$thumbprint = "737386A00937ED1A64A90628A02C8850D1FA5A40"
& $signtool sign /sha1 $thumbprint /fd SHA256 /v "Installer/bin/publish-ptp/T650Bridge.exe"
& $signtool sign /sha1 $thumbprint /fd SHA256 /v "Installer/bin/publish-admin/T650Bridge.exe"
```

### Step 6.3: Package WiX MSI Installers
```powershell
pwsh -File "Installer\Build-Bridge-Installer.ps1" -Version "1.1.7"
```

Output artifacts in `Dist/`:
* `T650Bridge-PTP-Setup-v<Version>.msi` (and `T650Bridge-PTP-Setup.msi`)
* `T650Bridge-Admin-Setup-v<Version>.msi` (and `T650Bridge-Admin-Setup.msi`)
* `SHA256SUMS.txt`

---

## 7. Runtime & Diagnostics

### Command-Line Arguments
* `--silent`: Starts minimized directly to the System Tray without opening a console window. Used by Windows Startup.
* `--console`: Keeps the diagnostics console window open with real-time packet hex dump and gesture state logging.

### Startup Registry Verification
* **PTP Edition:**
  * Registry Key: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
  * Entry Name: `"Logitech T650 Bridge"`
  * Target: `"C:\Program Files\Logitech T650 PTP\Bridge\T650Bridge.exe" --silent`
* **Admin Edition:**
  * Registry Key: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
  * Entry Name: `"Logitech T650 Bridge Admin"`
  * Target: `"C:\Program Files\Logitech T650 PTP\Bridge-Admin\T650Bridge.exe" --silent`
  * Enabled with `uiAccess="true"`, granting silent elevated privileges at login without UAC prompts.
