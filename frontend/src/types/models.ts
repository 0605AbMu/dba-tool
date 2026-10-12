export type DbEngineType =
  | 'mysql'
  | 'mariadb'
  | 'postgres'
  | 'mongo'
  | 'mssql'
  | 'redis'
  | 'sqlite'
  | 'influx'
  | 'couch'

export interface ConnectionProfile {
  id: number
  name: string
  type: DbEngineType | string
  host: string
  port: number
  username?: string | null
  password?: string | null
  authSource?: string | null
  influxVersion: number
  influxMode?: string | null
  tlsEnabled: boolean
  tlsCaFile?: string | null
  tlsCertFile?: string | null
  tlsKeyFile?: string | null
  tlsVerify: boolean
  tlsVersion?: string | null
  connectivityMethod: string
  timeoutSeconds: number
  retryIntervalSeconds?: number
  createdAt?: string
  updatedAt?: string
}

export interface StorageProfile {
  id: number
  name: string
  backend: 'filesystem' | 's3' | 'webdav' | 'azure' | 'gcs' | string
  path: string
  bucket?: string | null
  region?: string | null
  endpoint?: string | null
  keyId?: string | null
  keySecret?: string | null
  account?: string | null
  key?: string | null
  webDavUrl?: string | null
  webDavUser?: string | null
  webDavPass?: string | null
  fileMode: string
  dirMode: string
  ownerUser?: string | null
  ownerGroup?: string | null
  tlsEnabled?: boolean
  tlsCaFile?: string | null
  tlsCertFile?: string | null
  tlsKeyFile?: string | null
  tlsVerify?: boolean
  tlsVersion?: string | null
  isDefault: boolean
  createdAt?: string
}

export interface EncryptionProfile {
  id: number
  name: string
  type: 'none' | 'age' | 'gpg' | 'openssl' | string
  recipients?: string | null
  passphrase?: string | null
  createdAt?: string
}

export interface BackupJob {
  id: number
  name: string
  description?: string | null
  isEnabled: boolean
  connectionProfileId: number
  connectionProfile?: ConnectionProfile | null
  databaseNames: string
  excludeDatabases?: string | null
  splitDb: boolean
  tablesInclude?: string | null
  tablesExclude?: string | null
  tablesSchemaOnly?: string | null
  schemaOnly: boolean
  mySqlRoutines: boolean
  mySqlEvents: boolean
  mySqlTriggers: boolean
  mySqlViews: boolean
  mySqlRawBlobs?: boolean
  postgresGlobals: boolean
  strategy: 'full' | 'incremental' | 'differential' | string
  filenameTemplate: string
  createLatest?: boolean
  fullEvery?: number
  fullAfter?: string | null
  compressionType: 'zstd' | 'gz' | 'bz' | 'xz' | 'none' | string
  compressionLevel: number
  compressionThreads: number
  compressionRsyncable: boolean
  checksum: 'md5' | 'sha1' | 'none' | string
  storageProfileId: number
  storageProfile?: StorageProfile | null
  encryptionProfileId?: number | null
  encryptionProfile?: EncryptionProfile | null
  archiveEnabled?: boolean
  archiveStorageProfileId?: number | null
  archivePath?: string | null
  archiveLast?: number
  archiveWithin?: string | null
  archiveRetentionWithin?: string | null
  scheduleCron?: string | null
  scheduleIntervalMinutes: number
  scheduleTime?: string | null
  scheduleBegin?: string | null
  scheduleStart?: string | null
  scheduleRecurring?: boolean
  scheduleDays?: string | null
  scheduleExcludeDays?: string | null
  blackoutBegin?: string | null
  blackoutEnd?: string | null
  blackoutDays?: string | null
  retentionLast: number
  retentionWithin?: string | null
  retentionHourly: number
  retentionDaily: number
  retentionWeekly: number
  retentionMonthly: number
  retentionYearly: number
  standaloneMaintenanceOnly?: boolean
  maintenanceScheduleCron?: string | null
  maintCheckTables: boolean
  maintOptimize: boolean
  maintVacuum: boolean
  maintReindex: boolean
  maintAnalyze: boolean
  maintCompact: boolean
  maintMemoryPurge: boolean
  preHooks?: string | null
  postHooks?: string | null
  logLevel: string
  logPath?: string | null
  timingsEnabled?: boolean
  colourEnabled?: boolean
  sizeFormat?: string | null
  progressEnabled: boolean
  lastRunAt?: string | null
  lastStatus: string
  nextRunAt?: string | null
  createdAt?: string
  updatedAt?: string
}

