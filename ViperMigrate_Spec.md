# ViperMigrate — Technical Spec

## What It Is

Portable Windows exe that captures a user's workstation personality — browser profiles, passwords, printers, scanners, mapped drives, Outlook layout, WiFi, shortcuts, software inventory — and restores it all on a new machine. No install. No agents. No cloud dependency. Tech runs it on the old machine, runs it on the new machine, everything the user cares about comes back.

Does NOT handle user files (OneDrive or manual copy covers that) or domain join (tech handles that). Specifically targets the invisible stuff that always gets missed and causes callbacks.

---

## The Problem

Every workstation replacement follows the same pattern: tech sets up the new machine, copies files over, hands it to the user. User calls back within 48 hours because their printers are gone, bookmarks are missing, browser passwords are lost, Outlook looks different, their Excel recent files are empty, scanner doesn't work, network drives aren't mapped. Tech makes another trip to fix what should've been caught the first time. Multiply that by every tech across every client and it's a massive time sink.

---

## Stack

| Component | Technology | Why |
|-----------|-----------|-----|
| Language | C# / .NET 8 | Native Windows API access for registry, DPAPI, WMI. Single-file publish. |
| UI | WPF (Windows Presentation Foundation) | Lightweight desktop UI, no web layer needed. Clean checkboxes and progress bars. Ships inside the exe. |
| Data Export | JSON metadata + raw file copies | Migration package is a folder with JSON manifests and captured files organized by category. |
| Encryption | AES-256 via System.Security.Cryptography | Protects browser passwords and WiFi credentials in the migration package. Tech sets a password at capture time. |
| Password Decryption | Windows DPAPI (CryptUnprotectData) | Decrypts Chrome/Edge saved passwords while running under the user's session on the old machine. |
| Software Matching | Winget CLI | Matches captured software against winget repository for automated reinstall. |
| Packaging | .NET single-file publish, self-contained, ReadyToRun | One exe, no runtime dependency, portable. |

---

## Architecture

```
┌──────────────────────────────────────────────────┐
│                ViperMigrate.exe                    │
│                                                    │
│  ┌──────────────────────────────────────────────┐ │
│  │              WPF UI                           │ │
│  │  - Capture / Restore mode selection           │ │
│  │  - Category checkboxes with previews          │ │
│  │  - Progress tracking per category             │ │
│  │  - Manual action checklist at end             │ │
│  └─────────────────────┬────────────────────────┘ │
│                        │                           │
│  ┌─────────────────────▼────────────────────────┐ │
│  │           Migration Engine                    │ │
│  │                                               │ │
│  │  ┌─────────────┐  ┌────────────────────────┐ │ │
│  │  │  Capture     │  │  Restore               │ │ │
│  │  │  Pipeline    │  │  Pipeline              │ │ │
│  │  │              │  │                        │ │ │
│  │  │  Collectors  │  │  Applicators           │ │ │
│  │  │  (one per    │  │  (one per              │ │ │
│  │  │   category)  │  │   category)            │ │ │
│  │  └──────┬──────┘  └───────────┬────────────┘ │ │
│  │         │                     │               │ │
│  │         ▼                     ▼               │ │
│  │  ┌─────────────────────────────────────────┐  │ │
│  │  │        Migration Package                 │  │ │
│  │  │  (folder on network share or USB)        │  │ │
│  │  │                                          │  │ │
│  │  │  manifest.json     (master index)        │  │ │
│  │  │  software.json     (installed apps)      │  │ │
│  │  │  browsers/         (profiles, passwords) │  │ │
│  │  │  printers.json     (printer configs)     │  │ │
│  │  │  scanners.json     (scanner profiles)    │  │ │
│  │  │  drives.json       (mapped drives)       │  │ │
│  │  │  outlook/          (signatures, layout)  │  │ │
│  │  │  wifi/             (network profiles)    │  │ │
│  │  │  shortcuts/        (desktop, taskbar)    │  │ │
│  │  │  office.json       (recent/pinned files) │  │ │
│  │  │  quickaccess.json  (pinned folders)      │  │ │
│  │  │  machine.json      (reference info)      │  │ │
│  │  └─────────────────────────────────────────┘  │ │
│  └───────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────┘
```

---

## Project Structure

