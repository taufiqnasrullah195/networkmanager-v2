# TheWiseNetwork — Architecture

This document describes the **current** architecture of the codebase (which is NETworkManager's architecture, preserved) and defines **where** the future TheWiseNetwork functionality will live. Anything not yet implemented is explicitly marked **Planned**.

---

## 1. Existing NETworkManager architecture

| Aspect | Detail |
|---|---|
| Language / runtime | C# / .NET 10 (SDK pinned in `Source/global.json`) |
| UI | WPF + MahApps.Metro, MVVM (manual DI — no container) |
| Target | `net10.0-windows10.0.22621.0` (Windows 11, x64/arm64) |
| Size | ~720 C# files, 110 XAML views, 113 ViewModels, 12 projects |

### Solution layout (`Source/NETworkManager.sln`)

```
Source/
├── NETworkManager/               # Main WPF app: Views, ViewModels, Controls, App.xaml
├── NETworkManager.Models/        # Business logic + network engines (Ping, IPScanner, DNSLookup, ...)
├── NETworkManager.Utilities/     # Cross-cutting helpers (AssemblyManager lives in .Settings)
├── NETworkManager.Utilities.WPF/ # WPF-specific utilities
├── NETworkManager.Converters/    # WPF value converters
├── NETworkManager.Validators/    # Input validation
├── NETworkManager.Controls/      # Custom WPF controls (RDP, PuTTY, PowerShell, TigerVNC hosts)
├── NETworkManager.Localization/  # 17-language RESX strings
├── NETworkManager.Settings/      # Settings model + persistence, AssemblyManager, PolicyManager
├── NETworkManager.Profiles/      # (Encryptable) host/network profiles
├── NETworkManager.Update/        # GitHub-based update check (Octokit)
├── NETworkManager.Documentation/ # Library/ops documentation data
├── NETworkManager.Setup/         # WiX MSI installer
└── 3rdparty/                     # Dragablz submodule — DO NOT modify (maintained manually)
```

---

## 2. Application entry point

`NETworkManager.App` (`Source/NETworkManager/App.xaml` + `App.xaml.cs`):

1. Parse command line (`CommandLineManager`).
2. `PolicyManager.Load()` — system-wide policies.
3. `LocalSettingsManager.Load()` / `SettingsManager.Load()`.
4. `SettingsManager.Upgrade()` if settings version < current version.
5. `LocalizationManager` — load language, set `Strings.Culture`.
6. Single-instance mutex, background job timer, splash screen.
7. `StartupUri` → `MainWindow.xaml`.

Assembly identity/version comes from `AssemblyManager` (reads `Assembly.GetEntryAssembly()`), which also determines the **Name** used for all on-disk paths (see §6).

---

## 3. UI architecture

- **MVVM**: Views in `Source/NETworkManager/Views/*.xaml`, ViewModels in `ViewModels/`, code-behind kept minimal.
- **Base classes**: `ViewModelBase`, `PropertyChangedBase`, `RelayCommand` (in `Utilities`).
- **Theming**: MahApps.Metro resource dictionaries merged in `App.xaml`; light/dark + accent switching.
- **Window host**: `MainWindow` is a MahApps `MetroWindow` embedding application views via a `Dragablz` tab control.
- **Embedded native tools** (`NETworkManager.Controls`): RDP (AxMSTSCLib), PuTTY/PowerShell/TigerVNC — all Windows-native via `WindowsFormsHost`.

---

## 4. Core services

| Service | Location | Responsibility |
|---|---|---|
| `SettingsManager` | `NETworkManager.Settings` | Load/save XML app settings, version migration |
| `LocalSettingsManager` | `NETworkManager.Settings` | Per-user local settings (window placement, etc.) |
| `ProfileManager` | `NETworkManager.Profiles` | Host/network profiles (XML, optional AES encryption) |
| `AssemblyManager` | `NETworkManager.Settings` | Entry-assembly name/version/location |
| `PolicyManager` | `NETworkManager.Settings` | System-wide policies (enterprise lockdown) |
| `LocalizationManager` | `NETworkManager.Localization` | Language/culture resolution |
| `Updater` | `NETworkManager.Update` | Checks GitHub releases (upstream account) |

---

## 5. Network functionality

Located in `Source/NETworkManager.Models/Network/` — these are the engines the future AI layer will wrap, unchanged:

- Reachability: `Ping`, `Traceroute`
- Resolution: `DNSLookup`, `DNSServer`
- Scanning: `IPScanner`, `PortScanner`, `PortProbe`, `HostRangeHelper`
- Topology: `DiscoveryProtocol` (LLDP/CDP), `NeighborTable` (ARP/NDP), `SNMPClient`
- Interfaces: `NetworkInterface`, `NetworkInterfaceConfig`, `NetworkProfiles`
- Other: `Firewall`, `HostsFileEditor`, `BandwidthMeter`, `WakeOnLAN`, `SubnetCalculator`

---

## 6. Configuration & profile handling

All on-disk paths derive from `AssemblyManager.Current.Name` (the **assembly name**, currently `NETworkManager` — intentionally kept for backward compatibility):

- Settings: `Documents\{Name}\Settings\`
- Local settings: `LocalApplicationData\{Name}\`
- PuTTY logs / WebConsole cache: `LocalApplicationData\{Name}\...`
- Profiles: `Documents\{Name}\...` (via `ProfileManager`)

> **Compatibility invariant:** `AssemblyManager.Current.Name` (`AssemblyName` in `NETworkManager.csproj`) is **not renamed** to TheWiseNetwork. Changing it would orphan existing users' settings, encrypted profiles, and logs. Product branding is applied only to user-facing text (window/About title, assembly `Title`/`Product` metadata).

---

## 7. Existing extension points

- **New .NET projects** can be added to `Source/NETworkManager.sln` as siblings and referenced by the main app (upstream already splits logic into 11 class libraries — a clean seam).
- **MVVM**: new capabilities plug in as View + ViewModel + registration in `MainWindow`.
- **Models layer**: network engines are plain classes; they can be invoked outside the UI for tool-based access.
- **Settings upgrade** (`SettingsManager.Upgrade`) provides a versioned migration path.
- **`IAIProvider`-style seams do not yet exist** — that abstraction is introduced in Phase 2 (Planned).

---

## 8. Where future TheWiseNetwork functionality will live (Planned)

New sibling projects under `Source/` (not yet created — see `docs/TECHNICAL-REQUIREMENTS.md` §5 and `docs/ROADMAP.md`):

```
TheWiseNetwork (product)
└── Source/
    ├── NETworkManager.*          (existing NETworkManager — unmodified where possible)
    └── NETworkManager.AI         (Planned)  AI copilot engine + IAIProvider abstraction
        NETworkManager.AI.Tools   (Planned)  typed tool registry wrapping Models engines
        NETworkManager.Policy     (Planned)  risk classification + permission
        NETworkManager.CommandExecution (Planned)  controlled PowerShell/device execution
        NETworkManager.Audit      (Planned)  audit records
        NETworkManager.Knowledge  (Planned)  knowledge base / RAG
        NETworkManager.Agents     (Planned, later)  agent hosts
```

`Source/3rdparty/` remains off-limits.

---

## 9. AI integration boundary (Planned)

```
User → TheWiseNetwork UI
        → AI copilot (tool selection)
        → Policy engine (risk classification)
        → Permission check → Approval (if required)
        → Execution (wrapped diagnostic engine / command layer)
        → Result → AI analysis (evidence-labelled) → Audit
```

Rules: the AI is given a bounded typed tool list; **no unrestricted command execution**; every execution is audited; the AI must not invent tool results.

---

## 10. Agent execution boundary (Planned)

Future agents (discovery, troubleshooting, monitoring, security, configuration, documentation) execute only through the same policy-gated tool/command layer. No agent runs arbitrary code. Multi-agent architecture is **not** implemented prematurely.

---

## 11. Security boundary

- Read-only diagnostics: LOW risk (policy may auto-execute).
- Mutating actions: MEDIUM (approval normally required) to HIGH (explicit approval).
- Destructive actions: CRITICAL (blocked by default).
- Secrets: never hard-coded; config uses placeholders; stored in platform secure storage; never logged.

---

## 12. Future automation boundary (Planned)

Automation (PowerShell agent, network device agents, controlled command execution, approval workflow) is Phase 4. It is separated from the read-only diagnostic layer and always passes through risk classification + human approval.

---

## Diagrams

```
User
  |
  v
TheWiseNetwork UI (WPF, MainWindow)
  |
  v
Application / Core services
  +------------------------------+
  |                              |
  |                              |
Existing network                Future TheWiseNetwork
functionality                   extensions (Planned)
(NETworkManager.Models          |
 engines, untouched)            +----------+----------+----------+
                                |          |          |          |
                               AI        Policy    Audit     Knowledge
                                |
                         Policy Layer
                                |
                         Execution Layer
```

Boundaries marked *(Planned)* do not exist in the current codebase and must not be described as implemented.