export interface JobExecution {
  id: number
  jobId?: number | null
  jobName: string
  commandType: string
  trigger: string
  cliCommand: string
  status: 'running' | 'completed' | 'failed' | 'cancelled' | string
  exitCode?: number | null
  startedAt: string
  finishedAt?: string | null
  durationMs?: number | null
  outputFile?: string | null
  outputSize?: number | null
  isDryRun: boolean
  logOutput?: string
  currentStage?: string | null
  errorSummary?: string | null
  errorDetails?: string | null
}

export interface SidecarJob {
  name?: string
  schedule?: string
}

export interface SidecarEncryption {
  type?: string
  recipients?: string[]
  passphrase?: boolean
}

export interface SidecarMetadata {
  format?: string
  schema_version?: number
  tool?: {
    name?: string
    version?: string
    edition?: string
  }
  job?: string | SidecarJob
  trigger?: string
  session_id?: string
  run_id?: string
  status?: string
  duration_ms?: number
  raw_size?: number
  filename?: string
  strategy?: string
  protocol?: string
  schema_only?: boolean
  tables?: {
    include?: string[]
    exclude?: string[]
    schema_only?: string[]
  }
  base?: string
  position?: Record<string, unknown>
  type?: string
  db?: string
  host?: string
  server?: {
    engine?: string
    version?: string
    major?: number
    minor?: number
    flavor?: string
  }
  timestamp?: string
  checksums?: {
    compressed_md5?: string
    compressed_sha1?: string
    uncompressed_md5?: string
    uncompressed_sha1?: string
  }
  size?: number
  compress?: string
  compression_level?: number
  chain_depth?: number
  encryption?: string | SidecarEncryption
  [key: string]: unknown
}

export interface BackupArtifact {
  filename: string
  fullPath?: string
  storageProfileId?: number
  storageProfileName?: string
  size: number
  rawSize?: number | null
  createdAt: string
  engine?: string
  database?: string
  host?: string
  strategy?: string
  compression?: string
  encryption?: string
  checksumStatus?: string
  hasSidecar?: boolean
  durationMs?: number | null
  schemaOnly?: boolean
  jobName?: string
  sidecar?: SidecarMetadata | null
}

export interface SchedulerJobInfo {
  jobId: number
  jobName: string
  isEnabled: boolean
  scheduleCron?: string | null
  scheduleIntervalMinutes?: number
  quartzCron?: string | null
  triggerState?: string
  isPaused?: boolean
  lastRunAt?: string | null
  lastStatus?: string
  nextRunAt?: string | null
  connectionName?: string
  engineType?: string
}

export interface DashboardSummary {
  totalBackups: number
  totalStorageBytes: number
  totalJobs: number
  activeJobs: number
  totalConnections: number
  totalExecutions: number
  successfulExecutions: number
  failedExecutions: number
  runningExecutions: number
  successRate: number
  binaryVersion?: string
  binaryEdition?: string
  recentExecutions: JobExecution[]
  upcomingJobs: SchedulerJobInfo[]
}

export interface EngineSupportInfo {
  engine: string
  displayName?: string
  defaultPort?: number
  strategies?: string[]
  maintenanceOps?: string[]
  supportsSplitDb?: boolean
  supportsSchemaOnly?: boolean
  description?: string
}

