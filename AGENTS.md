# AGENTS.md — AI Context & Architecture Guide (`dba-tool`)

> **Purpose**: Read this file FIRST before scanning the codebase. It contains the complete architectural map, API/SignalR contracts, `nfrastack/db-backup` (`dbb`) CLI/YAML quirks, and invariants so you do NOT need to read every file.

---

## 1. Core Architectural Invariants (MUST FOLLOW)

1. **Zero Hardcoded Dev-PC Paths**:
   - NEVER hardcode `/home/...`, `localhost:5150`, or machine-specific paths in `backend/` or `frontend/`.
   - **Backend paths** are resolved exclusively through `IDbaPathProvider` (`backend/Services/DbaPathResolver.cs`), which checks `DBA_DATA_DIR` / `DBA_BINARY_PATH` env vars $\rightarrow$ `DbaTool:*` config $\rightarrow$ `AppContext.BaseDirectory`.
   - **Frontend API/SignalR URLs** default to relative origin `""` (`/api/...`, `/hubs/cli`, `/hangfire`) in `frontend/src/services/api.ts`. Vite dev proxy (`frontend/vite.config.ts`) forwards `/api`, `/hubs`, `/hangfire` during `npm run dev`.
2. **Self-Contained Publish Bundle (`dotnet publish`)**:
   - Running `dotnet publish backend/DbaTool.Api.csproj -c Release -o ./publish` automatically:
     1. Runs `npm run build` in `frontend/` via the `BundleFrontendSpaOnPublish` MSBuild target;
     2. Copies `frontend/dist/**/*` into `publish/wwwroot/` (served via `UseStaticFiles()` + `MapFallbackToFile("index.html")`);
     3. Bundles `bin/dbb` into `publish/bin/dbb` (`CopyToPublishDirectory="Always"`).
3. **Enterprise Light Theme Only (`frontend/`)**:
   - The UI is strictly **Light Theme** (`bg-slate-50`, `bg-white`, `border-slate-200`, `text-slate-900`, PrimeVue Aura `darkModeSelector: false`). Do NOT introduce dark-mode-only classes (`bg-slate-950`, `text-white` on light surfaces).
4. **Hangfire Scheduler (NOT Quartz.NET)**:
   - Background cron scheduling uses **Hangfire** (`Hangfire.Core`, `Hangfire.AspNetCore`, `Hangfire.MemoryStorage`) with standard 5-field Linux cron expressions (`0 2 * * *`). Dashboard is mounted at `/hangfire` (with `X-Frame-Options` stripped for iframe embedding).

---

## 2. Directory & File Map (Where Everything Lives)

