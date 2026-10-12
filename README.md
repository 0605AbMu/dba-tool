# nfrastack DBA Control Center (`db-backup` Web UI & API)

`nfrastack/db-backup` (`dbb`) CLI vositasini brauzer orqali real vaqtda boshqarish, monitoring qilish va avtomatlashtirish uchun mo‘ljallangan **ASP.NET Core (.NET 10 Minimal API) + SignalR + CliWrap + Hangfire + EF Core (SQLite)** hamda standalone **Vue 3 (PrimeVue 4 + Tailwind CSS v4 + Pinia)** dasturi.

---

## Arxitektura

```
dba-tool/
├── bin/
│   └── dbb                     # Kompilyatsiya qilingan nfrastack/db-backup (v5.1.0, CGO_ENABLED=0) CLI binari
├── data/
│   ├── dbatool.db              # EF Core SQLite asosiy ma'lumotlar bazasi
│   ├── sample-ecommerce.db     # Test uchun tayyor SQLite ma'lumotlar bazasi (4 ta jadval, 65 ta yozuv)
│   ├── configs/
│   │   └── db-backup.yml       # Avtomatik generatsiya qilinadigan nfrastack YAML konfiguratsiyasi
│   └── backups/                # Backup artefaktlari (.sql.zst, .gz, .age) va Sidecar v2 (.json) metama'lumotlari
├── backend/                    # ASP.NET Core (.NET 10 Minimal API) + SignalR + CliWrap + Hangfire + SQLite
│   ├── Data/                   # AppDbContext va DbInitializer (avto-seed)
│   ├── Hubs/                   # DbaHub (/hubs/cli) — real-time stdout/stderr va progress streaming
│   ├── Models/                 # Entities va DTO'lar (Sidecar v2, Artifacts, Jobs, Profiles)
│   ├── Services/               # DbbCliRunnerService (CliWrap), DbbConfigGeneratorService, BackupStorageService, HangfireSchedulerService
│   └── Program.cs              # Minimal API endpointlari va Hangfire Dashboard (/hangfire)
└── frontend/                   # Standalone Vue 3 + TypeScript + Vite + PrimeVue 4 + Tailwind CSS v4 + Pinia SPA
    ├── src/
    │   ├── components/         # LiveStreamDrawer.vue (global real-time SignalR terminal drawer)
    │   ├── services/           # api.ts (REST client) va signalr.ts (@microsoft/signalr client)
    │   ├── stores/             # dbaStore.ts (Pinia store)
    │   └── views/              # 7 ta boshqaruv sahifasi (Dashboard, Jobs, Backups, Profiles, CLI Studio, Scheduler, Config)
    └── .env                    # VITE_API_BASE_URL=http://localhost:5150
```

---

## Asosiy Imkoniyatlar

1. **Real-Time CLI Streaming (`CliWrap` + `SignalR`)**:
   - `dbb` jarayonlarining `stdout` va `stderr` (`--log-format json` va `--progress`) oqimlarini `PipeTarget.ToDelegate` orqali qator-ma-qator ushlab, `/hubs/cli` SignalR hubi orqali brauzerga uzatadi.
   - Ishlayotgan har qanday `dbb` jarayonini brauzerdan turib to‘xtatish (**Cancel / SIGINT**) imkoniyati mavjud.
2. **To‘liq `nfrastack/db-backup` (`dbb`) Qo‘llab-quvvatlovi**:
   - **9 ta Database Engine**: `mysql`, `mariadb`, `postgres`, `mongo`, `mssql`, `redis`, `sqlite`, `influx`, `couch`.
   - **Barcha CLI Subcommandlar**: `dump` (`backup`), `restore`, `list`, `verify`, `prune`, `archive`, `maintain`, `scheduler`, `version`, `license`, `stats`, `help`.
   - **Sidecar v2 (`.json`) Inspector**: Har bir backup yonida yaratiladigan `<filename>.json` faylini o‘qib, `compressed_md5`, `uncompressed_md5`, `raw_size`, siqish foizi, server versiyasi va zanjir (`base`/`position`) ma'lumotlarini ko‘rsatadi.
   - **Shifrlash va Siqish**: `zstd`, `gz`, `bz`, `xz` hamda `age`, `gpg`, `openssl` shifrlash profillari.
3. **Hangfire Cron Scheduler**:
   - Standart 5-qismli Linux cron (`0 */6 * * *`, `0 3 * * *`) jadvallarini Hangfire `IRecurringJobManager` bilan sinxronlashtiradi.
   - Brauzerning o‘zidan vazifalarni **Pause**, **Resume**, **Trigger Now** qilish va ichki `/hangfire` Dashboard'ni ko‘rish mumkin.

---

## Ishga Tushirish

### 1. Backend (`http://localhost:5150`)
```bash
cd backend
dotnet run
```
- REST API: `http://localhost:5150/api/...`
- SignalR Hub: `http://localhost:5150/hubs/cli`
- Hangfire Dashboard: `http://localhost:5150/hangfire`

### 2. Frontend Standalone SPA (`http://localhost:5173`)
```bash
cd frontend
npm install
npm run dev
```
Production build tayyorlash uchun:
```bash
cd frontend
npm run build
```
(`VITE_API_BASE_URL` ni `.env` yoki `.env.production` faylida sozlash mumkin.)
