import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { api } from '@/services/api'
import { signalrService, type SignalRConnectionState } from '@/services/signalr'
import type {
  BackupArtifact,
  BackupJob,
  CliOutputEvent,
  ConnectionProfile,
  DashboardSummary,
  EncryptionProfile,
  ExecutionProgressEvent,
  JobExecution,
  SchedulerJobInfo,
  StorageProfile,
  SystemInfo,
  NotificationProfile,
  ExecutionStateChangedEvent,
  ProcessErrorDiagnostic,
} from '@/types/models'

export interface ToastMessage {
  id: number
  severity: 'success' | 'info' | 'warn' | 'error'
  summary: string
  detail?: string
}

export const ENGINE_METADATA: Record<
  string,
  { label: string; defaultPort: number; color: string; badgeClass: string; icon: string }
> = {
  mysql: {
    label: 'MySQL',
    defaultPort: 3306,
    color: '#00758f',
    badgeClass: 'bg-sky-50 text-sky-700 border-sky-200',
    icon: 'pi pi-database',
  },
  mariadb: {
    label: 'MariaDB',
    defaultPort: 3306,
    color: '#c0765a',
    badgeClass: 'bg-amber-50 text-amber-700 border-amber-200',
    icon: 'pi pi-database',
  },
  postgres: {
    label: 'PostgreSQL',
    defaultPort: 5432,
    color: '#336791',
    badgeClass: 'bg-indigo-50 text-indigo-700 border-indigo-200',
    icon: 'pi pi-server',
  },
  mongo: {
    label: 'MongoDB',
    defaultPort: 27017,
    color: '#10aa50',
    badgeClass: 'bg-emerald-50 text-emerald-700 border-emerald-200',
    icon: 'pi pi-box',
  },
  mssql: {
    label: 'SQL Server',
    defaultPort: 1433,
    color: '#cc2927',
    badgeClass: 'bg-rose-50 text-rose-700 border-rose-200',
    icon: 'pi pi-microsoft',
  },
  redis: {
    label: 'Redis',
    defaultPort: 6379,
    color: '#d82c20',
    badgeClass: 'bg-red-50 text-red-700 border-red-200',
    icon: 'pi pi-bolt',
  },
  sqlite: {
    label: 'SQLite 3',
    defaultPort: 0,
    color: '#0f80cc',
    badgeClass: 'bg-cyan-50 text-cyan-700 border-cyan-200',
    icon: 'pi pi-file',
  },
  influx: {
    label: 'InfluxDB',
    defaultPort: 8086,
    color: '#22adf6',
    badgeClass: 'bg-purple-50 text-purple-700 border-purple-200',
    icon: 'pi pi-chart-line',
  },
  couch: {
    label: 'CouchDB',
    defaultPort: 5984,
    color: '#e42528',
    badgeClass: 'bg-orange-50 text-orange-700 border-orange-200',
    icon: 'pi pi-cloud',
  },
}