```text
dba-tool/
├── bin/dbb                                         # Statically compiled nfrastack/db-backup v5.1.0 (100% Supporter features unlocked)
├── backend/                                        # .NET 10 Minimal API + SignalR + CliWrap + Hangfire + EF Core SQLite
│   ├── DbaTool.Api.csproj                          # Packages + MSBuild target bundling ../bin/dbb and ../frontend/dist -> wwwroot
│   ├── Program.cs                                  # 15-line declarative bootstrap
│   ├── Abstractions/IServices.cs                   # Interfaces: IDbaPathProvider, IDbbConfigGeneratorService, IDbbCliRunnerService, IBackupStorageService, IHangfireSchedulerService
│   ├── Common/
│   │   ├── Exceptions.cs                           # EntityNotFoundException (404), DomainValidationException (400), ResourceInUseException (400)
│   │   └── SlugHelper.cs                           # SlugHelper.Sanitize(raw, fallback)
│   ├── Infrastructure/GlobalExceptionHandler.cs    # IExceptionHandler (RFC 7807 + { error, message, statusCode }) & Hangfire Auth Filter
│   ├── Extensions/
│   │   ├── ServiceCollectionExtensions.cs          # DI registration (EF Core, SignalR, Hangfire, Services)
│   │   └── WebApplicationExtensions.cs             # Pipeline middleware + MapDbaToolEndpoints() + InitializeDbaToolAsync()
│   ├── Models/
│   │   ├── Entities.cs                             # EF Entities: ConnectionProfile, StorageProfile, EncryptionProfile, BackupJob, JobExecution
│   │   └── Dtos.cs                                 # Request/Response DTOs, SidecarMetadataDto (v2), BackupArtifactDto, SystemInfoDto, DashboardSummaryDto
│   ├── Data/
│   │   ├── AppDbContext.cs                         # SQLite DbContext
│   │   ├── DbInitializer.cs                        # Creates data/{backups,configs,tmp,state} & seeds 'default-local' StorageProfile
│   │   └── Configurations/DbaEntityConfigurations.cs # IEntityTypeConfiguration<T> for all 5 entities
│   ├── Hubs/DbaHub.cs                              # SignalR Hub at /hubs/cli (CancelExecution, JoinExecutionGroup)
│   ├── Services/
│   │   ├── DbaPathResolver.cs                      # Dynamic path resolution (DataDir, BackupsDir, ConfigFilePath, ResolveBinaryPath)
│   │   ├── DbbConfigGeneratorService.cs            # Generates data/configs/db-backup.yml from DB entities using YamlDotNet
│   │   ├── DbbCliRunnerService.cs                  # CliWrap runner streaming stdout/stderr to SignalR + live CancellationTokenSource map
│   │   ├── BackupStorageService.cs                 # Scans storage paths + parses <filename>.json Sidecar v2 metadata
│   │   └── HangfireSchedulerService.cs             # Syncs BackupJob 5-field cron/intervals with Hangfire IRecurringJobManager
│   └── Endpoints/                                  # Modular Minimal API route groups:
│       ├── DashboardEndpoints.cs                   # GET /api/dashboard/summary, GET/POST /api/system/{info,version-check,license,stats}
│       ├── ConnectionEndpoints.cs                  # CRUD /api/connections + POST /api/connections/{id}/test
│       ├── StorageProfileEndpoints.cs              # CRUD /api/storage-profiles
│       ├── EncryptionProfileEndpoints.cs           # CRUD /api/encryption-profiles
│       ├── BackupJobEndpoints.cs                   # CRUD /api/jobs + POST /api/jobs/{id}/{run,maintain,toggle}
│       ├── BackupArtifactEndpoints.cs              # GET/DELETE /api/backups, GET sidecar/download, POST verify/restore/prune/archive
│       ├── ExecutionEndpoints.cs                   # GET /api/executions, POST /api/executions/{id}/cancel, POST /api/cli/execute
│       ├── SchedulerEndpoints.cs                   # GET /api/scheduler/jobs, POST /api/scheduler/jobs/{jobId}/{pause,resume,trigger}
│       └── ConfigEndpoints.cs                      # GET/PUT /api/config/yaml, POST /api/config/regenerate
└── frontend/                                       # Vue 3 + TypeScript + Vite + PrimeVue 4 + Tailwind CSS v4 + Pinia + @microsoft/signalr
    ├── src/
    │   ├── types/models.ts                         # Complete TS interfaces matching Backend DTOs & SignalR payloads
    │   ├── services/api.ts                         # Fetch wrapper for all /api/* endpoints
    │   ├── services/signalr.ts                     # SignalR connection to /hubs/cli
    │   ├── stores/dbaStore.ts                      # Pinia store + ENGINE_METADATA (9 engines) + formatters
    │   ├── components/LiveStreamDrawer.vue         # Slide-over real-time CLI output & progress drawer
    │   └── views/
    │       ├── DashboardView.vue                   # /          — KPIs, quick actions, recent runs, Hangfire preview, engine matrix
    │       ├── JobsView.vue                        # /jobs      — Job CRUD (4-tab modal covering 100% dbb job options) + Run/DryRun/Maintain
    │       ├── BackupsView.vue                     # /backups   — Artifact table, Sidecar v2 modal, Verify, Restore Wizard, Prune, Archive
    │       ├── ProfilesView.vue                    # /profiles  — 3 tabs: Connections (9 engines), Storage (5 backends), Encryption (3 types)
    │       ├── CliStudioView.vue                   # /cli       — Visual dbb flag builder (12 subcommands) + SignalR live terminal + history
    │       ├── SchedulerView.vue                   # /scheduler — Hangfire recurring jobs table + embedded /hangfire dashboard tab
    │       └── ConfigStudioView.vue                # /config    — Live db-backup.yml editor + License/Version-Check/Stats inspector
```

