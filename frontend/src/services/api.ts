import type {
  ArchiveBackupsRequest,
  BackupArtifact,
  BackupJob,
  ConnectionProfile,
  CustomCliRequest,
  DashboardSummary,
  EncryptionProfile,
  JobExecution,
  PruneBackupsRequest,
  RestoreBackupRequest,
  SchedulerJobInfo,
  SidecarMetadata,
  StorageProfile,
  SystemInfo,
  VerifyBackupRequest,
  NotificationProfile,
  BackupPreset,
} from '@/types/models'

export const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL || '').replace(/\/$/, '')

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const url = `${API_BASE_URL}${path.startsWith('/') ? path : `/${path}`}`
  const headers: Record<string, string> = {
    Accept: 'application/json',
    ...(options.headers as Record<string, string> || {}),
  }

  if (options.body && !(options.body instanceof FormData) && !headers['Content-Type']) {
    headers['Content-Type'] = 'application/json'
  }

  const response = await fetch(url, {
    ...options,
    headers,
  })

  if (!response.ok) {
    let errText = `HTTP ${response.status}: ${response.statusText}`
    try {
      const errJson = await response.json()
      errText = errJson.message || errJson.error || errJson.detail || JSON.stringify(errJson)
    } catch {
      const txt = await response.text().catch(() => '')
      if (txt) errText = txt
    }
    throw new Error(errText)
  }

  if (response.status === 204) {
    return undefined as T
  }

  const contentType = response.headers.get('content-type') || ''
  if (contentType.includes('application/json')) {
    return (await response.json()) as T
  }
  if (contentType.includes('text/html')) {
    throw new Error(`API path '${path}' returned HTML (route fallback), expected JSON.`)
  }
  return (await response.text()) as unknown as T
}