export function formatBytes(bytes?: number | null): string {
  if (bytes === undefined || bytes === null || isNaN(bytes)) return '0 B'
  if (bytes === 0) return '0 B'
  const k = 1024
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.floor(Math.log(Math.max(1, bytes)) / Math.log(k))
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(2))} ${sizes[i] || 'B'}`
}

export function formatDuration(ms?: number | null): string {
  if (ms === undefined || ms === null || isNaN(ms)) return '-'
  if (ms < 1000) return `${ms} ms`
  const sec = ms / 1000
  if (sec < 60) return `${sec.toFixed(2)}s`
  const mins = Math.floor(sec / 60)
  const remSec = Math.round(sec % 60)
  return `${mins}m ${remSec}s`
}

export function formatDateTime(iso?: string | null): string {
  if (!iso) return 'Never'
  try {
    const d = new Date(iso)
    if (isNaN(d.getTime())) return iso
    return d.toLocaleString(undefined, {
      year: 'numeric',
      month: 'short',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    })
  } catch {
    return iso
  }
}

export const useDbaStore = defineStore('dba', () => {
  // State
  const signalrState = ref<SignalRConnectionState>('disconnected')
  const summary = ref<DashboardSummary | null>(null)
  const systemInfo = ref<SystemInfo | null>(null)
  const connections = ref<ConnectionProfile[]>([])
  const storageProfiles = ref<StorageProfile[]>([])
  const encryptionProfiles = ref<EncryptionProfile[]>([])
  const jobs = ref<BackupJob[]>([])
  const backups = ref<BackupArtifact[]>([])
  const executions = ref<JobExecution[]>([])
  const schedulerJobs = ref<SchedulerJobInfo[]>([])
  const notifications = ref<NotificationProfile[]>([])

  // Live Terminal & Drawer State
  const liveDrawerOpen = ref(false)
  const activeExecutionId = ref<number | null>(null)
  const activeJobName = ref<string>('')
  const activeCommand = ref<string>('')
  const activeStatus = ref<string>('idle')
  const liveLines = ref<CliOutputEvent[]>([])
  const liveProgress = ref<ExecutionProgressEvent | null>(null)
  const activeStage = ref<string>('INIT')
  const activeStateEvent = ref<ExecutionStateChangedEvent | null>(null)
  const activeDiagnostic = ref<ProcessErrorDiagnostic | null>(null)

  // Toasts
  const toasts = ref<ToastMessage[]>([])
  let toastSeq = 1

  function notify(
    severity: 'success' | 'info' | 'warn' | 'error',
    summaryText: string,
    detail?: string
  ) {
    const id = toastSeq++
    toasts.value.push({ id, severity, summary: summaryText, detail })
    setTimeout(() => {
      toasts.value = toasts.value.filter((t) => t.id !== id)
    }, 4500)
  }

  function removeToast(id: number) {
    toasts.value = toasts.value.filter((t) => t.id !== id)
  }

  const runningExecutionsCount = computed(
    () => executions.value.filter((e) => e.status === 'running').length
  )

  let signalrInitialized = false
  let notifiedErrorExecutionId = -1

  function initSignalR() {
    if (signalrInitialized) return
    signalrInitialized = true

    signalrService.onStateChange((st) => {
      signalrState.value = st
    })

    signalrService.onExecutionStarted((ev) => {
      notifiedErrorExecutionId = -1
      activeExecutionId.value = ev.executionId
      activeJobName.value = ev.jobName
      activeCommand.value = ev.cliCommand
      activeStatus.value = 'running'
      activeStage.value = 'INIT'
      activeStateEvent.value = null
      activeDiagnostic.value = null
      liveProgress.value = null
      liveLines.value = []

      const existing = executions.value.find((x) => x.id === ev.executionId)
      if (!existing) {
        executions.value.unshift({
          id: ev.executionId,
          jobId: ev.jobId,
          jobName: ev.jobName,
          commandType: ev.commandType,
          trigger: ev.trigger,
          cliCommand: ev.cliCommand,
          status: 'running',
          startedAt: ev.startedAt,
          isDryRun: ev.cliCommand.includes('--dry-run'),
          currentStage: 'INIT',
        })
      }
    })

    signalrService.onExecutionStateChanged((ev) => {
      if (!activeExecutionId.value || activeExecutionId.value === ev.executionId) {
        activeStateEvent.value = ev
        activeStage.value = ev.currentStage
        if (ev.diagnostic) {
          activeDiagnostic.value = ev.diagnostic
        }
        if (ev.isError && ev.diagnostic && notifiedErrorExecutionId !== ev.executionId) {
          notifiedErrorExecutionId = ev.executionId
          notify('error', ev.diagnostic.title, ev.diagnostic.summary)
        }
      }
      const idx = executions.value.findIndex((x) => x.id === ev.executionId)
      if (idx !== -1) {
        executions.value[idx].currentStage = ev.currentStage
        if (ev.diagnostic?.summary) executions.value[idx].errorSummary = ev.diagnostic.summary
      }
    })

    signalrService.onCliOutput((ev) => {
      if (!activeExecutionId.value || activeExecutionId.value === ev.executionId) {
        activeExecutionId.value = ev.executionId
        if (ev.jobName) activeJobName.value = ev.jobName
        let parsed = ev.parsedJson
        if (!parsed && ev.line && ev.line.trim().startsWith('{')) {
          try {
            parsed = JSON.parse(ev.line.trim())
          } catch {
            parsed = null
          }
        }
        liveLines.value.push({
          ...ev,
          parsedJson: parsed,
        })
        if (liveLines.value.length > 2000) {
          liveLines.value.splice(0, liveLines.value.length - 2000)
        }
      }
    })

    signalrService.onExecutionProgress((ev) => {
      if (!activeExecutionId.value || activeExecutionId.value === ev.executionId) {
        liveProgress.value = ev
      }
    })

    signalrService.onExecutionCompleted((ev) => {
      if (activeExecutionId.value === ev.executionId) {
        activeStatus.value = ev.status
        if (ev.diagnostic) activeDiagnostic.value = ev.diagnostic
        if (ev.currentStage) activeStage.value = ev.currentStage
      }

      const idx = executions.value.findIndex((x) => x.id === ev.executionId)
      if (idx !== -1) {
        executions.value[idx] = {
          ...executions.value[idx],
          status: ev.status,
          exitCode: ev.exitCode,
          durationMs: ev.durationMs,
          outputFile: ev.outputFile,
          outputSize: ev.outputSize,
          finishedAt: ev.finishedAt,
          currentStage: ev.currentStage ?? (ev.status === 'completed' ? 'COMPLETED' : 'FAILED'),
          errorSummary: ev.errorSummary,
        }
      }

      if (ev.status === 'completed') {
        notify(
          'success',
          `${ev.commandType.toUpperCase()} Muvaffaqiyatli Yakunlandi`,
          `${ev.jobName} ${formatDuration(ev.durationMs)} ichida bajarildi (${formatBytes(ev.outputSize)})`
        )
      } else if (ev.status === 'cancelled') {
        notify('warn', 'Jarayon Bekor Qilindi', `${ev.jobName} to'xtatildi.`)
      } else {
        if (notifiedErrorExecutionId !== ev.executionId) {
          notifiedErrorExecutionId = ev.executionId
          notify(
            'error',
            ev.diagnostic?.title || `${ev.commandType.toUpperCase()} Xatolik Bilan To'xtadi`,
            ev.diagnostic?.summary || `${ev.jobName} xatolik sabab to'xtadi (Kod: ${ev.exitCode ?? -1})`
          )
        }
      }

      // Refresh dashboard, jobs, backups, executions
      fetchDashboard().catch(() => {})
      fetchJobs().catch(() => {})
      fetchBackups().catch(() => {})
      fetchExecutions().catch(() => {})
    })

    signalrService.onSchedulerUpdated((ev) => {
      const job = jobs.value.find((j) => j.id === ev.jobId)
      if (job) {
        if (ev.nextRunAt !== undefined) job.nextRunAt = ev.nextRunAt
        if (ev.lastRunAt !== undefined) job.lastRunAt = ev.lastRunAt
        if (ev.lastStatus) job.lastStatus = ev.lastStatus
      }
      fetchSchedulerJobs().catch(() => {})
    })

    signalrService.connect()
  }

  async function fetchDashboard() {
    summary.value = await api.getDashboardSummary()
  }

  async function fetchSystemInfo() {
    systemInfo.value = await api.getSystemInfo()
  }

  async function fetchAllProfiles() {
    const [conns, storages, encs, notifs] = await Promise.all([
      api.getConnections().catch(() => []),
      api.getStorageProfiles().catch(() => []),
      api.getEncryptionProfiles().catch(() => []),
      api.getNotifications().catch(() => []),
    ])
    connections.value = Array.isArray(conns) ? conns : []
    storageProfiles.value = Array.isArray(storages) ? storages : []
    encryptionProfiles.value = Array.isArray(encs) ? encs : []
    notifications.value = Array.isArray(notifs) ? notifs : []
  }

  async function fetchNotifications() {
    const res = await api.getNotifications().catch(() => [])
    notifications.value = Array.isArray(res) ? res : []
  }

  async function fetchJobs() {
    jobs.value = await api.getJobs()
  }

  async function fetchBackups(params?: { storageProfileId?: number | null; prefix?: string }) {
    backups.value = await api.getBackups(params)
  }

  async function fetchExecutions(limit = 50) {
    executions.value = await api.getExecutions({ limit })
  }

  async function fetchSchedulerJobs() {
    schedulerJobs.value = await api.getSchedulerJobs()
  }

  async function openExecutionInDrawer(executionId: number) {
    liveDrawerOpen.value = true
    if (activeExecutionId.value === executionId && liveLines.value.length > 0) {
      return
    }
    try {
      const exec = await api.getExecution(executionId)
      loadExecutionLogsIntoTerminal(exec)
    } catch (e: unknown) {
      notify('error', 'Failed to load execution logs', (e as Error).message)
    }
  }

  function loadExecutionLogsIntoTerminal(exec: JobExecution) {
    activeExecutionId.value = exec.id
    activeJobName.value = exec.jobName
    activeCommand.value = exec.cliCommand
    activeStatus.value = exec.status
    liveProgress.value = null
    activeStage.value = exec.currentStage || (exec.status === 'completed' ? 'COMPLETED' : (exec.status === 'failed' ? 'FAILED' : 'INIT'))
    if (exec.status === 'failed') {
      activeDiagnostic.value = {
        stage: exec.currentStage || 'FAILED',
        title: 'Jarayon Xatoligi',
        summary: exec.errorSummary || 'Ushbu jarayon bajarilishida xatolik yuz bergan.',
        technicalDetails: exec.errorDetails || undefined,
        actionableSteps: [
          'Ulanish profilida login va parolni tekshiring.',
          'Baza nomlari va server holatini tasdiqlang.',
          'Saqlash profilida disk maydoni yetarli ekanini ko\'ring.'
        ],
        suggestedActionUrl: '/jobs'
      }
    } else {
      activeDiagnostic.value = null
    }

    const rawLines = (exec.logOutput || '').split(/\r?\n/).filter((l) => l.trim().length > 0)
    liveLines.value = rawLines.map((raw) => {
      let stream: 'stdout' | 'stderr' = 'stdout'
      let text = raw
      if (raw.startsWith('[STDERR] ')) {
        stream = 'stderr'
        text = raw.slice(9)
      } else if (raw.startsWith('[STDOUT] ')) {
        stream = 'stdout'
        text = raw.slice(9)
      } else if (raw.trim().startsWith('{')) {
        stream = 'stderr'
      }

      let parsedJson: Record<string, unknown> | null = null
      if (text.trim().startsWith('{')) {
        try {
          parsedJson = JSON.parse(text.trim())
        } catch {
          parsedJson = null
        }
      }

      return {
        executionId: exec.id,
        jobId: exec.jobId,
        jobName: exec.jobName,
        commandType: exec.commandType,
        stream,
        line: text,
        parsedJson,
        timestamp: exec.startedAt,
      }
    })
  }

  async function cancelActiveExecution() {
    if (!activeExecutionId.value) return
    await signalrService.cancelExecution(activeExecutionId.value)
  }

  return {
    signalrState,
    summary,
    systemInfo,
    connections,
    storageProfiles,
    encryptionProfiles,
    notifications,
    jobs,
    backups,
    executions,
    schedulerJobs,
    liveDrawerOpen,
    activeExecutionId,
    activeJobName,
    activeCommand,
    activeStatus,
    activeStage,
    activeStateEvent,
    activeDiagnostic,
    liveLines,
    liveProgress,
    toasts,
    runningExecutionsCount,
    notify,
    removeToast,
    initSignalR,
    fetchDashboard,
    fetchSystemInfo,
    fetchAllProfiles,
    fetchNotifications,
    fetchJobs,
    fetchBackups,
    fetchExecutions,
    fetchSchedulerJobs,
    openExecutionInDrawer,
    loadExecutionLogsIntoTerminal,
    cancelActiveExecution,
  }
})