---

## 3. `nfrastack/db-backup` (`dbb`) Critical CLI & YAML Rules

When modifying `DbbConfigGeneratorService.cs` or `DbbCliRunnerService.cs`, keep these `dbb` internals in mind:

1. **Stream Routing (`stderr` vs `stdout`)**:
   - `dbb` writes ALL structured log lines (`--log-format json`) and non-TTY progress events (`status="progress"`) to **`stderr`**.
   - `dbb` writes command output for `list`, `verify`, `maintain`, `version`, `license`, `stats` to **`stdout`**.
   - `DbbCliRunnerService` pipes both `StandardOutputPipe` and `StandardErrorPipe` via `PipeTarget.ToDelegate` and broadcasts both over SignalR (`CliOutput` event).
2. **YAML Connection Resolution Quirk**:
   - In `dbb`'s `config.go` (`resolveJob`), top-level `job.connection` is ignored; connections in `profiles.connections.<conn>` are ONLY resolved through `profiles.databases.<db_prof>.connection` when a job sets `database: <db_prof>`.
   - Therefore, `DbbConfigGeneratorService` always generates a `profiles.databases.<job.Name>-db` entry with `connection: <conn.Name>` and sets `database: <job.Name>-db` on the job.
3. **SQLite Path Handling**:
   - For `sqlite`, `dbb` checks `db` name before `host`. If `DatabaseNames` is `"main"` or `"ALL"`, `DbbConfigGeneratorService` places `conn.Host` (the `.db` file path) into `profiles.databases.<job>-db.include: [conn.Host]`.
   - Keep default `backup.filename` omitted for SQLite unless customized, so `dbb` generates `sqlite-<db>--<strategy>-<timestamp>.sql.<ext>` which matches `dbb`'s `IsBackupFile()` regex parser for `list`/`verify`/`restore`/`prune`.
4. **Standalone Maintenance Jobs (`backup: unset`)**:
   - In `dbb`, a job in `db-backup.yml` that only runs maintenance requires `backup: unset`. When `BackupJob.StandaloneMaintenanceOnly == true`, `DbbConfigGeneratorService` emits `backup: "unset"` and `DbbCliRunnerService.RunJobBackupAsync` delegates to `dbb maintain <job.Name>`.
5. **Sidecar v2 (`.json`)**:
   - Every backup with `checksum != none` creates `<filename>.json` (`schema_version: 2`, `format: "dbbackup.sidecar"`). Checksums are stored inside `<filename>.json` under `checksums.compressed_md5` and `checksums.uncompressed_md5` (no separate `.md5` file is written).

---

## 4. SignalR Hub Contract (`/hubs/cli`)

- **Server $\rightarrow$ Client Events**:
  - `ExecutionStarted`: `{ executionId, jobId, jobName, commandType, cliCommand, startedAt, trigger }`
  - `CliOutput`: `{ executionId, jobId, jobName, commandType, stream: "stdout"|"stderr", line, parsedJson, timestamp }`
  - `ExecutionProgress`: `{ executionId, jobId, bytes, rate, elapsed, table, step }`
  - `ExecutionCompleted`: `{ executionId, jobId, jobName, commandType, status, exitCode, durationMs, outputFile, outputSize, finishedAt }`
  - `SchedulerUpdated`: `{ jobId, jobName, nextRunAt, lastRunAt, lastStatus }`
- **Client $\rightarrow$ Server Hub Methods**:
  - `CancelExecution(int executionId)`
  - `JoinExecutionGroup(int executionId)`

---

## 5. Build, Dev & Publish Commands

- **Build Backend**: `dotnet build backend/DbaTool.Api.csproj`
- **Build Frontend**: `cd frontend && npm run build`
- **Full Production Publish (API + `bin/dbb` + `wwwroot/`)**:
  ```bash
  dotnet publish backend/DbaTool.Api.csproj -c Release -o ./publish
  ```
- **Schema Changes**: The SQLite DB (`data/dbatool.db`) uses `EnsureCreatedAsync()`. If you add columns to `Models/Entities.cs`, delete `data/dbatool.db` (or add a migration) before restarting.