```
ViperMigrate/
├── ViperMigrate.sln
├── src/
│   ├── ViperMigrate.App/
│   │   ├── ViperMigrate.App.csproj
│   │   ├── Program.cs
│   │   ├── App.xaml
│   │   ├── App.xaml.cs
│   │   ├── Views/
│   │   │   ├── MainWindow.xaml              # Mode selection (Capture or Restore)
│   │   │   ├── MainWindow.xaml.cs
│   │   │   ├── CaptureView.xaml             # Capture UI — category list, destination, password, go
│   │   │   ├── CaptureView.xaml.cs
│   │   │   ├── RestoreView.xaml             # Restore UI — load package, checkboxes, preview, go
│   │   │   ├── RestoreView.xaml.cs
│   │   │   ├── ProgressView.xaml            # Running progress — per category status
│   │   │   ├── ProgressView.xaml.cs
│   │   │   ├── SummaryView.xaml             # Done — what was completed, manual action list
│   │   │   └── SummaryView.xaml.cs
│   │   ├── ViewModels/
│   │   │   ├── MainViewModel.cs
│   │   │   ├── CaptureViewModel.cs
│   │   │   ├── RestoreViewModel.cs
│   │   │   ├── ProgressViewModel.cs
│   │   │   └── SummaryViewModel.cs
│   │   ├── Converters/                      # WPF value converters for UI binding
│   │   │   └── StatusToColorConverter.cs
│   │   ├── Resources/
│   │   │   ├── Styles.xaml                  # App-wide styling
│   │   │   └── Icons/                       # App icon, category icons
│   │   └── Properties/
│   │       └── PublishProfiles/
│   │           └── portable.pubxml
│   │
│   └── ViperMigrate.Core/
│       ├── ViperMigrate.Core.csproj
│       ├── Models/
│       │   ├── MigrationPackage.cs          # Master manifest model
│       │   ├── CapturedSoftware.cs          # Software inventory item
│       │   ├── CapturedPrinter.cs           # Printer config
│       │   ├── CapturedScanner.cs           # Scanner profile
│       │   ├── CapturedDrive.cs             # Mapped drive
│       │   ├── CapturedBrowserProfile.cs    # Browser data reference
│       │   ├── CapturedWifiProfile.cs       # WiFi network
│       │   ├── CapturedShortcut.cs          # Desktop/taskbar shortcut
│       │   ├── CapturedOutlookConfig.cs     # Outlook settings reference
│       │   ├── CapturedOfficeRecent.cs      # Office recent/pinned files
│       │   ├── MachineInfo.cs               # Old machine reference data
│       │   ├── CategoryResult.cs            # Per-category capture/restore result
│       │   └── ManualAction.cs              # Item for the manual to-do list
│       │
│       ├── Capture/
│       │   ├── ICaptureCollector.cs          # Interface all collectors implement
│       │   ├── CaptureEngine.cs             # Orchestrates all collectors
│       │   ├── Collectors/
│       │   │   ├── SoftwareCollector.cs      # Registry scan + winget matching
│       │   │   ├── ChromeCollector.cs        # Chrome profile, bookmarks, passwords
│       │   │   ├── EdgeCollector.cs          # Edge profile, bookmarks, passwords
│       │   │   ├── PrinterCollector.cs       # WMI printer enumeration + config
│       │   │   ├── ScannerCollector.cs       # Scanner software detection + profiles
│       │   │   ├── DriveCollector.cs         # Mapped network drives from registry
│       │   │   ├── OutlookCollector.cs       # Signatures, layout, views, preferences
│       │   │   ├── WifiCollector.cs          # netsh export
│       │   │   ├── ShortcutCollector.cs      # Desktop, taskbar, Start Menu
│       │   │   ├── QuickAccessCollector.cs   # Pinned Quick Access folders
│       │   │   ├── OfficeRecentCollector.cs  # Recent/pinned file lists for Office apps
│       │   │   └── MachineInfoCollector.cs   # Computer name, domain, OU
│       │   └── Helpers/
│       │       ├── RegistryHelper.cs         # Safe registry read/write operations
│       │       ├── DpapiHelper.cs            # CryptUnprotectData wrapper for password decryption
│       │       ├── WingetHelper.cs           # Winget query and matching
│       │       ├── SqliteBrowserDb.cs        # Read Chrome/Edge SQLite password databases
│       │       └── PackageWriter.cs          # Writes migration package to disk with encryption
│       │
│       ├── Restore/
│       │   ├── IRestoreApplicator.cs         # Interface all applicators implement
│       │   ├── RestoreEngine.cs             # Orchestrates all applicators
│       │   ├── Applicators/
│       │   │   ├── SoftwareApplicator.cs     # Winget install + manual list generation
│       │   │   ├── ChromeApplicator.cs       # Restore Chrome profile and passwords
│       │   │   ├── EdgeApplicator.cs         # Restore Edge profile and passwords
│       │   │   ├── PrinterApplicator.cs      # Reconnect network printers, flag direct IP
│       │   │   ├── ScannerApplicator.cs      # Restore scanner software profiles
│       │   │   ├── DriveApplicator.cs        # Map network drives (skip GPO drives)
│       │   │   ├── OutlookApplicator.cs      # Restore signatures, layout, views
│       │   │   ├── WifiApplicator.cs         # netsh import
│       │   │   ├── ShortcutApplicator.cs     # Restore desktop, taskbar, Start Menu
│       │   │   ├── QuickAccessApplicator.cs  # Restore pinned folders
│       │   │   └── OfficeRecentApplicator.cs # Restore recent/pinned file lists
│       │   └── Helpers/
│       │       ├── PackageReader.cs          # Reads and decrypts migration package
│       │       ├── DpapiEncryptor.cs         # Re-encrypt passwords for new machine user context
│       │       └── PrinterDriverHelper.cs    # Check if printer driver exists on new machine
│       │
│       └── Common/
│           ├── PackageEncryption.cs          # AES-256 encrypt/decrypt for sensitive data in package
│           ├── PathResolver.cs              # Resolves user profile paths, AppData, etc.
│           ├── GpoDetector.cs               # Detects GPO-mapped drives to skip them
│           └── LogManager.cs                # Logging for troubleshooting
│
└── tests/
    └── ViperMigrate.Core.Tests/
        ├── Collectors/
        │   ├── SoftwareCollectorTests.cs
        │   ├── ChromeCollectorTests.cs
        │   ├── PrinterCollectorTests.cs
        │   ├── DriveCollectorTests.cs
        │   └── OutlookCollectorTests.cs
        ├── Applicators/
        │   ├── SoftwareApplicatorTests.cs
        │   ├── DriveApplicatorTests.cs
        │   └── WifiApplicatorTests.cs
        └── Helpers/
            ├── GpoDetectorTests.cs
            └── PackageEncryptionTests.cs
```