export const api = {
  baseUrl: API_BASE_URL,
  hangfireDashboardUrl: `${API_BASE_URL}/hangfire`,

  // Dashboard & System Info
  getDashboardSummary: () => request<DashboardSummary>('/api/dashboard/summary'),
  getSystemInfo: () => request<SystemInfo>('/api/system/info'),
  checkUpstreamVersion: () => request<Record<string, unknown>>('/api/system/version-check'),
  getLicenseStatus: () => request<Record<string, unknown>>('/api/system/license'),
  installLicense: (payload: { licenseKey?: string; licensePath?: string }) =>
    request<Record<string, unknown>>('/api/system/license/install', {
      method: 'POST',
      body: JSON.stringify(payload),
    }),
  getUsageStats: () => request<Record<string, unknown>>('/api/system/stats'),

  // Connection Profiles
  getConnections: () => request<ConnectionProfile[]>('/api/connections'),
  createConnection: (payload: Partial<ConnectionProfile>) =>
    request<ConnectionProfile>('/api/connections', {
      method: 'POST',
      body: JSON.stringify(payload),
    }),
  updateConnection: (id: number, payload: Partial<ConnectionProfile>) =>
    request<ConnectionProfile>(`/api/connections/${id}`, {
      method: 'PUT',
      body: JSON.stringify(payload),
    }),
  deleteConnection: (id: number) =>
    request<{ success?: boolean; message?: string }>(`/api/connections/${id}`, {
      method: 'DELETE',
    }),
  testConnection: (id: number) =>
    request<{ success: boolean; message: string; latencyMs?: number; details?: string }>(
      `/api/connections/${id}/test`,
      { method: 'POST' }
    ),
  getConnectionDatabases: (id: number) =>
    request<{ success: boolean; databases: string[]; message?: string }>(`/api/connections/${id}/databases`),
  getDatabases: (id: number) =>
    request<{ success: boolean; databases: string[]; message?: string }>(`/api/connections/${id}/databases`),

  // Storage Profiles
  getStorageProfiles: () => request<StorageProfile[]>('/api/storage-profiles'),
  createStorageProfile: (payload: Partial<StorageProfile>) =>
    request<StorageProfile>('/api/storage-profiles', {
      method: 'POST',
      body: JSON.stringify(payload),
    }),
  updateStorageProfile: (id: number, payload: Partial<StorageProfile>) =>
    request<StorageProfile>(`/api/storage-profiles/${id}`, {
      method: 'PUT',
      body: JSON.stringify(payload),
    }),
  deleteStorageProfile: (id: number) =>
    request<{ success?: boolean }>(`/api/storage-profiles/${id}`, {
      method: 'DELETE',
    }),
  testStorageProfile: (id: number) =>
    request<{ accessible: boolean; message: string; freeSpaceBytes?: number }>(
      `/api/storage-profiles/${id}/test`,
      { method: 'POST' }
    ),

  // Encryption Profiles
  getEncryptionProfiles: () => request<EncryptionProfile[]>('/api/encryption-profiles'),
  createEncryptionProfile: (payload: Partial<EncryptionProfile>) =>
    request<EncryptionProfile>('/api/encryption-profiles', {
      method: 'POST',
      body: JSON.stringify(payload),
    }),
  updateEncryptionProfile: (id: number, payload: Partial<EncryptionProfile>) =>
    request<EncryptionProfile>(`/api/encryption-profiles/${id}`, {
      method: 'PUT',
      body: JSON.stringify(payload),
    }),
  deleteEncryptionProfile: (id: number) =>
    request<{ success?: boolean }>(`/api/encryption-profiles/${id}`, {
      method: 'DELETE',
    }),

  // Backup Jobs
  getJobs: () => request<BackupJob[]>('/api/jobs'),
  getJob: (id: number) => request<BackupJob>(`/api/jobs/${id}`),
  createJob: (payload: Partial<BackupJob>) =>
    request<BackupJob>('/api/jobs', {
      method: 'POST',
      body: JSON.stringify(payload),
    }),
  updateJob: (id: number, payload: Partial<BackupJob>) =>
    request<BackupJob>(`/api/jobs/${id}`, {
      method: 'PUT',
      body: JSON.stringify(payload),
    }),
  deleteJob: (id: number) =>
    request<{ success?: boolean }>(`/api/jobs/${id}`, {
      method: 'DELETE',
    }),
  runJob: (id: number, dryRun = false) =>
    request<JobExecution>(`/api/jobs/${id}/run`, {
      method: 'POST',
      body: JSON.stringify({ dryRun }),
    }),
  maintainJob: (id: number) =>
    request<JobExecution>(`/api/jobs/${id}/maintain`, {
      method: 'POST',
    }),
  toggleJob: (id: number) =>
    request<BackupJob>(`/api/jobs/${id}/toggle`, {
      method: 'POST',
    }),

  // Backups & Sidecar Inspector
  getBackups: (params?: { storageProfileId?: number | null; prefix?: string }) => {
    const qs = new URLSearchParams()
    if (params?.storageProfileId) qs.set('storageProfileId', String(params.storageProfileId))
    if (params?.prefix) qs.set('prefix', params.prefix)
    const query = qs.toString() ? `?${qs.toString()}` : ''
    return request<BackupArtifact[]>(`/api/backups${query}`)
  },
  getBackupSidecar: (filename: string, storageProfileId?: number | null) => {
    const qs = storageProfileId ? `?storageProfileId=${storageProfileId}` : ''
    return request<SidecarMetadata>(`/api/backups/${encodeURIComponent(filename)}/sidecar${qs}`)
  },
  getBackupDownloadUrl: (filename: string, storageProfileId?: number | null) => {
    const qs = storageProfileId ? `?storageProfileId=${storageProfileId}` : ''
    return `${API_BASE_URL}/api/backups/${encodeURIComponent(filename)}/download${qs}`
  },
  deleteBackup: (filename: string, storageProfileId?: number | null) => {
    const qs = storageProfileId ? `?storageProfileId=${storageProfileId}` : ''
    return request<{ success?: boolean; message?: string }>(
      `/api/backups/${encodeURIComponent(filename)}${qs}`,
      { method: 'DELETE' }
    )
  },
  verifyBackup: (payload: VerifyBackupRequest) =>
    request<JobExecution>('/api/backups/verify', {
      method: 'POST',
      body: JSON.stringify(payload),
    }),
  restoreBackup: (payload: RestoreBackupRequest) =>
    request<JobExecution>('/api/backups/restore', {
      method: 'POST',
      body: JSON.stringify(payload),
    }),
  pruneBackups: (payload: PruneBackupsRequest) =>
    request<JobExecution>('/api/backups/prune', {
      method: 'POST',
      body: JSON.stringify(payload),
    }),
  archiveBackups: (payload: ArchiveBackupsRequest) =>
    request<JobExecution>('/api/backups/archive', {
      method: 'POST',
      body: JSON.stringify(payload),
    }),

  // Executions
  getExecutions: (params?: { limit?: number; jobId?: number }) => {
    const qs = new URLSearchParams()
    if (params?.limit) qs.set('limit', String(params.limit))
    if (params?.jobId) qs.set('jobId', String(params.jobId))
    const query = qs.toString() ? `?${qs.toString()}` : ''
    return request<JobExecution[]>(`/api/executions${query}`)
  },
  getExecution: (id: number) => request<JobExecution>(`/api/executions/${id}`),
  cancelExecution: (id: number) =>
    request<{ success?: boolean; message?: string }>(`/api/executions/${id}/cancel`, {
      method: 'POST',
    }),

  // Hangfire Scheduler
  getSchedulerJobs: () => request<SchedulerJobInfo[]>('/api/scheduler/jobs'),
  pauseSchedulerJob: (jobId: number) =>
    request<{ success?: boolean; message?: string }>(`/api/scheduler/jobs/${jobId}/pause`, {
      method: 'POST',
    }),
  resumeSchedulerJob: (jobId: number) =>
    request<{ success?: boolean; message?: string }>(`/api/scheduler/jobs/${jobId}/resume`, {
      method: 'POST',
    }),
  triggerSchedulerJob: (jobId: number) =>
    request<JobExecution | { success?: boolean; executionId?: number }>(
      `/api/scheduler/jobs/${jobId}/trigger`,
      { method: 'POST' }
    ),

  // Interactive CLI Runner
  executeCli: (payload: CustomCliRequest) =>
    request<JobExecution>('/api/cli/execute', {
      method: 'POST',
      body: JSON.stringify(payload),
    }),

  // YAML Config Studio
  getConfigYaml: async (): Promise<{ yamlContent: string; path?: string }> => {
    const res = await request<unknown>('/api/config/yaml')
    if (typeof res === 'string') {
      return { yamlContent: res }
    }
    const obj = res as Record<string, unknown>
    return {
      yamlContent: String(obj.yamlContent ?? obj.content ?? obj.yaml ?? ''),
      path: obj.path ? String(obj.path) : undefined,
    }
  },
  saveConfigYaml: (yamlContent: string) =>
    request<{ yamlContent?: string; message?: string }>('/api/config/yaml', {
      method: 'PUT',
      body: JSON.stringify({ yamlContent }),
    }),
  regenerateConfigYaml: async (): Promise<{ yamlContent: string; path?: string }> => {
    const res = await request<unknown>('/api/config/regenerate', {
      method: 'POST',
    })
    if (typeof res === 'string') {
      return { yamlContent: res }
    }
    const obj = res as Record<string, unknown>
    return {
      yamlContent: String(obj.yamlContent ?? obj.content ?? obj.yaml ?? ''),
      path: obj.path ? String(obj.path) : undefined,
    }
  },

  // Backup Presets
  getJobPresets: () => request<BackupPreset[]>('/api/jobs/presets'),

  // Notification Profiles & Alert Channels
  getNotifications: () => request<NotificationProfile[]>('/api/notifications'),
  getNotification: (id: number) => request<NotificationProfile>(`/api/notifications/${id}`),
  createNotification: (payload: Partial<NotificationProfile>) =>
    request<NotificationProfile>('/api/notifications', {
      method: 'POST',
      body: JSON.stringify(payload),
    }),
  updateNotification: (id: number, payload: Partial<NotificationProfile>) =>
    request<NotificationProfile>(`/api/notifications/${id}`, {
      method: 'PUT',
      body: JSON.stringify(payload),
    }),
  deleteNotification: (id: number) =>
    request<{ success?: boolean }>(`/api/notifications/${id}`, {
      method: 'DELETE',
    }),
  testNotification: (id: number) =>
    request<{ success: boolean; message: string }>(`/api/notifications/${id}/test`, {
      method: 'POST',
    }),
}

