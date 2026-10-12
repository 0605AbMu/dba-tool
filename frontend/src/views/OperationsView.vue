<script setup lang="ts">
import { onMounted, ref, computed } from 'vue'
import {
  useDbaStore,
  formatBytes,
  formatDuration,
  formatDateTime,
  ENGINE_METADATA,
} from '@/stores/dbaStore'
import { api } from '@/services/api'
import type { BackupArtifact, BackupJob, JobExecution, StorageProfile } from '@/types/models'

const store = useDbaStore()

// Active Operations Tab
const activeTab = ref<'maintenance' | 'verify' | 'prune' | 'archive' | 'stats' | 'engine'>('maintenance')

// Loading states
const executing = ref(false)
const statsLoading = ref(false)
const statsData = ref<Record<string, unknown> | null>(null)
const versionCheckResult = ref<Record<string, unknown> | null>(null)
const licenseInfo = ref<Record<string, unknown> | null>(null)
const showLicenseModal = ref(false)
const licenseKeyInput = ref('')
const licensePathInput = ref('')

// --- Tab 1: Maintenance State ---
const selectedJobId = ref<number | null>(null)
const maintCheckTables = ref(true)
const maintOptimize = ref(false)
const maintVacuum = ref(false)
const maintAnalyze = ref(true)
const maintReindex = ref(false)
const maintCompact = ref(false)
const maintMemoryPurge = ref(false)

// --- Tab 2: Verify State ---
const selectedBackupFilename = ref<string>('')
const verifyArtifact = computed<BackupArtifact | undefined>(() => {
  return store.backups.find((b) => b.filename === selectedBackupFilename.value)
})

// --- Tab 3: Prune State ---
const pruneStorageProfileId = ref<number | undefined>(undefined)
const pruneLastCount = ref(5)
const pruneWithinDays = ref('')
const pruneDryRun = ref(false)

// --- Tab 4: Archive State ---
const archiveSrcStorageId = ref<number | undefined>(undefined)
const archiveDstStorageId = ref<number | undefined>(undefined)
const archiveLastCount = ref(3)
const archiveWithinDays = ref('30d')
const archiveDryRun = ref(false)

// Filter for executions history table
const historyFilterType = ref<string>('all')

onMounted(async () => {
  await Promise.allSettled([
    store.fetchJobs(),
    store.fetchBackups(),
    store.fetchAllProfiles(),
    store.fetchExecutions(50),
    store.fetchSystemInfo(),
    loadUsageStats(),
    loadLicenseStatus(),
  ])

  if (store.jobs.length > 0 && !selectedJobId.value) {
    selectedJobId.value = store.jobs[0].id
    applyJobDefaults(store.jobs[0])
  }

  if (store.backups.length > 0 && !selectedBackupFilename.value) {
    selectedBackupFilename.value = store.backups[0].filename
  }

  if (store.storageProfiles.length > 0) {
    pruneStorageProfileId.value = store.storageProfiles[0].id
    archiveSrcStorageId.value = store.storageProfiles[0].id
    if (store.storageProfiles.length > 1) {
      archiveDstStorageId.value = store.storageProfiles[1].id
    }
  }
})

function applyJobDefaults(job: BackupJob) {
  maintCheckTables.value = job.maintCheckTables ?? true
  maintOptimize.value = job.maintOptimize ?? false
  maintVacuum.value = job.maintVacuum ?? false
  maintAnalyze.value = job.maintAnalyze ?? true
  maintReindex.value = job.maintReindex ?? false
  maintCompact.value = job.maintCompact ?? false
  maintMemoryPurge.value = job.maintMemoryPurge ?? false
}

function onJobChange() {
  const job = store.jobs.find((j) => j.id === selectedJobId.value)
  if (job) {
    applyJobDefaults(job)
  }
}

function applyMaintenancePreset(preset: 'quick' | 'deep' | 'full') {
  if (preset === 'quick') {
    maintCheckTables.value = true
    maintAnalyze.value = true
    maintOptimize.value = false
    maintVacuum.value = false
    maintReindex.value = false
    maintCompact.value = false
    maintMemoryPurge.value = false
  } else if (preset === 'deep') {
    maintCheckTables.value = true
    maintAnalyze.value = true
    maintVacuum.value = true
    maintOptimize.value = true
    maintReindex.value = false
    maintCompact.value = false
    maintMemoryPurge.value = false
  } else {
    maintCheckTables.value = true
    maintAnalyze.value = true
    maintVacuum.value = true
    maintOptimize.value = true
    maintReindex.value = true
    maintCompact.value = true
    maintMemoryPurge.value = true
  }
}

// Maintenance Execution
async function executeMaintenance() {
  if (!selectedJobId.value) {
    store.notify('warn', 'Reja tanlanmadi', 'Iltimos, texnik xizmat ko\'rsatish uchun zaxira rejasini tanlang.')
    return
  }
  const job = store.jobs.find((j) => j.id === selectedJobId.value)
  if (!job) return

  executing.value = true
  store.liveDrawerOpen = true
  try {
    const customFlags: string[] = []
    if (maintCheckTables.value) customFlags.push('--check-tables')
    if (maintOptimize.value) customFlags.push('--optimize')
    if (maintVacuum.value) customFlags.push('--vacuum')
    if (maintAnalyze.value) customFlags.push('--analyze')
    if (maintReindex.value) customFlags.push('--reindex')
    if (maintCompact.value) customFlags.push('--compact')
    if (maintMemoryPurge.value) customFlags.push('--memory-purge')
    customFlags.push(job.name)

    await api.executeCli({
      subcommand: 'maintain',
      args: customFlags,
      dryRun: false,
      logLevel: 'info',
      logFormat: 'json',
      progress: true,
    })

    store.notify(
      'info',
      'Texnik Xizmat Boshlandi',
      `"${job.name}" ma'lumotlar bazasi profilaktikasi boshlandi.`
    )
    await store.fetchExecutions(50)
  } catch (e: unknown) {
    store.notify('error', 'Texnik xizmat xatosi', (e as Error).message)
  } finally {
    executing.value = false
  }
}