---

## Core Data Models

### MigrationPackage (master manifest)

```csharp
public class MigrationPackage
{
    public string Version { get; set; }               // ViperMigrate version that created this
    public DateTime CapturedAt { get; set; }
    public MachineInfo SourceMachine { get; set; }
    public string CapturedByUser { get; set; }        // Domain\Username that was logged in
    public bool IsEncrypted { get; set; }              // Whether sensitive data is encrypted
    public Dictionary<string, CategoryResult> Categories { get; set; }  // What was captured per category
}
```

### MachineInfo

```csharp
public class MachineInfo
{
    public string ComputerName { get; set; }
    public string Domain { get; set; }
    public string OuPath { get; set; }
    public string OsVersion { get; set; }             // "Windows 11 Pro 23H2"
    public string UserProfilePath { get; set; }       // "C:\Users\jsmith"
    public string Username { get; set; }
}
```

### CapturedSoftware

```csharp
public class CapturedSoftware
{
    public string Name { get; set; }                  // "Google Chrome"
    public string Version { get; set; }               // "121.0.6167.85"
    public string Publisher { get; set; }              // "Google LLC"
    public string InstallPath { get; set; }           // "C:\Program Files\Google\Chrome"
    public string UninstallString { get; set; }
    public string LicenseKey { get; set; }            // Null if not found
    public string LicenseKeySource { get; set; }      // "Registry: HKLM\SOFTWARE\..."
    public string WingetId { get; set; }              // "Google.Chrome" or null if no match
    public bool CanAutoInstall { get; set; }          // True if winget match found
}
```

### CapturedPrinter

```csharp
public class CapturedPrinter
{
    public string Name { get; set; }                  // "HP LaserJet 4050 on PRINTSVR"
    public string PortName { get; set; }              // "\\PRINTSVR\HP4050" or "IP_192.168.1.50"
    public string DriverName { get; set; }            // "HP Universal Printing PCL 6"
    public string SharePath { get; set; }             // "\\PRINTSVR\HP4050" for network printers
    public string IpAddress { get; set; }             // "192.168.1.50" for direct IP printers
    public bool IsDefault { get; set; }
    public bool IsNetworkPrinter { get; set; }        // True = shared from print server
    public bool IsDirectIp { get; set; }              // True = direct IP port
    public bool CanAutoRestore { get; set; }          // Network printers yes, direct IP maybe not
}
```

### CapturedScanner

```csharp
public class CapturedScanner
{
    public string DeviceName { get; set; }            // "Fujitsu fi-7160"
    public string SoftwareName { get; set; }          // "PaperStream Capture" or "NAPS2" or "Windows Scan"
    public string SoftwareVersion { get; set; }
    public string ConnectionType { get; set; }        // "USB", "Network"
    public string IpAddress { get; set; }             // For network scanners
    public List<ScanProfile> Profiles { get; set; }   // Captured scan profiles
    public string ProfileSourcePath { get; set; }     // Where the profile data was captured from
    public bool CanAutoRestore { get; set; }          // True if we can copy profiles back
}

public class ScanProfile
{
    public string Name { get; set; }                  // "Scan to PDF - Color"
    public string DestinationPath { get; set; }       // "\\server\scans\jsmith"
    public string FileFormat { get; set; }            // "PDF", "JPEG", "TIFF"
    public string Resolution { get; set; }            // "300 DPI"
    public string ColorMode { get; set; }             // "Color", "Grayscale", "B&W"
    public bool Duplex { get; set; }
}
```

### CapturedDrive

```csharp
public class CapturedDrive
{
    public string DriveLetter { get; set; }           // "H:"
    public string UncPath { get; set; }               // "\\fileserver\users\jsmith"
    public bool IsGpoMapped { get; set; }             // True = skip on restore, GPO handles it
    public bool IsPersistent { get; set; }
    public string Label { get; set; }                 // Drive label if set
}
```

### CapturedBrowserProfile

```csharp
public class CapturedBrowserProfile
{
    public string Browser { get; set; }               // "Chrome" or "Edge"
    public string ProfileName { get; set; }           // "Default" or "Profile 1"
    public int BookmarkCount { get; set; }
    public int PasswordCount { get; set; }
    public int ExtensionCount { get; set; }
    public List<string> ExtensionNames { get; set; }  // For the preview UI
    public bool PasswordsCaptured { get; set; }       // Whether DPAPI decryption succeeded
    public string ProfileDataPath { get; set; }       // Path in migration package
}
```

### ManualAction

```csharp
public class ManualAction
{
    public string Category { get; set; }              // "Software", "Printers", "Outlook"
    public string Description { get; set; }           // "Install QuickBooks Desktop 2024"
    public string Detail { get; set; }                // "Version 24.0.1, License: XXXX-XXXX-XXXX"
    public ManualActionPriority Priority { get; set; } // High, Medium, Low
}

public enum ManualActionPriority { High, Medium, Low }
```

