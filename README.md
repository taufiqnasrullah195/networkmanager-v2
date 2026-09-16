# TheWiseNetwork

> **TheWiseNetwork** is an AI-ready network administration and diagnostics platform built on top of the [NETworkManager](https://github.com/BornToBeRoot/NETworkManager) project.

TheWiseNetwork preserves the stability, reliability, and feature set of NETworkManager while intending to add a *controlled* AI layer and advanced network intelligence.

---

## Upstream vs. TheWiseNetwork

### UPSTREAM — NETworkManager

- Project: [BornToBeRoot/NETworkManager](https://github.com/BornToBeRoot/NETworkManager)
- Description: A powerful open-source tool for managing networks and troubleshooting network problems.
- License: [GNU GPL v3](LICENSE)

NETworkManager is the mature, enterprise-ready desktop foundation this project builds upon. It provides a unified interface for network tools including Remote Desktop (RDP), PuTTY (SSH/Telnet/Serial), PowerShell, TigerVNC (VNC), WiFi Analyzer, IP Scanner, Port Scanner, Ping Monitor, Traceroute, DNS Lookup, LLDP/CDP Capture, and many more.

### TheWiseNetwork

TheWiseNetwork builds upon the existing network engineering functionality and is intended to add:

- **AI-assisted diagnostics**
- **Network troubleshooting**
- **Automation**
- **Network intelligence**
- **Monitoring**
- **Knowledge assistance**
- **Enterprise controls**

> **Status note:** AI, automation, monitoring, and knowledge-assistance features are **Planned / Roadmap** items and are **not yet implemented**. The current codebase is a clean fork of NETworkManager with product branding and the architectural foundation for those future features. See [docs/ROADMAP.md](docs/ROADMAP.md).

---

## Architecture & Development

- [Technical requirements & architecture](docs/TECHNICAL-REQUIREMENTS.md)
- [Current architecture & boundaries](docs/THEWISENETWORK_ARCHITECTURE.md)
- [Development principles](docs/DEVELOPMENT.md)
- [Roadmap](docs/ROADMAP.md)

---

## Building

TheWiseNetwork is a .NET 10 WPF application targeting **Windows (x64 / arm64)**.

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (version pinned in `Source/global.json`), Visual Studio 2022 / Rider with the .NET desktop workloads.

```powershell
git submodule update --init --recursive
dotnet restore .\Source\NETworkManager.sln
dotnet build .\Source\NETworkManager.sln --configuration Release
```

CI builds run on GitHub Actions (`windows-latest`) — see `.github/workflows/build.yml`.

---

## License & Attribution

TheWiseNetwork is licensed under the **GNU General Public License v3** ([LICENSE](LICENSE)), inheriting the license of the upstream NETworkManager project.

- Original project: [NETworkManager](https://github.com/BornToBeRoot/NETworkManager) by [BornToBeRoot](https://github.com/BornToBeRoot)
- Library licenses: `Source/NETworkManager.Documentation/Licenses/`

All upstream copyright notices, license text, and attribution are retained.