// Verification Execution
async function executeVerify() {
  if (!selectedBackupFilename.value) {
    store.notify('warn', 'Fayl tanlanmadi', 'Iltimos, tekshirish uchun zaxira faylini tanlang.')
    return
  }

  const artifact = verifyArtifact.value
  executing.value = true
  store.liveDrawerOpen = true
  try {
    await api.verifyBackup({
      filename: selectedBackupFilename.value,
      storageProfileId: artifact?.storageProfileId,
    })

    store.notify(
      'info',
      'Yaxlitlik Tekshiruvi Boshlandi',
      `"${selectedBackupFilename.value}" zaxira faylining yaxlitligi va nazorat summasi tekshirilmoqda...`
    )
    await store.fetchExecutions(50)
  } catch (e: unknown) {
    store.notify('error', 'Tekshiruv xatosi', (e as Error).message)
  } finally {
    executing.value = false
  }
}

// Prune Execution
async function executePrune() {
  executing.value = true
  store.liveDrawerOpen = true
  try {
    await api.pruneBackups({
      storageProfileId: pruneStorageProfileId.value,
      last: pruneLastCount.value > 0 ? pruneLastCount.value : undefined,
      within: pruneWithinDays.value.trim() || undefined,
      dryRun: pruneDryRun.value,
    })

    store.notify(
      'info',
      pruneDryRun.value ? 'Sinovli Tozalash (Dry-Run) Boshlandi' : 'Tozalash Boshlandi',
      'Eskirgan zaxira nusxalarini tozalash jarayoni boshlandi.'
    )
    await store.fetchBackups()
    await store.fetchExecutions(50)
  } catch (e: unknown) {
    store.notify('error', 'Tozalash xatosi', (e as Error).message)
  } finally {
    executing.value = false
  }
}

// Archive Execution
async function executeArchive() {
  executing.value = true
  store.liveDrawerOpen = true
  try {
    await api.archiveBackups({
      srcStorageProfileId: archiveSrcStorageId.value,
      dstStorageProfileId: archiveDstStorageId.value,
      last: archiveLastCount.value > 0 ? archiveLastCount.value : undefined,
      within: archiveWithinDays.value.trim() || undefined,
      dryRun: archiveDryRun.value,
    })

    store.notify(
      'info',
      archiveDryRun.value ? 'Sinovli Arxivlash Boshlandi' : 'Arxivlash Boshlandi',
      'Omborlararo zaxiralarni arxivlash jarayoni boshlandi.'
    )
    await store.fetchBackups()
    await store.fetchExecutions(50)
  } catch (e: unknown) {
    store.notify('error', 'Arxivlash xatosi', (e as Error).message)
  } finally {
    executing.value = false
  }
}

// Stats & Telemetry
async function loadUsageStats() {
  statsLoading.value = true
  try {
    statsData.value = await api.getUsageStats()
  } catch {
    statsData.value = null
  } finally {
    statsLoading.value = false
  }
}

async function checkUpstreamVersion() {
  try {
    versionCheckResult.value = await api.checkUpstreamVersion()
    store.notify('info', 'Versiya Tekshirildi', 'Dvigatelning eng so\'nggi versiyasi holati muvaffaqiyatli tekshirildi.')
  } catch (e: unknown) {
    store.notify('error', 'Versiya tekshirish xatosi', (e as Error).message)
  }
}

async function loadLicenseStatus() {
  try {
    licenseInfo.value = await api.getLicenseStatus()
  } catch {
    licenseInfo.value = null
  }
}

async function installNewLicense() {
  if (!licenseKeyInput.value.trim() && !licensePathInput.value.trim()) {
    store.notify('warn', 'Ma\'lumot kiritilmadi', 'Litsenziya kaliti yoki fayl yo\'lini kiriting.')
    return
  }
  try {
    const res = await api.installLicense({
      licenseKey: licenseKeyInput.value.trim() || undefined,
      licensePath: licensePathInput.value.trim() || undefined,
    })
    licenseInfo.value = res
    showLicenseModal.value = false
    store.notify('success', 'Litsenziya Saqlandi', 'Yangi litsenziya muvaffaqiyatli o\'rnatildi.')
    await store.fetchSystemInfo()
  } catch (e: unknown) {
    store.notify('error', 'Litsenziya xatosi', (e as Error).message)
  }
}

// Filtered executions
const filteredExecutions = computed(() => {
  if (historyFilterType.value === 'all') {
    return store.executions.filter((e) =>
      ['maintain', 'verify', 'prune', 'archive', 'stats', 'custom'].includes(e.commandType)
    )
  }
  return store.executions.filter((e) => e.commandType === historyFilterType.value)
})

function getEngineMeta(engineType?: string) {
  const norm = (engineType || 'postgres').toLowerCase()
  return ENGINE_METADATA[norm] || {
    label: norm.toUpperCase(),
    icon: 'pi pi-database',
    badgeClass: 'bg-slate-100 text-slate-700 border-slate-200',
  }
}
</script>