---

## Capture Details — What Gets Grabbed and How

### 1. Software Inventory

**Source:** Registry  
**Locations:**
- `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*`
- `HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*`
- `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*`

**Process:**
1. Enumerate all subkeys at the above locations
2. For each: read DisplayName, DisplayVersion, Publisher, InstallLocation, UninstallString
3. Filter out system components, updates, and drivers (check SystemComponent flag, filter known noise like "Microsoft Visual C++ Redistributable")
4. For each real application, search common registry locations for license keys:
   - Software-specific known paths (maintained list for common apps like Office, Adobe, etc.)
   - Generic `ProductKey`, `SerialNumber`, `LicenseKey` value names in the software's registry hive
5. Run `winget list` and cross-reference to find winget package IDs for each captured app
6. Mark each app as CanAutoInstall (winget match found) or manual

**Output:** `software.json` — array of CapturedSoftware objects

### 2. Chrome Profile

**Source:** File system + SQLite database  
**Location:** `%LOCALAPPDATA%\Google\Chrome\User Data\Default\` (and any additional profiles like `Profile 1`, `Profile 2`)

**Process:**
1. Detect all Chrome profiles in User Data directory
2. For each profile:
   - Copy `Bookmarks` file (JSON, no encryption)
   - Copy `Preferences` file (JSON — homepage, startup pages, search engine, settings)
   - Read `Extensions` directory to capture installed extension IDs and names
   - Read `Login Data` SQLite database:
     - Open the database with System.Data.SQLite
     - Read `logins` table: origin_url, username_value, password_value (encrypted blob)
     - Read `encrypted_key` from `Local State` JSON file in the User Data root
     - Decrypt master key using DPAPI (CryptUnprotectData — works because we're running as the user)
     - Decrypt each password using the master key with AES-256-GCM
     - Store decrypted credentials in the migration package, re-encrypted with the tech's package password
   - Copy `Web Data` SQLite database (contains autofill data, addresses, payment methods — payment methods are DPAPI encrypted, same decryption process)

**Output:** `browsers/chrome/` directory in package with profile data, `browsers/chrome/passwords.enc` (encrypted credential export)

### 3. Edge Profile

**Source:** Same structure as Chrome (Edge is Chromium-based)  
**Location:** `%LOCALAPPDATA%\Microsoft\Edge\User Data\Default\`

**Process:** Identical to Chrome. Same DPAPI decryption, same SQLite structure, same file locations relative to the profile root.

**Output:** `browsers/edge/` directory in package

### 4. Printers

**Source:** WMI + Registry  
**WMI Query:** `SELECT * FROM Win32_Printer`

**Process:**
1. Query WMI for all installed printers
2. For each printer capture: Name, PortName, DriverName, ShareName, Default status
3. Determine printer type:
   - If PortName starts with `\\` → network shared printer, mark IsNetworkPrinter, extract SharePath
   - If PortName contains IP address or matches `IP_*` or `TCP/IP Port` → direct IP, mark IsDirectIp, extract IP
   - Otherwise → local/USB printer
4. For network printers: mark CanAutoRestore = true (just reconnect to share)
5. For direct IP printers: check if driver exists in Windows driver store on new machine during restore
6. Query `HKCU\Printers\Connections` for per-user network printer connections

**Output:** `printers.json` — array of CapturedPrinter objects

### 5. Scanners

**Source:** File system + Registry (varies by scanner software)

**Process:**
1. Detect installed scanning software by checking for known applications:
   - **NAPS2**: Profiles at `%APPDATA%\NAPS2\profiles.xml`
   - **HP Smart**: Profiles in `%LOCALAPPDATA%\Packages\AD2F1837.HPSmart*\LocalState`
   - **HP Scan**: Profiles at `%PROGRAMDATA%\HP\HP Scan\Profiles`
   - **Canon IJ Scan Utility**: Settings in registry `HKCU\SOFTWARE\Canon\CanoScan`
   - **Epson Scan 2**: Profiles in `%PROGRAMDATA%\EPSON\Epson Scan 2\`
   - **Brother iPrint&Scan**: Settings in `%APPDATA%\Brother\`
   - **Fujitsu PaperStream/ScanSnap**: Profiles in `%PROGRAMDATA%\PFU\ScanSnap\` or `%APPDATA%\PFU\`
   - **Windows Scan/Fax and Scan**: Settings in registry `HKCU\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows Messaging Subsystem`
2. For each detected scanner software:
   - Copy profile/config files
   - Parse profile data where possible to extract human-readable settings (destination, format, resolution, color, duplex)
3. For USB scanners: capture WMI `Win32_PnPEntity` data for USB-connected imaging devices to record make/model
4. For network scanners: capture IP and any scan-to-folder destinations (note: destinations stored ON the scanner itself aren't capturable from the PC)

**Output:** `scanners.json` + `scanners/` directory with copied profile files

### 6. Mapped Network Drives

**Source:** Registry  
**Location:** `HKCU\Network\*`

**Process:**
1. Enumerate subkeys under `HKCU\Network` — each subkey name is a drive letter
2. For each: read RemotePath (UNC), ProviderName, UserName
3. Detect GPO-mapped drives:
   - Check Group Policy registry at `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Group Policy\Drive Maps`
   - Also run `gpresult /scope:user /v` and parse drive mapping section
   - Any drive that appears in GPO results gets flagged IsGpoMapped = true
4. Only drives with IsGpoMapped = false need to be restored

**Output:** `drives.json` — array of CapturedDrive objects

### 7. Outlook Configuration

**Source:** File system + Registry

**Signatures:**
- Location: `%APPDATA%\Microsoft\Signatures\`
- Copy entire directory (HTML, RTF, TXT files plus image subfolders per signature)

**View Settings and Layout:**
- Location: `%LOCALAPPDATA%\Microsoft\Outlook\`
- Copy `*.xml` files (custom views, folder views)
- Copy `*.srs` file (Send/Receive settings)
- Copy `*.NK2` file if present (legacy autocomplete cache)

**Preferences:**
- Registry: `HKCU\SOFTWARE\Microsoft\Office\16.0\Outlook\Preferences`
- Export reading pane position, preview behavior, notification settings, font preferences

**Ribbon and Toolbar:**
- Location: `%LOCALAPPDATA%\Microsoft\Office\` — `*.officeUI` files
- Copy Outlook-specific officeUI file

**RoamCache (Autocomplete):**
- Location: `%LOCALAPPDATA%\Microsoft\Outlook\RoamCache\`
- Copy Stream_Autocomplete*.dat files (for on-prem Exchange or POP/IMAP accounts — M365 roams this server-side)

**PST Files:**
- Registry: `HKCU\SOFTWARE\Microsoft\Office\16.0\Outlook\Profiles\*`
- Scan profile keys for entries pointing to `.pst` files
- Record file paths (don't copy the PSTs — they can be huge and file copy is the tech's job)
- Add to manual action list: "Reattach PST files at these locations"

**Account List:**
- Registry: Same profile keys contain account configuration
- Extract email addresses and account types for reference
- Add to manual action list: "Set up these email accounts via autodiscover"

**Output:** `outlook/` directory with signatures and config files, `outlook.json` with metadata and manual actions

### 8. WiFi Profiles

**Source:** Windows netsh utility

**Process:**
1. Run `netsh wlan show profiles` to list all saved networks
2. For each profile, run `netsh wlan export profile name="{name}" key=clear folder="{tempdir}"`
3. This produces XML files containing SSID, security type, encryption type, and plaintext password
4. Move XML files to migration package
5. Encrypt the WiFi XML files with the package password (they contain plaintext passwords)

**Restore:**
1. Decrypt XML files
2. For each: run `netsh wlan add profile filename="{xmlpath}" user=all`

**Output:** `wifi/` directory with encrypted XML profile files

### 9. Desktop Shortcuts

**Source:** File system

**Locations:**
- User desktop: `%USERPROFILE%\Desktop\*.lnk`
- Public desktop: `C:\Users\Public\Desktop\*.lnk` (skip these — they come from software installs and will return when software is reinstalled)

**Process:**
1. Copy all `.lnk` files from user's Desktop
2. For each shortcut, read the target path using `IShellLink` COM interface
3. Record target path so the restore can verify if the target exists

**Restore:**
1. Copy `.lnk` files back to new user's Desktop
2. Flag any shortcuts whose targets don't exist yet (software not yet installed) — these still get placed, they'll work once the software is installed

**Output:** `shortcuts/desktop/` directory with .lnk files

### 10. Taskbar Pins

**Source:** File system + Registry

**Location:** `%APPDATA%\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\`

**Process:**
1. Copy all files from the TaskBar pin directory
2. Note: Windows 11 handles taskbar pins differently than Windows 10
   - Windows 10: `.lnk` files in the above path
   - Windows 11: Registry at `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband` contains serialized pin data
3. Detect OS version and capture accordingly
4. On restore, detect target OS version and apply appropriately
5. If migrating between Win 10 and Win 11, flag as manual action since the formats aren't compatible

**Output:** `shortcuts/taskbar/` directory + `taskbar.json` metadata

### 11. Quick Access Pinned Folders

**Source:** File system

**Location:** `%APPDATA%\Microsoft\Windows\Recent\AutomaticDestinations\` and `%APPDATA%\Microsoft\Windows\Recent\CustomDestinations\`

**Process:**
1. The pinned Quick Access items are stored in jump list files
2. Read `f01b4d95cf55d32a.automaticDestinations-ms` — this is the File Explorer jump list that contains pinned folders
3. Parse the OLE compound file to extract pinned folder paths
4. Store the paths as a simple list

**Restore:**
1. Use Shell COM objects to pin each folder path back to Quick Access
2. Verify the paths still exist (network shares should still be there, local paths may differ)

**Output:** `quickaccess.json` — array of folder paths

### 12. Office Recent and Pinned Files

**Source:** Registry

**Locations:**
- Excel: `HKCU\SOFTWARE\Microsoft\Office\16.0\Excel\File MRU` and `HKCU\SOFTWARE\Microsoft\Office\16.0\Excel\User MRU\LiveId_*\File MRU`
- Word: Same pattern under `\Word\`
- PowerPoint: Same pattern under `\PowerPoint\`

**Process:**
1. Export the File MRU registry keys for Excel, Word, and PowerPoint
2. Each entry contains the file path and a pinned/unpinned flag
3. Capture both the recent list and which items are pinned
4. Also capture `Place MRU` keys (recent folder locations in Save/Open dialogs)

**Restore:**
1. Import registry keys on new machine
2. Recent and pinned lists will appear in each Office app
3. Files at network paths will work immediately
4. Files at local paths that no longer exist will just show as unavailable (harmless)

**Output:** `office.json` — recent and pinned file lists per Office app

### 13. Machine Info (Reference Only)

**Source:** WMI + Registry + System

**Captures:**
- Computer name: `Environment.MachineName`
- Domain: `System.DirectoryServices.ActiveDirectory.Domain.GetComputerDomain()`
- OU path: LDAP query for the computer object
- OS version: `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion` — ProductName, DisplayVersion, CurrentBuild
- Username: `Environment.UserName`
- User profile path: `Environment.GetFolderPath(SpecialFolder.UserProfile)`

**Output:** `machine.json` — MachineInfo object (reference for the tech, not used in restore)

---

## Migration Package Structure

```
ViperMigrate_jsmith_2026-02-12/
├── manifest.json                          # Master index — what was captured, versions, metadata
├── machine.json                           # Source machine reference info
├── software.json                          # Installed software inventory
├── printers.json                          # Printer configurations
├── scanners.json                          # Scanner software and profiles
├── scanners/                              # Scanner profile data files
│   ├── naps2_profiles.xml
│   └── hp_scan_profiles/
├── drives.json                            # Mapped network drives
├── browsers/
│   ├── chrome/
│   │   ├── Default/
│   │   │   ├── Bookmarks                  # Chrome bookmarks JSON
│   │   │   ├── Preferences                # Chrome settings
│   │   │   └── extensions.json            # Extension list
│   │   └── passwords.enc                  # Encrypted password export
│   └── edge/
│       ├── Default/
│       │   ├── Bookmarks
│       │   ├── Preferences
│       │   └── extensions.json
│       └── passwords.enc
├── outlook/
│   ├── Signatures/                        # Full signatures directory
│   │   ├── MySignature.htm
│   │   ├── MySignature.rtf
│   │   ├── MySignature.txt
│   │   └── MySignature_files/
│   ├── views/                             # View XML files
│   ├── roamcache/                         # Autocomplete cache
│   ├── officeui/                          # Ribbon customizations
│   ├── preferences.reg                    # Outlook preference registry export
│   └── outlook.json                       # Account list, PST paths, manual actions
├── wifi/
│   ├── profiles.enc                       # Encrypted WiFi profile XMLs
├── shortcuts/
│   ├── desktop/                           # Desktop .lnk files
│   └── taskbar/                           # Taskbar pin data
├── quickaccess.json                       # Pinned Quick Access folders
└── office.json                            # Recent/pinned files for Excel, Word, PowerPoint
```

---

## Restore Logic Details

### Software Installation Order

1. First pass: Install all winget-available apps silently (`winget install --id {id} --silent --accept-package-agreements --accept-source-agreements`)
2. Run installs sequentially to avoid conflicts 
3. Track success/failure for each
4. Generate manual action list for everything that couldn't auto-install, sorted by priority:
   - High priority: Apps the user had open recently or that are in startup (check captured startup items)
   - Medium priority: Everything else that was installed
   - Low priority: Rarely-used tools, utilities

### Browser Password Restore

1. Decrypt the password export file using the tech's package password
2. Chrome/Edge must be installed first and run at least once to create the profile directory
3. Close the browser completely
4. Open the new machine's `Login Data` SQLite database
5. Read the new machine's encryption key from `Local State`
6. Re-encrypt each password using the NEW machine's DPAPI context
7. Insert the credential rows into the `Login Data` database
8. When the user opens the browser, passwords are there

### Printer Restoration

1. Network shared printers (\\server\printer): Run `Add-Printer -ConnectionName "{SharePath}"` — this auto-downloads the driver from the print server
2. Direct IP printers:
   - Check if the driver exists on the new machine: `Get-PrinterDriver -Name "{DriverName}"`
   - If driver exists: Create TCP/IP port and add printer
   - If driver doesn't exist: Add to manual action list with the driver name, IP, and port info
3. Set default printer if one was marked as default

### Drive Mapping

1. Filter out any drives flagged as IsGpoMapped
2. For remaining drives: `net use {DriveLetter}: {UncPath} /persistent:yes`
3. If the net use fails (permission denied, path not found): add to manual action list

### Scanner Profile Restoration

1. Check if the same scanning software is installed on the new machine (may have been auto-installed via winget)
2. If installed: copy profile files back to the same paths
3. If not installed: add to manual action list — "Install {SoftwareName} and restore scan profiles from package"

---

## UI Flow

### Main Window
```
┌─────────────────────────────────────────┐
│          🐍 ViperMigrate                │
│                                         │
│   ┌─────────────┐  ┌─────────────┐     │
│   │             │  │             │     │
│   │   CAPTURE   │  │   RESTORE   │     │
│   │             │  │             │     │
│   │  Grab the   │  │  Apply to   │     │
│   │  old machine│  │  new machine│     │
│   │             │  │             │     │
│   └─────────────┘  └─────────────┘     │
│                                         │
│                          v1.0.0         │
└─────────────────────────────────────────┘
```

### Capture View
```
┌─────────────────────────────────────────┐
│  CAPTURE — Old Machine                  │
│─────────────────────────────────────────│
│                                         │
│  What to capture:                       │
│  ☑ Installed Software                   │
│  ☑ Chrome (bookmarks, passwords, ext.)  │
│  ☑ Edge (bookmarks, passwords, ext.)    │
│  ☑ Printers                             │
│  ☑ Scanners                             │
│  ☑ Mapped Network Drives                │
│  ☑ Outlook (signatures, layout, views)  │
│  ☑ WiFi Networks                        │
│  ☑ Desktop Shortcuts                    │
│  ☑ Taskbar Pins                         │
│  ☑ Quick Access Folders                 │
│  ☑ Office Recent/Pinned Files           │
│                                         │
│  Save package to:                       │
│  [ \\server\migrations\jsmith    ] [📁] │
│                                         │
│  Package password (protects passwords): │
│  [ ••••••••••                    ]      │
│                                         │
│  [ ▶ START CAPTURE ]                    │
└─────────────────────────────────────────┘
```

### Progress View
```
┌─────────────────────────────────────────┐
│  CAPTURING...                           │
│─────────────────────────────────────────│
│                                         │
│  ✅ Machine Info              done      │
│  ✅ Installed Software        47 apps   │
│  ✅ Chrome                    312 pw    │
│  ✅ Edge                      no data   │
│  ⏳ Printers                  scanning  │
│  ⬜ Scanners                            │
│  ⬜ Mapped Drives                       │
│  ⬜ Outlook                             │
│  ⬜ WiFi                                │
│  ⬜ Shortcuts                           │
│  ⬜ Quick Access                        │
│  ⬜ Office Recent                       │
│                                         │
│  ████████████░░░░░░░░░░  52%           │
└─────────────────────────────────────────┘
```

### Restore View
```
┌─────────────────────────────────────────┐
│  RESTORE — New Machine                  │
│─────────────────────────────────────────│
│                                         │
│  Package: \\server\migrations\jsmith    │
│  Captured: 2026-02-12 from WORKSTATION5 │
│  User: DOMAIN\jsmith                    │
│                                         │
│  What to restore:                       │
│  ☑ Software (32 via winget, 15 manual)  │
│  ☑ Chrome (312 passwords, 89 bookmarks) │
│  ☐ Edge (no data captured)              │
│  ☑ Printers (3 network, 1 direct IP)   │
│  ☑ Scanners (NAPS2 — 4 profiles)       │
│  ☑ Network Drives (2 manual, 3 GPO)    │
│  ☑ Outlook (2 signatures, layout)      │
│  ☑ WiFi (5 networks)                   │
│  ☑ Desktop Shortcuts (12 shortcuts)    │
│  ☑ Taskbar Pins (8 pins)              │
│  ☑ Quick Access (4 folders)            │
│  ☑ Office Recent (Excel: 15, Word: 8)  │
│                                         │
│  Package password:                      │
│  [ ••••••••••                    ]      │
│                                         │
│  [ ▶ START RESTORE ]                    │
└─────────────────────────────────────────┘
```

### Summary View
```
┌─────────────────────────────────────────┐
│  RESTORE COMPLETE ✅                    │
│─────────────────────────────────────────│
│                                         │
│  Restored automatically:                │
│  ✅ 30 apps installed via winget        │
│  ✅ 312 Chrome passwords restored       │
│  ✅ 89 Chrome bookmarks restored        │
│  ✅ 3 network printers connected        │
│  ✅ 4 NAPS2 scan profiles restored      │
│  ✅ 2 network drives mapped             │
│  ✅ Outlook signatures restored         │
│  ✅ Outlook layout and views restored   │
│  ✅ 5 WiFi networks imported            │
│  ✅ 12 desktop shortcuts placed         │
│  ✅ 8 taskbar pins restored             │
│  ✅ 4 Quick Access folders pinned       │
│  ✅ Office recent files restored        │
│                                         │
│  ⚠️ Manual actions needed (7):          │
│  ─────────────────────────────────────  │
│  🔴 Install QuickBooks Desktop 2024     │
│     License: XXXX-XXXX-XXXX-XXXX       │
│  🔴 Install MedicalApp Pro v3.2         │
│  🔴 Install Fujitsu ScanSnap Home      │
│     Then restore profiles from package  │
│  🟡 HP LaserJet direct IP — install     │
│     driver "HP Universal PCL6" for      │
│     printer at 192.168.1.50             │
│  🟡 Set up Outlook accounts:            │
│     - jsmith@clientdomain.com           │
│     - jsmith@personalemail.com          │
│  🟡 Reattach PST files:                 │
│     - D:\Archives\2023.pst             │
│     - D:\Archives\OldMail.pst          │
│  🟢 2 winget installs failed — retry    │
│     or install manually:                │
│     - Notepad++ v8.6 (winget timeout)   │
│     - VLC v3.0.20 (winget timeout)      │
│                                         │
│  [ 📋 Copy Manual List ]  [ ✖ Close ]  │
└─────────────────────────────────────────┘
```

---

## Build Order

### Phase 1 — Project scaffold + core
1. Create .NET solution with App and Core projects
2. Define all C# models
3. Set up ICaptureCollector and IRestoreApplicator interfaces
4. Build PackageWriter and PackageReader with AES-256 encryption
5. Build PathResolver and RegistryHelper
6. Build basic WPF shell with view navigation (Main → Capture → Progress → Summary)
7. Verify: exe launches, views navigate, package can be written and read back

### Phase 2 — Software capture and restore
8. Build SoftwareCollector (registry scan + license key extraction)
9. Build WingetHelper (match software against winget repository)
10. Build SoftwareApplicator (winget install + manual list)
11. Unit tests with mock registry data

### Phase 3 — Browser profiles and passwords
12. Build SqliteBrowserDb helper (read Chrome/Edge SQLite databases)
13. Build DpapiHelper (CryptUnprotectData wrapper)
14. Build ChromeCollector (bookmarks, extensions, password decryption)
15. Build EdgeCollector (same pattern)
16. Build ChromeApplicator (restore bookmarks, re-encrypt and inject passwords)
17. Build EdgeApplicator
18. Unit tests — test with real Chrome profile on dev machine

### Phase 4 — Printers and scanners
19. Build PrinterCollector (WMI enumeration, type detection)
20. Build PrinterApplicator (network reconnect, direct IP handling)
21. Build ScannerCollector (detect software, capture profiles)
22. Build ScannerApplicator (restore profiles)
23. Test with real printers on test network

### Phase 5 — Drives, WiFi, Outlook
24. Build DriveCollector with GpoDetector
25. Build DriveApplicator (net use for manual drives only)
26. Build WifiCollector (netsh export)
27. Build WifiApplicator (netsh import)
28. Build OutlookCollector (signatures, views, preferences, ribbon, PST paths, account list)
29. Build OutlookApplicator (file copy + registry import)
30. Test Outlook restore with real Outlook profile

### Phase 6 — Shortcuts, Quick Access, Office recent
31. Build ShortcutCollector (desktop + taskbar, OS version aware)
32. Build ShortcutApplicator
33. Build QuickAccessCollector (parse jump list files)
34. Build QuickAccessApplicator (Shell COM pin)
35. Build OfficeRecentCollector (registry MRU export)
36. Build OfficeRecentApplicator (registry import)

### Phase 7 — Full UI
37. Build CaptureView with category checkboxes, destination picker, password field
38. Build RestoreView with package loader, preview counts, selective restore
39. Build ProgressView with per-category status updates
40. Build SummaryView with completed items and manual action list with copy button
41. Style everything — clean, professional, Viper branding

### Phase 8 — Integration testing + packaging
42. Full capture → restore test on real workstations (different hardware, same domain)
43. Test cross-OS: Windows 10 → Windows 11 migration
44. Test edge cases: no Chrome installed, no printers, no Outlook, no mapped drives
45. Test large profile: hundreds of passwords, dozens of printers, many WiFi networks
46. Configure single-file self-contained publish
47. Test portable exe on clean machine
48. Test from USB drive

### Phase 9 — Polish + ship
49. App icon and Viper branding
50. Error handling — graceful failures per category (don't stop entire capture if one thing fails)
51. Logging — write a log file to the migration package for troubleshooting
52. README and quick start guide
53. GitHub repo
54. Dark Pattern content

---

## Edge Cases and Considerations

**User not logged in on old machine:** The tool MUST run under the user's Windows session for DPAPI password decryption to work. If the old machine is available but the user isn't logged in, the tech needs to log in as the user or the password capture will fail. Everything else (printers, drives, shortcuts) works from any admin session since those paths are deterministic from the username.

**Multiple browser profiles:** Some users have multiple Chrome/Edge profiles (personal + work). The tool captures all profiles found and lets the tech select which to restore.

**Outlook not configured yet on new machine:** The Outlook applicator restores signatures, views, and preferences to the correct file paths. When Outlook is later configured with the email account, it picks up these settings automatically.

**Winget not available:** Some older Windows 10 builds don't have winget. The tool checks for winget availability at the start of restore. If not present, all software goes to the manual list. The tool could optionally install winget first via the App Installer package.

**Domain user profile not yet created on new machine:** The restore must run AFTER the user has logged into the new machine at least once so their profile directory exists. The tool checks for this and warns the tech if the target user profile doesn't exist yet.

**Printer driver dependencies:** Network shared printers auto-download drivers from the print server. Direct IP printers need the driver preinstalled. The tool can't install printer drivers automatically (they're not in winget), so these always go to the manual list with exact driver name for the tech to download.

**Package portability:** The migration package is just a folder. It works on USB, network share, or local disk. No special file system requirements. The JSON files are human-readable for troubleshooting.

**Security:** The encrypted portions of the package (browser passwords, WiFi passwords) use AES-256-CBC with PBKDF2 key derivation from the tech's password. The rest of the package (bookmarks, shortcuts, printer configs) is unencrypted since it's not sensitive. If the tech loses the package password, encrypted data is unrecoverable — passwords would need to be reset manually.

---

## What This Tool Does NOT Do

- **Does not copy user files.** No Desktop/Documents/Photos/Downloads sync. That's OneDrive or manual copy.
- **Does not join the domain.** Tech handles that.
- **Does not install proprietary software.** Only winget-available apps. Everything else goes to the manual list.
- **Does not configure email accounts.** Lists them for the tech, autodiscover handles the setup.
- **Does not copy PST files.** Records their locations for the tech to handle.
- **Does not require internet** except for winget installs during restore. Capture is fully offline.
- **Does not modify the old machine.** Capture is read-only. Nothing is changed on the source.
- **Does not store credentials to disk unencrypted.** All passwords are AES-256 encrypted in the package.
