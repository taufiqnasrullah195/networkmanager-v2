# TheWiseNetwork — Development Principles

This document summarizes the most important development principles for TheWiseNetwork. The authoritative coding rules remain the repository's **`AGENTS.md`** (and `CLAUDE.md`, which references it). Do not bypass them.

## Core principles

1. **Preserve upstream functionality.** Do not break or rewrite existing NETworkManager features to add new ones; extend, don't replace.
2. **Modular architecture.** No giant classes/methods, no duplicated business logic, no global mutable state, no tight UI↔business coupling.
3. **Security first.** No hard-coded credentials, no secrets in logs/commits, no unrestricted AI/command execution.
4. **Human approval for risky operations.** Mutating or destructive network actions require approval; never silently downgrade risk.
5. **Evidence before AI conclusions.** AI output distinguishes observed facts, calculated results, inferences, recommendations, and assumptions. Never present an inference as confirmed fact.
6. **No AI shell.** AI operates only through typed, policy-gated tools.
7. **Test before declaring completion.** Never claim a build/test passed unless it was actually executed.
8. **Small, logical Git commits.** Follow conventional prefixes (`feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `ci:`). Never commit secrets, `.env`, credentials, or build artifacts.
9. **Document architectural changes.** Every feature documents purpose, architecture, configuration, usage, security, limitations, and testing.
10. **Avoid unnecessary refactoring.** Make the smallest reasonable change; inspect existing implementation before modifying it.

## Where things live

- Coding rules & repo guide: [AGENTS.md](../AGENTS.md)
- Current architecture & boundaries: [THEWISENETWORK_ARCHITECTURE.md](THEWISENETWORK_ARCHITECTURE.md)
- Requirements & roadmap: [TECHNICAL-REQUIREMENTS.md](TECHNICAL-REQUIREMENTS.md), [ROADMAP.md](ROADMAP.md)

## Build & verify

```powershell
git submodule update --init --recursive
dotnet restore .\Source\NETworkManager.sln
dotnet build .\Source\NETworkManager.sln --configuration Release --property:Platform=x64
```

CI (GitHub Actions `windows-latest`) is the authoritative build gate; this authoring host is Linux and cannot build or run the WPF application.