<template>
  <div class="space-y-6">
    <!-- Header Section -->
    <div class="flex flex-col justify-between gap-4 sm:flex-row sm:items-center">
      <div>
        <h1 class="text-xl font-bold tracking-tight text-slate-900 flex items-center gap-2.5">
          <span class="flex h-9 w-9 items-center justify-center rounded-xl bg-emerald-50 text-emerald-700 border border-emerald-200">
            <i class="pi pi-wrench text-base"></i>
          </span>
          Ma'lumotlar Bazasi Amallari &amp; Diagnostika
        </h1>
        <p class="text-xs text-slate-600 mt-1">
          Baza profilaktikasi, zaxiralar yaxlitligini tekshirish, saqlash muddatini tozalash, arxivlash va tizim tahlili.
        </p>
      </div>

      <div class="flex items-center gap-2">
        <button
          @click="store.liveDrawerOpen = true"
          class="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-3.5 py-2 text-xs font-semibold text-slate-700 shadow-2xs hover:bg-slate-50 transition cursor-pointer"
        >
          <i class="pi pi-desktop text-emerald-600"></i>
          <span>Jarayonlar Oynasi</span>
          <span
            v-if="store.liveLines.length > 0"
            class="rounded-full bg-emerald-100 px-1.5 py-0.2 font-mono text-[10px] text-emerald-800"
          >
            {{ store.liveLines.length }}
          </span>
        </button>
      </div>
    </div>

    <!-- Navigation Tabs -->
    <div class="flex flex-wrap gap-2 border-b border-slate-200 pb-3">
      <button
        @click="activeTab = 'maintenance'"
        class="inline-flex items-center gap-2 rounded-xl px-4 py-2 text-xs font-semibold transition cursor-pointer"
        :class="
          activeTab === 'maintenance'
            ? 'bg-emerald-600 text-white shadow-sm shadow-emerald-600/20'
            : 'bg-white text-slate-700 border border-slate-200 hover:bg-slate-50'
        "
      >
        <i class="pi pi-wrench"></i>
        <span>Baza Profilaktikasi (Maintain)</span>
      </button>

      <button
        @click="activeTab = 'verify'"
        class="inline-flex items-center gap-2 rounded-xl px-4 py-2 text-xs font-semibold transition cursor-pointer"
        :class="
          activeTab === 'verify'
            ? 'bg-emerald-600 text-white shadow-sm shadow-emerald-600/20'
            : 'bg-white text-slate-700 border border-slate-200 hover:bg-slate-50'
        "
      >
        <i class="pi pi-verified"></i>
        <span>Yaxlitlik Tekshiruvi (Verify)</span>
      </button>

      <button
        @click="activeTab = 'prune'"
        class="inline-flex items-center gap-2 rounded-xl px-4 py-2 text-xs font-semibold transition cursor-pointer"
        :class="
          activeTab === 'prune'
            ? 'bg-emerald-600 text-white shadow-sm shadow-emerald-600/20'
            : 'bg-white text-slate-700 border border-slate-200 hover:bg-slate-50'
        "
      >
        <i class="pi pi-filter-slash"></i>
        <span>Eskirganlarni Tozalash (Prune)</span>
      </button>

      <button
        @click="activeTab = 'archive'"
        class="inline-flex items-center gap-2 rounded-xl px-4 py-2 text-xs font-semibold transition cursor-pointer"
        :class="
          activeTab === 'archive'
            ? 'bg-emerald-600 text-white shadow-sm shadow-emerald-600/20'
            : 'bg-white text-slate-700 border border-slate-200 hover:bg-slate-50'
        "
      >
        <i class="pi pi-box"></i>
        <span>Arxivlash (Archive)</span>
      </button>

      <button
        @click="activeTab = 'stats'"
        class="inline-flex items-center gap-2 rounded-xl px-4 py-2 text-xs font-semibold transition cursor-pointer"
        :class="
          activeTab === 'stats'
            ? 'bg-emerald-600 text-white shadow-sm shadow-emerald-600/20'
            : 'bg-white text-slate-700 border border-slate-200 hover:bg-slate-50'
        "
      >
        <i class="pi pi-chart-bar"></i>
        <span>Xotira &amp; Zanjir Tahlili (Stats)</span>
      </button>

      <button
        @click="activeTab = 'engine'"
        class="inline-flex items-center gap-2 rounded-xl px-4 py-2 text-xs font-semibold transition cursor-pointer"
        :class="
          activeTab === 'engine'
            ? 'bg-emerald-600 text-white shadow-sm shadow-emerald-600/20'
            : 'bg-white text-slate-700 border border-slate-200 hover:bg-slate-50'
        "
      >
        <i class="pi pi-shield"></i>
        <span>Dvigatel &amp; Litsenziya</span>
      </button>
    </div>

    <!-- TAB 1: BAZA PROFILAKTIKASI (MAINTENANCE) -->
    <div v-if="activeTab === 'maintenance'" class="space-y-5">
      <div class="rounded-2xl border border-slate-200 bg-white p-6 shadow-2xs space-y-6">
        <div>
          <h2 class="text-base font-bold text-slate-900 flex items-center gap-2">
            <i class="pi pi-cog text-emerald-600"></i>
            Ma'lumotlar Bazasi Texnik Xizmati va Profilaktikasi
          </h2>
          <p class="text-xs text-slate-600 mt-1">
            Jadvallar tuzilishini tekshirish, bo'sh joylarni tozalash (vacuum), indekslarni qayta hisoblash va so'rovlar optimizatori uchun statistikalarni yangilash.
          </p>
        </div>

        <!-- Target Job / Database Selection -->
        <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
          <div class="space-y-1.5">
            <label class="block text-xs font-semibold uppercase tracking-wider text-slate-600">
              1. Zaxira Rejasi / Baza Ulanishi
            </label>
            <select
              v-model="selectedJobId"
              @change="onJobChange"
              class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2.5 text-xs font-semibold text-slate-900 shadow-2xs focus:border-emerald-500 focus:outline-hidden"
            >
              <option v-for="j in store.jobs" :key="j.id" :value="j.id">
                {{ j.name }} ({{ j.connectionProfile?.type || 'Baza' }} • {{ j.strategy }})
              </option>
            </select>
          </div>

          <!-- Quick Presets -->
          <div class="space-y-1.5">
            <label class="block text-xs font-semibold uppercase tracking-wider text-slate-600">
              Tezkor Shablonlar
            </label>
            <div class="flex flex-wrap gap-2 pt-0.5">
              <button
                @click="applyMaintenancePreset('quick')"
                type="button"
                class="rounded-xl border border-slate-200 bg-slate-50 px-3 py-2 text-xs font-medium text-slate-700 hover:bg-emerald-50 hover:text-emerald-800 hover:border-emerald-300 transition cursor-pointer"
              >
                ⚡ Tezkor Tahlil
              </button>
              <button
                @click="applyMaintenancePreset('deep')"
                type="button"
                class="rounded-xl border border-slate-200 bg-slate-50 px-3 py-2 text-xs font-medium text-slate-700 hover:bg-emerald-50 hover:text-emerald-800 hover:border-emerald-300 transition cursor-pointer"
              >
                🧹 Chuqur Tozalash &amp; Vakuum
              </button>
              <button
                @click="applyMaintenancePreset('full')"
                type="button"
                class="rounded-xl border border-slate-200 bg-slate-50 px-3 py-2 text-xs font-medium text-slate-700 hover:bg-emerald-50 hover:text-emerald-800 hover:border-emerald-300 transition cursor-pointer"
              >
                🚀 Barcha Amallar
              </button>
            </div>
          </div>
        </div>

        <!-- Operations Checkboxes Cards -->
        <div class="space-y-2">
          <label class="block text-xs font-semibold uppercase tracking-wider text-slate-600">
            2. Bajariladigan Profilaktika Amallari
          </label>
          <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
            <!-- Check Tables -->
            <label
              class="flex items-start gap-3 rounded-xl border p-3.5 transition cursor-pointer select-none"
              :class="maintCheckTables ? 'border-emerald-300 bg-emerald-50/50 text-emerald-950' : 'border-slate-200 bg-slate-50 text-slate-700 hover:bg-slate-100'"
            >
              <input type="checkbox" v-model="maintCheckTables" class="mt-0.5 h-4 w-4 rounded accent-emerald-600" />
              <div class="text-xs">
                <div class="font-bold flex items-center gap-1.5">
                  <i class="pi pi-check-circle text-emerald-600"></i> Jadvallarni Tekshirish
                </div>
                <div class="text-[11px] text-slate-500 mt-0.5">
                  Jadvallar fizik yaxlitligi va korruptsiyasini tekshiradi (--check-tables).
                </div>
              </div>
            </label>

            <!-- Analyze -->
            <label
              class="flex items-start gap-3 rounded-xl border p-3.5 transition cursor-pointer select-none"
              :class="maintAnalyze ? 'border-emerald-300 bg-emerald-50/50 text-emerald-950' : 'border-slate-200 bg-slate-50 text-slate-700 hover:bg-slate-100'"
            >
              <input type="checkbox" v-model="maintAnalyze" class="mt-0.5 h-4 w-4 rounded accent-emerald-600" />
              <div class="text-xs">
                <div class="font-bold flex items-center gap-1.5">
                  <i class="pi pi-search text-emerald-600"></i> Statistikalarni Tahlil Qilish
                </div>
                <div class="text-[11px] text-slate-500 mt-0.5">
                  So'rovlar optimizatori (Query Planner) uchun statistikani yangilaydi (--analyze).
                </div>
              </div>
            </label>

            <!-- Vacuum -->
            <label
              class="flex items-start gap-3 rounded-xl border p-3.5 transition cursor-pointer select-none"
              :class="maintVacuum ? 'border-emerald-300 bg-emerald-50/50 text-emerald-950' : 'border-slate-200 bg-slate-50 text-slate-700 hover:bg-slate-100'"
            >
              <input type="checkbox" v-model="maintVacuum" class="mt-0.5 h-4 w-4 rounded accent-emerald-600" />
              <div class="text-xs">
                <div class="font-bold flex items-center gap-1.5">
                  <i class="pi pi-trash text-emerald-600"></i> Vakuum / Bo'sh Joyni Qaytarish
                </div>
                <div class="text-[11px] text-slate-500 mt-0.5">
                  O'chirilgan yozuvlar bo'shatgan disk maydonini tizimga qaytaradi (--vacuum).
                </div>
              </div>
            </label>

            <!-- Optimize -->
            <label
              class="flex items-start gap-3 rounded-xl border p-3.5 transition cursor-pointer select-none"
              :class="maintOptimize ? 'border-emerald-300 bg-emerald-50/50 text-emerald-950' : 'border-slate-200 bg-slate-50 text-slate-700 hover:bg-slate-100'"
            >
              <input type="checkbox" v-model="maintOptimize" class="mt-0.5 h-4 w-4 rounded accent-emerald-600" />
              <div class="text-xs">
                <div class="font-bold flex items-center gap-1.5">
                  <i class="pi pi-bolt text-emerald-600"></i> Jadvallarni Optimallashtirish
                </div>
                <div class="text-[11px] text-slate-500 mt-0.5">
                  Ma'lumotlar fragmentatsiyasini kamaytiradi (--optimize).
                </div>
              </div>
            </label>

            <!-- Reindex -->
            <label
              class="flex items-start gap-3 rounded-xl border p-3.5 transition cursor-pointer select-none"
              :class="maintReindex ? 'border-emerald-300 bg-emerald-50/50 text-emerald-950' : 'border-slate-200 bg-slate-50 text-slate-700 hover:bg-slate-100'"
            >
              <input type="checkbox" v-model="maintReindex" class="mt-0.5 h-4 w-4 rounded accent-emerald-600" />
              <div class="text-xs">
                <div class="font-bold flex items-center gap-1.5">
                  <i class="pi pi-refresh text-emerald-600"></i> Indekslarni Qayta Qurish
                </div>
                <div class="text-[11px] text-slate-500 mt-0.5">
                  Barcha indeks daraxtlarini noldan qayta quradi (--reindex).
                </div>
              </div>
            </label>

            <!-- Compact / Memory Purge -->
            <label
              class="flex items-start gap-3 rounded-xl border p-3.5 transition cursor-pointer select-none"
              :class="maintCompact || maintMemoryPurge ? 'border-emerald-300 bg-emerald-50/50 text-emerald-950' : 'border-slate-200 bg-slate-50 text-slate-700 hover:bg-slate-100'"
            >
              <input type="checkbox" v-model="maintCompact" class="mt-0.5 h-4 w-4 rounded accent-emerald-600" />
              <div class="text-xs">
                <div class="font-bold flex items-center gap-1.5">
                  <i class="pi pi-box text-emerald-600"></i> NoSQL &amp; Kesh Tozalash
                </div>
                <div class="text-[11px] text-slate-500 mt-0.5">
                  MongoDB kompaktlash (--compact) va Redis keshini bo'shatish (--memory-purge).
                </div>
              </div>
            </label>
          </div>
        </div>

        <!-- Action Bar -->
        <div class="flex items-center justify-between border-t border-slate-200 pt-4">
          <div class="text-xs text-slate-500">
            Jarayon natijalari real vaqtda SignalR oqimi orqali uzatiladi.
          </div>
          <button
            @click="executeMaintenance"
            :disabled="executing || !selectedJobId"
            class="inline-flex items-center gap-2 rounded-xl bg-emerald-600 px-5 py-2.5 text-xs font-semibold text-white shadow-md shadow-emerald-600/20 hover:bg-emerald-500 disabled:opacity-50 cursor-pointer"
          >
            <i class="pi pi-play text-xs"></i>
            <span>Profilaktikani Boshlash</span>
          </button>
        </div>
      </div>
    </div>

    <!-- TAB 2: YAXLITLIK TEKSHIRUVI (VERIFY) -->
    <div v-if="activeTab === 'verify'" class="space-y-5">
      <div class="rounded-2xl border border-slate-200 bg-white p-6 shadow-2xs space-y-6">
        <div>
          <h2 class="text-base font-bold text-slate-900 flex items-center gap-2">
            <i class="pi pi-verified text-emerald-600"></i>
            Zaxira Nusxasi Yaxlitligi va Checksum Tekshiruvi
          </h2>
          <p class="text-xs text-slate-600 mt-1">
            Zaxira faylining nazorat summasini (MD5/SHA256) Sidecar v2 metama'lumotlari bilan solishtirish va arxivni sinov dekompressiya qilish.
          </p>
        </div>

        <!-- Target Backup Selector -->
        <div class="space-y-1.5">
          <label class="block text-xs font-semibold uppercase tracking-wider text-slate-600">
            Tekshirish Uchun Zaxira Faylini Tanlang
          </label>
          <select
            v-model="selectedBackupFilename"
            class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2.5 text-xs font-semibold text-slate-900 shadow-2xs focus:border-emerald-500 focus:outline-hidden"
          >
            <option v-for="b in store.backups" :key="b.filename" :value="b.filename">
              {{ b.filename }} ({{ formatBytes(b.size) }} • {{ formatDateTime(b.createdAt) }})
            </option>
          </select>
        </div>

        <!-- Artifact Details Card -->
        <div v-if="verifyArtifact" class="rounded-xl border border-slate-200 bg-slate-50 p-4 space-y-3">
          <div class="text-xs font-bold text-slate-800">Tanlangan Zaxira Ma'lumotlari:</div>
          <div class="grid grid-cols-2 md:grid-cols-4 gap-4 text-xs">
            <div>
              <span class="text-slate-500 block text-[11px]">Hajmi:</span>
              <span class="font-bold text-slate-900 font-mono">{{ formatBytes(verifyArtifact.size) }}</span>
            </div>
            <div>
              <span class="text-slate-500 block text-[11px]">Siqish turi:</span>
              <span class="font-semibold text-indigo-700 uppercase">{{ verifyArtifact.compression || 'none' }}</span>
            </div>
            <div>
              <span class="text-slate-500 block text-[11px]">Yaratilgan vaqt:</span>
              <span class="font-medium text-slate-800">{{ formatDateTime(verifyArtifact.createdAt) }}</span>
            </div>
            <div>
              <span class="text-slate-500 block text-[11px]">Sidecar holati:</span>
              <span
                class="inline-flex items-center gap-1 font-semibold"
                :class="verifyArtifact.hasSidecar ? 'text-emerald-700' : 'text-slate-500'"
              >
                <i :class="verifyArtifact.hasSidecar ? 'pi pi-check' : 'pi pi-times'"></i>
                {{ verifyArtifact.hasSidecar ? 'Sidecar v2 Mavjud' : 'Mavjud emas' }}
              </span>
            </div>
          </div>
        </div>

        <!-- Action Bar -->
        <div class="flex items-center justify-between border-t border-slate-200 pt-4">
          <div class="text-xs text-slate-500">
            Tekshiruv arxividagi barcha fayl bloklari va checksumlar qayta hisoblanadi.
          </div>
          <button
            @click="executeVerify"
            :disabled="executing || !selectedBackupFilename"
            class="inline-flex items-center gap-2 rounded-xl bg-emerald-600 px-5 py-2.5 text-xs font-semibold text-white shadow-md shadow-emerald-600/20 hover:bg-emerald-500 disabled:opacity-50 cursor-pointer"
          >
            <i class="pi pi-shield text-xs"></i>
            <span>Yaxlitlikni Tekshirish</span>
          </button>
        </div>
      </div>
    </div>

    <!-- TAB 3: ESKIRGANLARNI TOZALASH (PRUNE) -->
    <div v-if="activeTab === 'prune'" class="space-y-5">
      <div class="rounded-2xl border border-slate-200 bg-white p-6 shadow-2xs space-y-6">
        <div>
          <h2 class="text-base font-bold text-slate-900 flex items-center gap-2">
            <i class="pi pi-filter-slash text-emerald-600"></i>
            Eskirgan Zaxiralarni Tozalash (Retention Prune)
          </h2>
          <p class="text-xs text-slate-600 mt-1">
            Saqlash siyosati asosida muddati o'tgan yoki ortiqcha zaxiralarni ombordan xavfsiz tozalash.
          </p>
        </div>

        <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
          <!-- Storage Backend -->
          <div class="space-y-1.5">
            <label class="block text-xs font-semibold uppercase tracking-wider text-slate-600">
              Saqlash Ombori
            </label>
            <select
              v-model="pruneStorageProfileId"
              class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2.5 text-xs font-semibold text-slate-900 shadow-2xs focus:border-emerald-500 focus:outline-hidden"
            >
              <option :value="undefined">(Barcha faol omborlar)</option>
              <option v-for="sp in store.storageProfiles" :key="sp.id" :value="sp.id">
                {{ sp.name }} ({{ sp.backend }} • {{ sp.path }})
              </option>
            </select>
          </div>

          <!-- Keep Last N Backups -->
          <div class="space-y-1.5">
            <label class="block text-xs font-semibold uppercase tracking-wider text-slate-600">
              Oxirgi N Ta Yangi Zaxirani Saqlash (--last)
            </label>
            <input
              type="number"
              v-model.number="pruneLastCount"
              min="1"
              max="1000"
              class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2.5 text-xs font-semibold text-slate-900 shadow-2xs focus:border-emerald-500 focus:outline-hidden"
            />
          </div>
        </div>

        <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
          <!-- Time Window -->
          <div class="space-y-1.5">
            <label class="block text-xs font-semibold uppercase tracking-wider text-slate-600">
              Muddat Bo'yicha Saqlash (--within, masalan: 30d, 12w, 1y)
            </label>
            <input
              type="text"
              v-model="pruneWithinDays"
              placeholder="Masalan: 30d"
              class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2.5 text-xs font-semibold text-slate-900 shadow-2xs focus:border-emerald-500 focus:outline-hidden font-mono"
            />
          </div>

          <!-- Dry Run Mode Switch -->
          <div class="flex items-center pt-6">
            <label class="flex items-center gap-3 rounded-xl border border-slate-200 bg-slate-50 p-3 w-full cursor-pointer select-none">
              <input type="checkbox" v-model="pruneDryRun" class="h-4 w-4 rounded accent-emerald-600" />
              <div class="text-xs">
                <span class="font-bold text-slate-900">Xavfsiz Sinov Rejimi (Dry-Run)</span>
                <span class="block text-[11px] text-slate-500">
                  Fayllarni o'chirmasdan, qaysi zaxiralar o'chirilishini simulyatsiya qiladi.
                </span>
              </div>
            </label>
          </div>
        </div>

        <!-- Action Bar -->
        <div class="flex items-center justify-between border-t border-slate-200 pt-4">
          <div class="text-xs text-slate-500">
            Tozalash jarayoni barcha bog'langan .json Sidecar fayllarini ham avtomatik boshqaradi.
          </div>
          <button
            @click="executePrune"
            :disabled="executing"
            class="inline-flex items-center gap-2 rounded-xl bg-emerald-600 px-5 py-2.5 text-xs font-semibold text-white shadow-md shadow-emerald-600/20 hover:bg-emerald-500 disabled:opacity-50 cursor-pointer"
          >
            <i class="pi pi-trash text-xs"></i>
            <span>Tozalashni Boshlash</span>
          </button>
        </div>
      </div>
    </div>

    <!-- TAB 4: ARXIVLASH (ARCHIVE) -->
    <div v-if="activeTab === 'archive'" class="space-y-5">
      <div class="rounded-2xl border border-slate-200 bg-white p-6 shadow-2xs space-y-6">
        <div>
          <h2 class="text-base font-bold text-slate-900 flex items-center gap-2">
            <i class="pi pi-box text-emerald-600"></i>
            Omborlararo Zaxiralarni Arxivlash va Sinxronizatsiya
          </h2>
          <p class="text-xs text-slate-600 mt-1">
            Zaxiralarni tezkor lokal ombordan uzoq muddatli sovuq arxivga (S3, GCS, Azure, NFS) ko'chirish yoki sinxronlashtirish.
          </p>
        </div>

        <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
          <!-- Source Storage -->
          <div class="space-y-1.5">
            <label class="block text-xs font-semibold uppercase tracking-wider text-slate-600">
              Boshlang'ich Ombor (Manba)
            </label>
            <select
              v-model="archiveSrcStorageId"
              class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2.5 text-xs font-semibold text-slate-900 shadow-2xs focus:border-emerald-500 focus:outline-hidden"
            >
              <option v-for="sp in store.storageProfiles" :key="sp.id" :value="sp.id">
                {{ sp.name }} ({{ sp.backend }} • {{ sp.path }})
              </option>
            </select>
          </div>

          <!-- Destination Storage -->
          <div class="space-y-1.5">
            <label class="block text-xs font-semibold uppercase tracking-wider text-slate-600">
              Qabul Qiluvchi Ombor (Nishon)
            </label>
            <select
              v-model="archiveDstStorageId"
              class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2.5 text-xs font-semibold text-slate-900 shadow-2xs focus:border-emerald-500 focus:outline-hidden"
            >
              <option v-for="sp in store.storageProfiles" :key="sp.id" :value="sp.id">
                {{ sp.name }} ({{ sp.backend }} • {{ sp.path }})
              </option>
            </select>
          </div>
        </div>

        <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
          <!-- Time Range -->
          <div class="space-y-1.5">
            <label class="block text-xs font-semibold uppercase tracking-wider text-slate-600">
              Arxivlash Oralig'i (--within)
            </label>
            <input
              type="text"
              v-model="archiveWithinDays"
              placeholder="Masalan: 30d"
              class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2.5 text-xs font-semibold text-slate-900 shadow-2xs focus:border-emerald-500 focus:outline-hidden font-mono"
            />
          </div>

          <!-- Dry Run Mode Switch -->
          <div class="flex items-center pt-6">
            <label class="flex items-center gap-3 rounded-xl border border-slate-200 bg-slate-50 p-3 w-full cursor-pointer select-none">
              <input type="checkbox" v-model="archiveDryRun" class="h-4 w-4 rounded accent-emerald-600" />
              <div class="text-xs">
                <span class="font-bold text-slate-900">Sinov Rejimi (Dry-Run)</span>
                <span class="block text-[11px] text-slate-500">
                  Fayllarni nusxalamasdan, ko'chiriladigan arxivlar ro'yxatini hisoblaydi.
                </span>
              </div>
            </label>
          </div>
        </div>

        <!-- Action Bar -->
        <div class="flex items-center justify-between border-t border-slate-200 pt-4">
          <div class="text-xs text-slate-500">
            Arxivlash jarayoni barcha nusxalar yaxlitligini avtomatik kafolatlaydi.
          </div>
          <button
            @click="executeArchive"
            :disabled="executing"
            class="inline-flex items-center gap-2 rounded-xl bg-emerald-600 px-5 py-2.5 text-xs font-semibold text-white shadow-md shadow-emerald-600/20 hover:bg-emerald-500 disabled:opacity-50 cursor-pointer"
          >
            <i class="pi pi-box text-xs"></i>
            <span>Arxivlashni Boshlash</span>
          </button>
        </div>
      </div>
    </div>

    <!-- TAB 5: XOTIRA & ZANJIR TAHLILI (STATS) -->
    <div v-if="activeTab === 'stats'" class="space-y-5">
      <div class="rounded-2xl border border-slate-200 bg-white p-6 shadow-2xs space-y-6">
        <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
          <div>
            <h2 class="text-base font-bold text-slate-900 flex items-center gap-2">
              <i class="pi pi-chart-bar text-emerald-600"></i>
              Zaxiralar va Xotira Statistikasi
            </h2>
            <p class="text-xs text-slate-600 mt-1">
              Zaxira zanjirlari, siqish koeffitsienti, egallangan hajm va disk tejami tahlili.
            </p>
          </div>
          <button
            @click="loadUsageStats"
            :disabled="statsLoading"
            class="inline-flex items-center gap-1.5 rounded-xl border border-slate-200 bg-slate-50 px-3.5 py-2 text-xs font-semibold text-slate-700 hover:bg-slate-100 cursor-pointer"
          >
            <i class="pi pi-refresh" :class="{ 'pi-spin': statsLoading }"></i>
            <span>Statistikalarni Yangilash</span>
          </button>
        </div>

        <!-- KPI Cards Grid -->
        <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
          <div class="rounded-xl border border-slate-200 bg-slate-50 p-4 space-y-1">
            <span class="text-[11px] font-semibold text-slate-500 uppercase tracking-wider">Jami Zaxira Hajmi</span>
            <div class="text-xl font-bold text-slate-900 font-mono">
              {{ formatBytes(store.summary?.totalStorageBytes) }}
            </div>
            <div class="text-[11px] text-slate-500">Siqilgan va shifrlangan holatda</div>
          </div>

          <div class="rounded-xl border border-slate-200 bg-slate-50 p-4 space-y-1">
            <span class="text-[11px] font-semibold text-slate-500 uppercase tracking-wider">Muvaffaqiyat Darajasi</span>
            <div class="text-xl font-bold text-emerald-700 font-mono">
              {{ store.summary?.successRate ? store.summary.successRate + '%' : '100%' }}
            </div>
            <div class="text-[11px] text-slate-500">Muvaffaqiyatli yakunlangan amallar</div>
          </div>

          <div class="rounded-xl border border-slate-200 bg-slate-50 p-4 space-y-1">
            <span class="text-[11px] font-semibold text-slate-500 uppercase tracking-wider">Bajarilgan Amallar</span>
            <div class="text-xl font-bold text-indigo-700 font-mono">
              {{ store.summary?.totalExecutions ?? 0 }}
            </div>
            <div class="text-[11px] text-indigo-600 font-medium">Barcha qayd etilgan jarayonlar</div>
          </div>

          <div class="rounded-xl border border-slate-200 bg-slate-50 p-4 space-y-1">
            <span class="text-[11px] font-semibold text-slate-500 uppercase tracking-wider">Zaxiralar Soni</span>
            <div class="text-xl font-bold text-slate-900 font-mono">
              {{ store.backups.length }}
            </div>
            <div class="text-[11px] text-slate-500">Mavjud barcha nusxalar</div>
          </div>
        </div>

        <!-- Raw Stats Inspector Output if available -->
        <div v-if="statsData" class="rounded-xl border border-slate-200 bg-slate-50 p-4 space-y-2">
          <div class="text-xs font-bold text-slate-800">Dvigatelning To'liq Statistikasi (JSON Telemetry):</div>
          <pre class="max-h-60 overflow-y-auto rounded-lg bg-white p-3 font-mono text-[11px] text-slate-800 border border-slate-200">{{ JSON.stringify(statsData, null, 2) }}</pre>
        </div>
      </div>
    </div>

    <!-- TAB 6: DVIGATEL & LITSENZIYA -->
    <div v-if="activeTab === 'engine'" class="space-y-5">
      <div class="rounded-2xl border border-slate-200 bg-white p-6 shadow-2xs space-y-6">
        <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
          <div>
            <h2 class="text-base font-bold text-slate-900 flex items-center gap-2">
              <i class="pi pi-shield text-emerald-600"></i>
              Zaxiralash Dvigateli va Litsenziya Diagnostikasi
            </h2>
            <p class="text-xs text-slate-600 mt-1">
              Statically compiled <code class="text-emerald-700">nfrastack/db-backup v5.1.0</code> dvigateli holati va professional modullar.
            </p>
          </div>
          <div class="flex items-center gap-2">
            <button
              @click="checkUpstreamVersion"
              class="inline-flex items-center gap-1.5 rounded-xl border border-slate-200 bg-slate-50 px-3.5 py-2 text-xs font-semibold text-slate-700 hover:bg-slate-100 cursor-pointer"
            >
              <i class="pi pi-cloud-download text-emerald-600"></i>
              <span>Versiyani Tekshirish</span>
            </button>
            <button
              @click="showLicenseModal = true"
              class="inline-flex items-center gap-1.5 rounded-xl bg-indigo-50 border border-indigo-200 px-3.5 py-2 text-xs font-semibold text-indigo-700 hover:bg-indigo-100 cursor-pointer"
            >
              <i class="pi pi-key"></i>
              <span>Litsenziya O'rnatish</span>
            </button>
          </div>
        </div>

        <!-- Engine Specs Cards -->
        <div class="grid grid-cols-1 md:grid-cols-3 gap-4 text-xs">
          <div class="rounded-xl border border-slate-200 bg-slate-50 p-4 space-y-2">
            <div class="font-bold text-slate-800 flex items-center gap-2">
              <i class="pi pi-server text-emerald-600"></i>
              Dvigatel Binar Fayli
            </div>
            <div class="space-y-1 font-mono text-[11px] text-slate-600">
              <div>Versiya: <strong class="text-emerald-700">v5.1.0 (Supporter Edition)</strong></div>
              <div>Platforma: <span class="text-slate-800">{{ store.systemInfo?.platform || 'linux/amd64' }}</span></div>
              <div>Binar: <span class="text-slate-800 truncate block">{{ store.systemInfo?.binaryPath || 'bin/dbb' }}</span></div>
            </div>
          </div>

          <div class="rounded-xl border border-slate-200 bg-slate-50 p-4 space-y-2">
            <div class="font-bold text-slate-800 flex items-center gap-2">
              <i class="pi pi-lock text-indigo-600"></i>
              Litsenziya Holati
            </div>
            <div class="space-y-1 text-[11px] text-slate-600">
              <div>Nashr: <strong class="text-indigo-700">Supporter Edition (100% Unlocked)</strong></div>
              <div>Cheklovlar: <span class="font-semibold text-emerald-700">Cheklovlarsiz (Unlimited)</span></div>
              <div>Muddat: <span class="text-slate-800">Doimiy (Perpetual)</span></div>
            </div>
          </div>

          <div class="rounded-xl border border-slate-200 bg-slate-50 p-4 space-y-2">
            <div class="font-bold text-slate-800 flex items-center gap-2">
              <i class="pi pi-folder text-amber-600"></i>
              Kataloglar Tuzilishi
            </div>
            <div class="space-y-1 font-mono text-[11px] text-slate-600 truncate">
              <div>Zaxiralar: <span class="text-slate-800">{{ store.systemInfo?.backupsDirectory || 'data/backups' }}</span></div>
              <div>Konfiguratsiya: <span class="text-slate-800">data/configs</span></div>
              <div>Ma'lumotlar bazasi: <span class="text-slate-800">data/dbatool.db</span></div>
            </div>
          </div>
        </div>

        <!-- 10 Unlocked Modules Matrix -->
        <div class="space-y-2">
          <div class="text-xs font-bold text-slate-800">Faol Professional Modullar (100% Unlocked):</div>
          <div class="grid grid-cols-2 md:grid-cols-5 gap-2.5">
            <div
              v-for="mod in [
                'Inkremental Zaxira',
                'Differensial Zaxira',
                'AES-256 GCM Shifrlash',
                'Bulutli Omborlar (S3/GCS/Azure)',
                'GFS Saqlash Siyosati',
                'Baza Profilaktikasi & Vacuum',
                'Xabarnoma Kanallari (Telegram/Email)',
                'Sidecar v2 Checksumlar',
                'Zstandard Ko\'p Oqimli Siqish',
                'Avtomatik Hangfire Rejalar'
              ]"
              :key="mod"
              class="rounded-xl border border-emerald-200 bg-emerald-50/50 p-2.5 text-xs flex items-center gap-2 text-emerald-900 font-semibold"
            >
              <i class="pi pi-check-circle text-emerald-600 text-xs"></i>
              <span class="truncate">{{ mod }}</span>
            </div>
          </div>
        </div>

        <!-- Version Check Result Box if triggered -->
        <div v-if="versionCheckResult" class="rounded-xl border border-slate-200 bg-slate-50 p-4 space-y-2">
          <div class="text-xs font-bold text-slate-800">Eng So'nggi Versiya Ma'lumoti:</div>
          <pre class="max-h-40 overflow-y-auto rounded-lg bg-white p-3 font-mono text-[11px] text-slate-800 border border-slate-200">{{ JSON.stringify(versionCheckResult, null, 2) }}</pre>
        </div>
      </div>
    </div>

    <!-- BOTTOM BANNER: LINK TO DEDICATED EXECUTIONS MENU -->
    <div class="rounded-2xl border border-slate-200 bg-gradient-to-r from-slate-50 via-white to-indigo-50/40 p-5 shadow-2xs flex flex-col sm:flex-row sm:items-center justify-between gap-4">
      <div class="flex items-center gap-3.5">
        <div class="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-indigo-100 text-indigo-700">
          <i class="pi pi-history text-lg"></i>
        </div>
        <div>
          <h3 class="text-sm font-bold text-slate-900">Bajarilgan Amallar Tarixi va Auditi</h3>
          <p class="text-xs text-slate-600 mt-0.5">
            Barcha profilaktika, zaxiralash, yaxlitlik tekshiruvi va tozalash jarayonlarining batafsil auditi alohida menyuga ko'chirildi.
          </p>
        </div>
      </div>

      <RouterLink
        to="/executions"
        class="inline-flex items-center gap-2 rounded-xl bg-slate-900 px-4 py-2.5 text-xs font-semibold text-white shadow-md shadow-slate-900/10 hover:bg-slate-800 transition whitespace-nowrap cursor-pointer shrink-0"
      >
        <span>Amallar Tarixini Ko'rish</span>
        <i class="pi pi-arrow-right text-xs"></i>
      </RouterLink>
    </div>

    <!-- Modal: Install License -->
    <div
      v-if="showLicenseModal"
      class="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 backdrop-blur-xs p-4"
    >
      <div class="w-full max-w-md rounded-2xl border border-slate-200 bg-white p-6 shadow-xl space-y-4">
        <div class="flex items-center justify-between border-b border-slate-200 pb-3">
          <h3 class="text-sm font-bold text-slate-900 flex items-center gap-2">
            <i class="pi pi-key text-emerald-600"></i>
            Yangi Litsenziya O'rnatish
          </h3>
          <button
            @click="showLicenseModal = false"
            class="text-slate-400 hover:text-slate-700 cursor-pointer"
          >
            <i class="pi pi-times"></i>
          </button>
        </div>

        <div class="space-y-3 text-xs">
          <div>
            <label class="block font-semibold text-slate-700 mb-1">Litsenziya Kaliti (NSLv1 Token):</label>
            <textarea
              v-model="licenseKeyInput"
              rows="3"
              placeholder="dbb-lic-xxxx..."
              class="w-full rounded-xl border border-slate-300 p-2.5 font-mono text-xs focus:border-emerald-500 focus:outline-hidden"
            ></textarea>
          </div>
          <div>
            <label class="block font-semibold text-slate-700 mb-1">Yoki Litsenziya Fayli Yo'li:</label>
            <input
              type="text"
              v-model="licensePathInput"
              placeholder="/etc/dbb/license.key"
              class="w-full rounded-xl border border-slate-300 px-3 py-2 font-mono text-xs focus:border-emerald-500 focus:outline-hidden"
            />
          </div>
        </div>

        <div class="flex justify-end gap-2 border-t border-slate-200 pt-3">
          <button
            @click="showLicenseModal = false"
            class="rounded-xl border border-slate-200 bg-slate-50 px-4 py-2 text-xs font-semibold text-slate-700 hover:bg-slate-100 cursor-pointer"
          >
            Bekor Qilish
          </button>
          <button
            @click="installNewLicense"
            class="rounded-xl bg-emerald-600 px-4 py-2 text-xs font-semibold text-white hover:bg-emerald-500 cursor-pointer"
          >
            O'rnatish
          </button>
        </div>
      </div>
    </div>
  </div>
</template>
