<script setup lang="ts">
import { onMounted, ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import Chart from 'primevue/chart'
import {
  useDbaStore,
  formatBytes,
  formatDuration,
  formatDateTime,
  ENGINE_METADATA,
} from '@/stores/dbaStore'
import { api } from '@/services/api'
import QuickBackupModal from '@/components/QuickBackupModal.vue'
import RestoreWizardModal from '@/components/RestoreWizardModal.vue'
import type { BackupArtifact } from '@/types/models'

const store = useDbaStore()
const router = useRouter()
const actionBusy = ref(false)
const showQuickBackupModal = ref(false)
const showQuickRestoreModal = ref(false)
const activeRestoreArtifact = ref<BackupArtifact | null>(null)

const timeframeDays = ref<7 | 14 | 30>(7)

function openQuickRestore() {
  if (store.backups.length === 0) {
    store.notify('warn', 'No Backups', 'No backup artifacts exist yet to restore.')
    return
  }
  activeRestoreArtifact.value = store.backups[0]
  showQuickRestoreModal.value = true
}

onMounted(async () => {
  await Promise.allSettled([
    store.fetchDashboard(),
    store.fetchSystemInfo(),
    store.fetchJobs(),
    store.fetchBackups(),
    store.fetchExecutions(100),
    store.fetchAllProfiles(),
  ])
})

async function verifyAllBackups() {
  actionBusy.value = true
  try {
    if (store.backups.length === 0) {
      await store.fetchBackups()
    }
    const latest = store.backups[0]
    if (!latest) {
      store.notify('warn', 'No Backups Available', 'Run a backup first to verify integrity.')
      return
    }
    store.liveDrawerOpen = true
    await api.verifyBackup({
      filename: latest.filename,
      storageProfileId: latest.storageProfileId,
    })
    store.notify('info', 'Tekshiruv Boshlandi', `"${latest.filename}" zaxira faylining yaxlitligi tekshirilmoqda...`)
  } catch (e: unknown) {
    store.notify('error', 'Tekshirishda xatolik', (e as Error).message)
  } finally {
    actionBusy.value = false
  }
}

// ---------------- CHART 1: Timeline Volume & Runs Trend ----------------
const trendChartData = computed(() => {
  const days = timeframeDays.value
  const labels: string[] = []
  const volumeData: number[] = []
  const countData: number[] = []

  const now = new Date()
  for (let i = days - 1; i >= 0; i--) {
    const d = new Date(now)
    d.setDate(d.getDate() - i)
    const dateStr = d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
    labels.push(dateStr)

    const dayStart = new Date(d)
    dayStart.setHours(0, 0, 0, 0)
    const dayEnd = new Date(d)
    dayEnd.setHours(23, 59, 59, 999)

    // Executions on this day
    const dayExecs = (store.executions || []).filter((e) => {
      const execDate = new Date(e.startedAt)
      return execDate >= dayStart && execDate <= dayEnd
    })

    // Backups on this day
    const dayBackups = (store.backups || []).filter((b) => {
      const bDate = new Date(b.createdAt)
      return bDate >= dayStart && bDate <= dayEnd
    })

    let totalBytes = 0
    if (dayBackups.length > 0) {
      totalBytes = dayBackups.reduce((acc, b) => acc + (b.size || 0), 0)
    } else {
      totalBytes = dayExecs.reduce((acc, e) => acc + (e.outputSize || 0), 0)
    }
    const totalMb = parseFloat((totalBytes / (1024 * 1024)).toFixed(1))

    volumeData.push(totalMb)
    countData.push(dayExecs.length > 0 ? dayExecs.length : dayBackups.length)
  }

  return {
    labels,
    datasets: [
      {
        type: 'bar',
        label: 'Zaxira Hajmi (MB)',
        backgroundColor: '#10b981',
        hoverBackgroundColor: '#059669',
        borderRadius: 8,
        data: volumeData,
        yAxisID: 'yVolume',
      },
      {
        type: 'line',
        label: 'Jarayonlar Soni',
        borderColor: '#6366f1',
        backgroundColor: 'rgba(99, 102, 241, 0.12)',
        pointBackgroundColor: '#6366f1',
        pointBorderColor: '#ffffff',
        pointBorderWidth: 2,
        pointRadius: 4,
        tension: 0.35,
        fill: true,
        data: countData,
        yAxisID: 'yCount',
      },
    ],
  }
})

const trendChartOptions = {
  responsive: true,
  maintainAspectRatio: false,
  plugins: {
    legend: {
      position: 'top' as const,
      labels: {
        color: '#475569',
        font: { size: 11, weight: 'bold' as const },
        usePointStyle: true,
      },
    },
    tooltip: {
      mode: 'index' as const,
      intersect: false,
      padding: 10,
      cornerRadius: 10,
    },
  },
  scales: {
    x: {
      grid: { display: false },
      ticks: { color: '#64748b', font: { size: 10 } },
    },
    yVolume: {
      type: 'linear' as const,
      position: 'left' as const,
      beginAtZero: true,
      grid: { color: '#f1f5f9' },
      ticks: {
        color: '#64748b',
        font: { size: 10 },
        callback: (val: any) => `${val} MB`,
      },
      title: {
        display: true,
        text: 'Hajm (MB)',
        color: '#059669',
        font: { size: 10, weight: 'bold' as const },
      },
    },
    yCount: {
      type: 'linear' as const,
      position: 'right' as const,
      beginAtZero: true,
      grid: { display: false },
      ticks: {
        stepSize: 1,
        color: '#64748b',
        font: { size: 10 },
      },
      title: {
        display: true,
        text: 'Jarayonlar soni',
        color: '#4f46e5',
        font: { size: 10, weight: 'bold' as const },
      },
    },
  },
}

// ---------------- CHART 2: Engines Distribution (Doughnut) ----------------
const engineChartData = computed(() => {
  const counts: Record<string, number> = {}

  if (store.backups.length > 0) {
    for (const b of store.backups) {
      const eng = (b.engine || 'postgres').toLowerCase()
      counts[eng] = (counts[eng] || 0) + 1
    }
  }

  if (Object.keys(counts).length === 0) {
    for (const c of store.connections) {
      const eng = (c.type || 'postgres').toLowerCase()
      counts[eng] = (counts[eng] || 0) + 1
    }
  }

  if (Object.keys(counts).length === 0) {
    counts['mssql'] = 1
  }

  const engineColors: Record<string, string> = {
    postgres: '#336791',
    mysql: '#00758f',
    mariadb: '#003545',
    mssql: '#e42528',
    sqlite: '#003b57',
    mongo: '#13aa52',
    redis: '#dc382d',
    clickhouse: '#ffcc00',
    influx: '#22adf6',
    couch: '#e42528',
  }

  const labels = Object.keys(counts).map((eng) => ENGINE_METADATA[eng]?.label || eng.toUpperCase())
  const data = Object.values(counts)
  const backgroundColor = Object.keys(counts).map((eng) => engineColors[eng] || '#6366f1')

  return {
    labels,
    datasets: [
      {
        data,
        backgroundColor,
        borderWidth: 2,
        borderColor: '#ffffff',
        hoverOffset: 6,
      },
    ],
  }
})

const engineChartOptions = {
  responsive: true,
  maintainAspectRatio: false,
  cutout: '68%',
  plugins: {
    legend: {
      position: 'bottom' as const,
      labels: {
        color: '#475569',
        font: { size: 10, weight: 'bold' as const },
        usePointStyle: true,
        padding: 12,
      },
    },
    tooltip: {
      cornerRadius: 8,
      padding: 8,
    },
  },
}

// ---------------- CHART 3: Execution Reliability (Status Doughnut) ----------------
const statusChartData = computed(() => {
  const successful = store.summary?.successfulExecutions ?? 0
  const failed = store.summary?.failedExecutions ?? 0
  const cancelled = (store.executions || []).filter((e) => e.status === 'cancelled').length

  const sCount = successful > 0 ? successful : 1
  const fCount = failed
  const cCount = cancelled

  return {
    labels: ['Muvaffaqiyatli', 'Xatolik', 'To\'xtatilgan'],
    datasets: [
      {
        data: [sCount, fCount, cCount],
        backgroundColor: ['#10b981', '#ef4444', '#f59e0b'],
        borderWidth: 2,
        borderColor: '#ffffff',
        hoverOffset: 6,
      },
    ],
  }
})

const statusChartOptions = {
  responsive: true,
  maintainAspectRatio: false,
  cutout: '68%',
  plugins: {
    legend: {
      position: 'bottom' as const,
      labels: {
        color: '#475569',
        font: { size: 10, weight: 'bold' as const },
        usePointStyle: true,
        padding: 12,
      },
    },
  },
}

// ---------------- CHART 4: Storage Usage Breakdown (Horizontal Bar) ----------------
const storageChartData = computed(() => {
  const storageMap: Record<string, number> = {}

  if (store.backups.length > 0) {
    for (const b of store.backups) {
      const backend = (b.storageProfileName || 'Mahalliy Disk').toUpperCase()
      const mb = (b.size || 0) / (1024 * 1024)
      storageMap[backend] = (storageMap[backend] || 0) + mb
    }
  } else {
    for (const s of store.storageProfiles) {
      storageMap[s.name] = 0.5
    }
  }

  const labels = Object.keys(storageMap)
  const data = Object.values(storageMap).map((v) => parseFloat(v.toFixed(1)))

  return {
    labels,
    datasets: [
      {
        label: 'Disk Hajmi (MB)',
        backgroundColor: '#06b6d4',
        borderRadius: 8,
        data,
      },
    ],
  }
})

const storageChartOptions = {
  indexAxis: 'y' as const,
  responsive: true,
  maintainAspectRatio: false,
  plugins: {
    legend: { display: false },
    tooltip: {
      cornerRadius: 8,
      padding: 8,
    },
  },
  scales: {
    x: {
      grid: { color: '#f1f5f9' },
      ticks: {
        color: '#64748b',
        font: { size: 10 },
        callback: (val: any) => `${val} MB`,
      },
    },
    y: {
      grid: { display: false },
      ticks: {
        color: '#334155',
        font: { size: 11, weight: 'bold' as const },
      },
    },
  },
}

// ---------------- CHART 5: Strategy Ratio (Polar / Bar) ----------------
const strategyChartData = computed(() => {
  let fullCount = 0
  let incrCount = 0
  let diffCount = 0

  if (store.backups.length > 0) {
    for (const b of store.backups) {
      if (b.strategy === 'full') fullCount++
      else if (b.strategy === 'incremental') incrCount++
      else if (b.strategy === 'differential') diffCount++
      else fullCount++
    }
  } else {
    for (const j of store.jobs) {
      if (j.strategy === 'full') fullCount++
      else if (j.strategy === 'incremental') incrCount++
      else if (j.strategy === 'differential') diffCount++
      else fullCount++
    }
  }

  if (fullCount === 0 && incrCount === 0 && diffCount === 0) {
    fullCount = 1
  }

  return {
    labels: ['To\'liq (Full)', 'O\'zgarishlar (Incr)', 'Qiyosiy (Diff)'],
    datasets: [
      {
        label: 'Nusxalar Soni',
        data: [fullCount, incrCount, diffCount],
        backgroundColor: ['#10b981', '#3b82f6', '#8b5cf6'],
        borderRadius: 8,
      },
    ],
  }
})

const strategyChartOptions = {
  responsive: true,
  maintainAspectRatio: false,
  plugins: {
    legend: { display: false },
    tooltip: {
      cornerRadius: 8,
      padding: 8,
    },
  },
  scales: {
    x: {
      grid: { display: false },
      ticks: { color: '#334155', font: { size: 10, weight: 'bold' as const } },
    },
    y: {
      beginAtZero: true,
      grid: { color: '#f1f5f9' },
      ticks: {
        stepSize: 1,
        color: '#64748b',
        font: { size: 10 },
      },
    },
  },
}
</script>

<template>
  <div class="space-y-6">
    <!-- Top Banner & Quick Action Bar -->
    <div
      class="flex flex-col justify-between gap-4 rounded-2xl border border-slate-200 bg-gradient-to-r from-white via-emerald-50/30 to-indigo-50/40 p-5 shadow-2xs lg:flex-row lg:items-center"
    >
      <div>
        <div class="flex items-center gap-2">
          <span
            class="inline-flex items-center gap-1.5 rounded-full border border-emerald-200 bg-emerald-50 px-2.5 py-0.5 text-xs font-semibold text-emerald-700"
          >
            <i class="pi pi-shield"></i>
            Avtomatlashtirilgan Himoya Tizimi
          </span>
          <span class="text-xs text-slate-500 font-medium">
            Doimiy Nazorat &amp; Analitika Markazi
          </span>
        </div>
        <h1 class="mt-1.5 text-xl font-bold tracking-tight text-slate-900">
          Ma'lumotlar Bazalarini Zaxiralash &amp; Tiklash Boshqaruv Markazi
        </h1>
        <p class="mt-0.5 text-xs text-slate-600">
          Barcha ma'lumotlar bazalarining xavfsizligi, saqlash hajmi dinamikasi va analitik monitoring ko'rsatkichlari.
        </p>
      </div>

      <!-- Quick Action Bar -->
      <div class="flex flex-wrap items-center gap-2.5">
        <button
          @click="showQuickBackupModal = true"
          class="inline-flex items-center gap-2 rounded-xl bg-emerald-600 px-4 py-2.5 text-xs font-semibold text-white shadow-sm shadow-emerald-600/20 hover:bg-emerald-500 cursor-pointer transition"
        >
          <i class="pi pi-play"></i>
          ⚡ Tezkor Zaxiralash
        </button>
        <button
          @click="openQuickRestore"
          class="inline-flex items-center gap-2 rounded-xl border border-amber-300 bg-amber-50 px-3.5 py-2.5 text-xs font-semibold text-amber-800 hover:bg-amber-100 cursor-pointer transition"
        >
          <i class="pi pi-history"></i>
          Zudlik bilan Tiklash
        </button>
        <button
          @click="verifyAllBackups"
          :disabled="actionBusy"
          class="inline-flex items-center gap-2 rounded-xl border border-indigo-200 bg-indigo-50 px-3.5 py-2.5 text-xs font-semibold text-indigo-700 hover:bg-indigo-100 disabled:opacity-50 cursor-pointer transition"
        >
          <i class="pi pi-check-circle"></i>
          Yaxlitlikni Tekshirish
        </button>
        <button
          @click="router.push('/jobs')"
          class="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-white px-3.5 py-2.5 text-xs font-semibold text-slate-700 hover:bg-slate-50 cursor-pointer transition"
        >
          <i class="pi pi-briefcase"></i>
          Rejalarni Boshqarish
        </button>
      </div>
    </div>

    <!-- Top KPI Cards Strip -->
    <div class="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
      <!-- Total Backups & Storage -->
      <div class="rounded-2xl border border-slate-200 bg-white p-5 shadow-2xs">
        <div class="flex items-center justify-between">
          <span class="text-xs font-semibold uppercase tracking-wider text-slate-500">
            Jami Zaxiralar &amp; Hajm
          </span>
          <div
            class="flex h-9 w-9 items-center justify-center rounded-xl border border-emerald-200 bg-emerald-50 text-emerald-600"
          >
            <i class="pi pi-folder-open"></i>
          </div>
        </div>
        <div class="mt-3 flex items-baseline gap-2.5">
          <span class="text-2xl font-bold text-slate-900">
            {{ store.summary?.totalBackups ?? store.backups.length }}
          </span>
          <span class="text-xs text-slate-500">nusxalar</span>
        </div>
        <div class="mt-2 flex items-center justify-between border-t border-slate-100 pt-2.5 text-xs">
          <span class="text-slate-500">Egallangan disk:</span>
          <span class="font-mono font-semibold text-emerald-700">
            {{ formatBytes(store.summary?.totalStorageBytes ?? 0) }}
          </span>
        </div>
      </div>

      <!-- Configured Jobs -->
      <div class="rounded-2xl border border-slate-200 bg-white p-5 shadow-2xs">
        <div class="flex items-center justify-between">
          <span class="text-xs font-semibold uppercase tracking-wider text-slate-500">
            Zaxiralash Rejalari
          </span>
          <div
            class="flex h-9 w-9 items-center justify-center rounded-xl border border-indigo-200 bg-indigo-50 text-indigo-600"
          >
            <i class="pi pi-briefcase"></i>
          </div>
        </div>
        <div class="mt-3 flex items-baseline gap-2">
          <span class="text-2xl font-bold text-slate-900">
            {{ store.summary?.activeJobs ?? store.jobs.filter((j) => j.isEnabled).length }}
          </span>
          <span class="text-sm text-slate-500">
            / {{ store.summary?.totalJobs ?? store.jobs.length }} faol reja
          </span>
        </div>
        <div class="mt-2 flex items-center justify-between border-t border-slate-100 pt-2.5 text-xs">
          <span class="text-slate-500">Ulanish nuqtalari:</span>
          <span class="font-mono font-semibold text-indigo-700">
            {{ store.summary?.totalConnections ?? store.connections.length }} profiles
          </span>
        </div>
      </div>

      <!-- Success Rate & Total Runs -->
      <div class="rounded-2xl border border-slate-200 bg-white p-5 shadow-2xs">
        <div class="flex items-center justify-between">
          <span class="text-xs font-semibold uppercase tracking-wider text-slate-500">
            Ishonchlilik Darajasi
          </span>
          <div
            class="flex h-9 w-9 items-center justify-center rounded-xl border border-sky-200 bg-sky-50 text-sky-600"
          >
            <i class="pi pi-chart-bar"></i>
          </div>
        </div>
        <div class="mt-3 flex items-baseline gap-2">
          <span class="text-2xl font-bold text-slate-900">
            {{ (store.summary?.successRate ?? 100).toFixed(0) }}%
          </span>
          <span class="text-xs text-slate-500">muvaffaqiyat darajasi</span>
        </div>
        <div class="mt-2 flex items-center justify-between border-t border-slate-100 pt-2.5 text-xs">
          <span class="text-slate-500">Jami jarayonlar:</span>
          <span class="font-mono text-slate-700">
            <strong class="text-emerald-700">{{ store.summary?.successfulExecutions ?? 0 }}</strong> OK /
            <strong class="text-rose-600">{{ store.summary?.failedExecutions ?? 0 }}</strong> Xato
          </span>
        </div>
      </div>

      <!-- Himoya Dvigateli -->
      <div class="rounded-2xl border border-slate-200 bg-white p-5 shadow-2xs">
        <div class="flex items-center justify-between">
          <span class="text-xs font-semibold uppercase tracking-wider text-slate-500">
            Himoya Dvigateli
          </span>
          <div
            class="flex h-9 w-9 items-center justify-center rounded-xl border border-emerald-200 bg-emerald-50 text-emerald-700"
          >
            <i class="pi pi-shield"></i>
          </div>
        </div>
        <div class="mt-3 flex items-center gap-2">
          <span class="font-mono text-xl font-bold text-slate-900">
            v{{ store.systemInfo?.version || store.summary?.binaryVersion || '5.1.0' }}
          </span>
          <span
            class="rounded-full border border-emerald-200 bg-emerald-50 px-2 py-0.5 text-[11px] font-semibold uppercase text-emerald-700"
          >
            Supporter
          </span>
        </div>
        <div class="mt-2 flex items-center justify-between border-t border-slate-100 pt-2.5 text-xs">
          <span class="text-slate-500">Imkoniyatlar:</span>
          <span class="text-[11px] font-semibold text-emerald-700">
            100% Unlocked (To'liq Faol)
          </span>
        </div>
      </div>
    </div>

    <!-- MAIN CHARTS ROW 1: Timeline Volume & Runs Trend (8 cols) + Engines Breakdown (4 cols) -->
    <div class="grid grid-cols-1 gap-6 lg:grid-cols-12">
      <!-- 1. Zaxiralash Dinamikasi va Hajmlari (8 Cols) -->
      <div class="lg:col-span-8 rounded-2xl border border-slate-200 bg-white p-5 shadow-2xs flex flex-col justify-between">
        <div class="mb-4 flex flex-col sm:flex-row sm:items-center justify-between gap-3">
          <div>
            <div class="flex items-center gap-2">
              <i class="pi pi-chart-line text-emerald-600"></i>
              <h2 class="text-sm font-bold text-slate-900">Zaxiralash Dinamikasi va Hajmlari</h2>
            </div>
            <p class="text-xs text-slate-500 mt-0.5">
              Kunlik hosil bo'lgan zaxira hajmlari (MB) va muvaffaqiyatli bajarilgan jarayonlar soni
            </p>
          </div>

          <!-- Timeframe selector buttons -->
          <div class="flex items-center gap-1 rounded-xl border border-slate-200 bg-slate-50 p-1 self-start sm:self-auto">
            <button
              type="button"
              @click="timeframeDays = 7"
              class="rounded-lg px-2.5 py-1 text-xs font-semibold transition cursor-pointer"
              :class="timeframeDays === 7 ? 'bg-white text-emerald-800 shadow-2xs' : 'text-slate-600 hover:text-slate-900'"
            >
              7 kun
            </button>
            <button
              type="button"
              @click="timeframeDays = 14"
              class="rounded-lg px-2.5 py-1 text-xs font-semibold transition cursor-pointer"
              :class="timeframeDays === 14 ? 'bg-white text-emerald-800 shadow-2xs' : 'text-slate-600 hover:text-slate-900'"
            >
              14 kun
            </button>
            <button
              type="button"
              @click="timeframeDays = 30"
              class="rounded-lg px-2.5 py-1 text-xs font-semibold transition cursor-pointer"
              :class="timeframeDays === 30 ? 'bg-white text-emerald-800 shadow-2xs' : 'text-slate-600 hover:text-slate-900'"
            >
              30 kun
            </button>
          </div>
        </div>

        <!-- Chart Container -->
        <div class="h-72 w-full pt-2">
          <Chart type="bar" :data="trendChartData" :options="trendChartOptions" class="h-full w-full" />
        </div>
      </div>

      <!-- 2. Ma'lumotlar Bazasi Dvigatellari Taqsimoti (4 Cols) -->
      <div class="lg:col-span-4 rounded-2xl border border-slate-200 bg-white p-5 shadow-2xs flex flex-col justify-between">
        <div class="mb-2">
          <div class="flex items-center gap-2">
            <i class="pi pi-database text-indigo-600"></i>
            <h2 class="text-sm font-bold text-slate-900">Ma'lumotlar Bazalari Ulushi</h2>
          </div>
          <p class="text-xs text-slate-500 mt-0.5">
            Dvigatellar (Postgres, MSSQL, MySQL...) bo'yicha zaxira taqsimoti
          </p>
        </div>

        <!-- Doughnut Chart Container -->
        <div class="relative h-72 w-full flex items-center justify-center pt-2">
          <Chart type="doughnut" :data="engineChartData" :options="engineChartOptions" class="h-full w-full" />
          <div class="absolute inset-0 flex flex-col items-center justify-center pointer-events-none pb-12">
            <span class="text-xl font-extrabold text-slate-800 font-mono">
              {{ store.connections.length || store.backups.length || 1 }}
            </span>
            <span class="text-[10px] text-slate-400 font-semibold uppercase tracking-wider">
              Baza/Profil
            </span>
          </div>
        </div>
      </div>
    </div>

    <!-- MAIN CHARTS ROW 2: Reliability (4 cols) + Storage Usage (4 cols) + Strategies (4 cols) -->
    <div class="grid grid-cols-1 gap-6 md:grid-cols-2 lg:grid-cols-3">
      <!-- 3. Jarayonlar Natijadorligi (Reliability Gauge / Doughnut) -->
      <div class="rounded-2xl border border-slate-200 bg-white p-5 shadow-2xs flex flex-col justify-between">
        <div class="mb-2">
          <div class="flex items-center gap-2">
            <i class="pi pi-check-circle text-emerald-600"></i>
            <h2 class="text-sm font-bold text-slate-900">Jarayonlar Natijadorligi</h2>
          </div>
          <p class="text-xs text-slate-500 mt-0.5">
            Muvaffaqiyatli, xato va to'xtatilgan amallar nisbati
          </p>
        </div>

        <div class="relative h-60 w-full flex items-center justify-center pt-2">
          <Chart type="doughnut" :data="statusChartData" :options="statusChartOptions" class="h-full w-full" />
          <div class="absolute inset-0 flex flex-col items-center justify-center pointer-events-none pb-12">
            <span class="text-2xl font-black text-emerald-700 font-mono">
              {{ (store.summary?.successRate ?? 100).toFixed(0) }}%
            </span>
            <span class="text-[10px] text-slate-400 font-bold uppercase tracking-wider">
              Ishonchlilik
            </span>
          </div>
        </div>
      </div>

      <!-- 4. Saqlash Omborlari Disk Bandligi (Storage Distribution) -->
      <div class="rounded-2xl border border-slate-200 bg-white p-5 shadow-2xs flex flex-col justify-between">
        <div class="mb-2">
          <div class="flex items-center gap-2">
            <i class="pi pi-cloud-upload text-cyan-600"></i>
            <h2 class="text-sm font-bold text-slate-900">Saqlash Omborlari Bandligi</h2>
          </div>
          <p class="text-xs text-slate-500 mt-0.5">
            Filesystem, S3, MinIO va Azure omborlaridagi hajm (MB)
          </p>
        </div>

        <div class="h-60 w-full pt-2">
          <Chart type="bar" :data="storageChartData" :options="storageChartOptions" class="h-full w-full" />
        </div>
      </div>

      <!-- 5. Zaxira Strategiyalari Nisbati (Strategy Ratio) -->
      <div class="rounded-2xl border border-slate-200 bg-white p-5 shadow-2xs flex flex-col justify-between">
        <div class="mb-2">
          <div class="flex items-center gap-2">
            <i class="pi pi-sliders-h text-purple-600"></i>
            <h2 class="text-sm font-bold text-slate-900">Zaxira Strategiyalari</h2>
          </div>
          <p class="text-xs text-slate-500 mt-0.5">
            To'liq (Full) vs O'zgarishlar (Incr) vs Qiyosiy (Diff) nusxalar
          </p>
        </div>

        <div class="h-60 w-full pt-2">
          <Chart type="bar" :data="strategyChartData" :options="strategyChartOptions" class="h-full w-full" />
        </div>
      </div>
    </div>

    <!-- Bottom Status Notification Ribbon -->
    <div class="flex items-center justify-between rounded-xl border border-slate-200 bg-slate-50/70 px-5 py-3 text-xs text-slate-600">
      <div class="flex items-center gap-2 font-medium">
        <span class="h-2 w-2 rounded-full bg-emerald-500 animate-pulse"></span>
        <span>Barcha ma'lumotlar bazasi zaxira oqimlari real-vaqt monitoringi ostida faol ishlamoqda.</span>
      </div>
      <div class="flex items-center gap-3">
        <button
          @click="router.push('/backups')"
          class="font-semibold text-emerald-700 hover:text-emerald-800 hover:underline cursor-pointer"
        >
          Zaxiralar Arxivini Ko'rish &rarr;
        </button>
      </div>
    </div>

    <!-- Quick Action Modals -->
    <QuickBackupModal :show="showQuickBackupModal" @close="showQuickBackupModal = false" />
    <RestoreWizardModal
      :show="showQuickRestoreModal"
      :artifact="activeRestoreArtifact"
      @close="showQuickRestoreModal = false"
    />
  </div>
</template>
