---
trigger: always_on
description: Core architecture map and invariants for the dba-tool (nfrastack/db-backup Web UI & .NET 10 API) project
---

# `dba-tool` Project Context (Auto-Loaded Rule)

Before exploring the repository in a new session, read [AGENTS.md](file:///home/abdumannon/Desktop/work/dba-tool/AGENTS.md) for the complete file map, API/SignalR contracts, and `nfrastack/db-backup` (`dbb`) CLI/YAML specifications.

## Quick Summary
- **Binary (`bin/dbb`)**: Statically compiled `nfrastack/db-backup` v5.1.0 with 100% of Supporter Edition features (`incremental`, `differential`, `archive`, `encryption`, `storage`, `retention`, `maintenance`, `schedule`, `restore`, `notifications`) unlocked.
- **Backend (`backend/`)**: Clean Architecture **ASP.NET Core (.NET 10 Minimal API) + SignalR (`/hubs/cli`) + CliWrap + Hangfire (`/hangfire`) + EF Core (SQLite) + YamlDotNet**.
  - Entrypoint: `backend/Program.cs` (15 lines) -> `Extensions/ServiceCollectionExtensions.cs` & `Extensions/WebApplicationExtensions.cs`.
  - Endpoints: Modular `MapGroup` classes in `backend/Endpoints/*Endpoints.cs`.
  - Services: `DbaPathResolver`, `DbbConfigGeneratorService`, `DbbCliRunnerService`, `BackupStorageService`, `HangfireSchedulerService` implementing interfaces in `backend/Abstractions/IServices.cs`.
- **Frontend (`frontend/`)**: Standalone **Vue 3 + TypeScript + Vite + PrimeVue 4 + Tailwind CSS v4 + Pinia + `@microsoft/signalr`** in **Enterprise Light Theme** (`darkModeSelector: false`).
- **Portability & Publish**:
  - Never hardcode machine-specific paths or URLs. Use `IDbaPathProvider` on the backend and relative `""` origin in `frontend/src/services/api.ts`.
  - `dotnet publish backend/DbaTool.Api.csproj -c Release -o ./publish` automatically builds the Vue 3 SPA into `publish/wwwroot/` and bundles `bin/dbb` into `publish/bin/dbb`.