export interface SystemInfo {
  binaryFound: boolean
  binaryPath: string
  version: string
  edition: string
  goVersion?: string
  platform?: string
  buildDate?: string
  gitCommit?: string
  rawVersionJson?: Record<string, unknown> | null
  maintainOpsOutput?: string
  configFilePath?: string
  backupsDirectory?: string
  supportedEngines: EngineSupportInfo[]
}

export interface VerifyBackupRequest {
  filename: string
  storageProfileId?: number
  passphrase?: string
  identity?: string
}

export interface RestoreBackupRequest {
  filename: string
  storageProfileId?: number
  connectionProfileId: number
  targetDatabase?: string
  createDb?: boolean
  dryRun?: boolean
  passphrase?: string
  identity?: string
  base?: string
  compress?: string
  encryption?: string
  profile?: string
  authSource?: string
}

export interface PruneBackupsRequest {
  storageProfileId?: number
  last?: number
  within?: string
  daily?: number
  weekly?: number
  monthly?: number
  yearly?: number
  dryRun?: boolean
}

export interface ArchiveBackupsRequest {
  srcStorageProfileId?: number
  dstStorageProfileId?: number
  dstPath?: string
  last?: number
  within?: string
  dryRun?: boolean
}

export interface CustomCliRequest {
  subcommand: string
  args: string[]
  configPath?: string
  dryRun?: boolean
  logLevel?: string
  logFormat?: string
  progress?: boolean
}

/* SignalR Event Payloads */
export interface ExecutionStartedEvent {
  executionId: number
  jobId?: number | null
  jobName: string
  commandType: string
  cliCommand: string
  startedAt: string
  trigger: string
}

export interface CliOutputEvent {
  executionId: number
  jobId?: number | null
  jobName: string
  commandType: string
  stream: 'stdout' | 'stderr' | string
  line: string
  parsedJson?: Record<string, unknown> | null
  timestamp: string
}

export interface ExecutionProgressEvent {
  executionId: number
  jobId?: number | null
  bytes?: number
  rate?: number | string
  elapsed?: string | number
  table?: string
  step?: string
}

export interface ProcessErrorDiagnostic {
  stage: string
  title: string
  summary: string
  technicalDetails?: string
  actionableSteps: string[]
  suggestedActionUrl?: string
}

export interface ExecutionStateChangedEvent {
  executionId: number
  jobId?: number | null
  jobName: string
  commandType: string
  currentStage: string // INIT | CONNECT | EXTRACT | COMPRESS | ENCRYPT | STORAGE | RETENTION | NOTIFY | COMPLETED | FAILED
  stageTitle: string
  stageDescription: string
  progressPercent?: number
  bytesProcessed?: number
  throughput?: string
  elapsed?: string
  currentDatabase?: string
  currentTable?: string
  isError: boolean
  diagnostic?: ProcessErrorDiagnostic | null
}

export interface ExecutionCompletedEvent {
  executionId: number
  jobId?: number | null
  jobName: string
  commandType: string
  status: string
  exitCode?: number | null
  durationMs?: number | null
  outputFile?: string | null
  outputSize?: number | null
  finishedAt: string
  currentStage?: string
  errorSummary?: string
  diagnostic?: ProcessErrorDiagnostic | null
}

export interface SchedulerUpdatedEvent {
  jobId: number
  jobName: string
  nextRunAt?: string | null
  lastRunAt?: string | null
  lastStatus?: string
}

export interface NotificationProfile {
  id: number
  name: string
  channel: 'telegram' | 'slack' | 'discord' | 'webhook' | 'email' | string
  isEnabled: boolean
  notifyOnSuccess: boolean
  notifyOnFailure: boolean
  botToken?: string | null
  chatId?: string | null
  webhookUrl?: string | null
  smtpHost?: string | null
  smtpPort?: number
  smtpUsername?: string | null
  smtpPassword?: string | null
  smtpUseSsl?: boolean
  emailFrom?: string | null
  emailTo?: string | null
  createdAt?: string
}

export interface BackupPreset {
  id: string
  name: string
  badge: string
  badgeClass?: string
  description: string
  icon: string
  values: Partial<BackupJob>
}

