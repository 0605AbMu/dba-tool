<script setup lang="ts">
import { onMounted, onUnmounted, ref, computed } from 'vue'
import {
  useDbaStore,
  formatBytes,
  formatDuration,
  formatDateTime,
} from '@/stores/dbaStore'
import { api } from '@/services/api'
import type { JobExecution } from '@/types/models'

const store = useDbaStore()

// State
const filterStatus = ref<string>('all')
const filterCommand = ref<string>('all')
const searchQuery = ref<string>('')
const limitCount = ref<number>(50)
const loading = ref(false)
const autoRefresh = ref(true)
let refreshInterval: number | null = null

// Selected execution for inspect modal
const selectedExecution = ref<JobExecution | null>(null)
const showDetailModal = ref(false)

onMounted(async () => {
  await fetchExecutions()
  refreshInterval = window.setInterval(() => {
    if (autoRefresh.value && store.activeStatus !== 'running') {
      fetchExecutions(true)
    }
  }, 10000)
})

onUnmounted(() => {
  if (refreshInterval) {
    clearInterval(refreshInterval)
    refreshInterval = null
  }
})

async function fetchExecutions(silent = false) {
  if (!silent) loading.value = true
  try {
    await store.fetchExecutions(limitCount.value)
  } catch (e: unknown) {
    if (!silent) {
      store.notify('error', 'Yuklashda xatolik', (e as Error).message)
    }
  } finally {
    if (!silent) loading.value = false
  }
}

function onLimitChange() {
  fetchExecutions()
}

// Filtered executions list
const filteredExecutions = computed(() => {
  return store.executions.filter((ex) => {
    // Status filter
    if (filterStatus.value !== 'all' && ex.status !== filterStatus.value) {
      return false
    }
    // Command type filter
    if (filterCommand.value !== 'all' && ex.commandType !== filterCommand.value) {
      return false
    }
    // Search query
    if (searchQuery.value.trim()) {
      const q = searchQuery.value.toLowerCase().trim()
      const name = (ex.jobName || '').toLowerCase()
      const cmd = (ex.cliCommand || '').toLowerCase()
      const trigger = (ex.trigger || '').toLowerCase()
      const id = String(ex.id)
      if (!name.includes(q) && !cmd.includes(q) && !trigger.includes(q) && !id.includes(q)) {
        return false
      }
    }
    return true
  })
})

// KPI Metrics
const totalCount = computed(() => store.executions.length)
const completedCount = computed(() => store.executions.filter((e) => e.status === 'completed').length)
const failedCount = computed(() => store.executions.filter((e) => e.status === 'failed').length)
const runningCount = computed(() => store.executions.filter((e) => e.status === 'running').length)
const successRate = computed(() => {
  const finished = completedCount.value + failedCount.value
  if (finished === 0) return 100
  return ((completedCount.value / finished) * 100).toFixed(1)
})

function viewExecutionLogs(ex: JobExecution) {
  store.openExecutionInDrawer(ex.id)
}

function openDetailModal(ex: JobExecution) {
  selectedExecution.value = ex
  showDetailModal.value = true
}

async function cancelExecution(ex: JobExecution) {
  try {
    await api.cancelExecution(ex.id)
    store.notify('warn', 'Jarayon to\'xtatilmoqda', `#${ex.id} jarayoni bekor qilish so'rovi yuborildi.`)
    await fetchExecutions(true)
  } catch (e: unknown) {
    store.notify('error', 'Bekor qilishda xatolik', (e as Error).message)
  }
}

function getCommandBadgeClass(cmd: string): string {
  switch (cmd) {
    case 'dump':
    case 'backup':
      return 'bg-blue-50 text-blue-800 border-blue-200'
    case 'restore':
      return 'bg-purple-50 text-purple-800 border-purple-200'
    case 'maintain':
      return 'bg-emerald-50 text-emerald-800 border-emerald-200'
    case 'verify':
      return 'bg-indigo-50 text-indigo-800 border-indigo-200'
    case 'prune':
      return 'bg-amber-50 text-amber-800 border-amber-200'
    case 'archive':
      return 'bg-cyan-50 text-cyan-800 border-cyan-200'
    case 'stats':
      return 'bg-teal-50 text-teal-800 border-teal-200'
    default:
      return 'bg-slate-100 text-slate-800 border-slate-200'
  }
}
</script>

<template>
  <div class="space-y-6">
    <!-- Header -->
    <div class="flex flex-col justify-between gap-4 sm:flex-row sm:items-center">
      <div>
        <h1 class="text-xl font-bold tracking-tight text-slate-900 flex items-center gap-2.5">
          <span class="flex h-9 w-9 items-center justify-center rounded-xl bg-indigo-50 text-indigo-700 border border-indigo-200">
            <i class="pi pi-history text-base"></i>
          </span>
          Bajarilgan Amallar va Jarayonlar Tarixi
        </h1>
        <p class="text-xs text-slate-600 mt-1">
          Barcha zaxiralash, tiklash, texnik xizmat, yaxlitlik tekshiruvi va tozalash jarayonlarining to'liq auditi va jurnali.
        </p>
      </div>

      <div class="flex items-center gap-2.5">
        <label class="flex items-center gap-1.5 text-xs text-slate-600 cursor-pointer select-none">
          <input type="checkbox" v-model="autoRefresh" class="h-3.5 w-3.5 rounded accent-emerald-600" />
          <span>Avto-yangilanish (10s)</span>
        </label>

        <button
          @click="fetchExecutions(false)"
          :disabled="loading"
          class="inline-flex items-center gap-1.5 rounded-xl border border-slate-200 bg-white px-3.5 py-2 text-xs font-semibold text-slate-700 shadow-2xs hover:bg-slate-50 transition cursor-pointer"
        >
          <i class="pi pi-refresh" :class="{ 'pi-spin': loading }"></i>
          <span>Yangilash</span>
        </button>
      </div>
    </div>

    <!-- KPI Metric Cards -->
    <div class="grid grid-cols-2 lg:grid-cols-5 gap-3.5">
      <div class="rounded-2xl border border-slate-200 bg-white p-4 shadow-2xs space-y-1">
        <div class="text-[11px] font-semibold uppercase tracking-wider text-slate-500">Jami Amallar</div>
        <div class="text-2xl font-bold font-mono text-slate-900">{{ totalCount }}</div>
        <div class="text-[11px] text-slate-500">Yuklangan yozuvlar</div>
      </div>

      <div class="rounded-2xl border border-emerald-200 bg-emerald-50/40 p-4 shadow-2xs space-y-1">
        <div class="text-[11px] font-semibold uppercase tracking-wider text-emerald-800">Muvaffaqiyatli</div>
        <div class="text-2xl font-bold font-mono text-emerald-700">{{ completedCount }}</div>
        <div class="text-[11px] text-emerald-600 font-semibold">{{ successRate }}% muvaffaqiyat darajasi</div>
      </div>

      <div class="rounded-2xl border border-rose-200 bg-rose-50/40 p-4 shadow-2xs space-y-1">
        <div class="text-[11px] font-semibold uppercase tracking-wider text-rose-800">Xatoliklar</div>
        <div class="text-2xl font-bold font-mono text-rose-700">{{ failedCount }}</div>
        <div class="text-[11px] text-rose-600">Tekshirish talab qilinadi</div>
      </div>

      <div class="rounded-2xl border border-amber-200 bg-amber-50/40 p-4 shadow-2xs space-y-1">
        <div class="text-[11px] font-semibold uppercase tracking-wider text-amber-800">Bajarilmoqda</div>
        <div class="text-2xl font-bold font-mono text-amber-700 flex items-center gap-2">
          <span>{{ runningCount }}</span>
          <span v-if="runningCount > 0" class="h-2.5 w-2.5 rounded-full bg-amber-500 animate-ping"></span>
        </div>
        <div class="text-[11px] text-amber-600">Faol vazifalar</div>
      </div>

      <div class="rounded-2xl border border-slate-200 bg-white p-4 shadow-2xs space-y-1">
        <div class="text-[11px] font-semibold uppercase tracking-wider text-slate-500">So'nggi Holat</div>
        <div class="text-base font-bold text-slate-800 truncate">
          {{ store.executions.length > 0 ? store.executions[0].jobName : 'Mavjud emas' }}
        </div>
        <div class="text-[11px] text-slate-500 font-mono truncate">
          {{ store.executions.length > 0 ? formatDateTime(store.executions[0].startedAt) : '-' }}
        </div>
      </div>
    </div>

    <!-- Filter & Search Controls -->
    <div class="rounded-2xl border border-slate-200 bg-white p-4 shadow-2xs space-y-3">
      <div class="flex flex-col md:flex-row md:items-center justify-between gap-3">
        <!-- Search Input -->
        <div class="relative flex-1 max-w-md">
          <i class="pi pi-search absolute left-3.5 top-1/2 -translate-y-1/2 text-slate-400 text-xs"></i>
          <input
            v-model="searchQuery"
            type="text"
            placeholder="Reja nomi, buyruq yoki ID bo'yicha qidirish..."
            class="w-full rounded-xl border border-slate-200 bg-slate-50 pl-9 pr-3.5 py-2 text-xs text-slate-900 focus:border-emerald-500 focus:bg-white focus:outline-hidden"
          />
        </div>

        <div class="flex flex-wrap items-center gap-2">
          <!-- Command Type Filter -->
          <select
            v-model="filterCommand"
            class="rounded-xl border border-slate-200 bg-slate-50 px-3 py-2 text-xs font-semibold text-slate-700 focus:border-emerald-500 focus:outline-hidden"
          >
            <option value="all">Barcha Amallar</option>
            <option value="dump">Zaxiralash (Dump)</option>
            <option value="restore">Tiklash (Restore)</option>
            <option value="maintain">Profilaktika (Maintain)</option>
            <option value="verify">Yaxlitlik (Verify)</option>
            <option value="prune">Tozalash (Prune)</option>
            <option value="archive">Arxivlash (Archive)</option>
            <option value="stats">Statistika (Stats)</option>
          </select>

          <!-- Status Filter -->
          <select
            v-model="filterStatus"
            class="rounded-xl border border-slate-200 bg-slate-50 px-3 py-2 text-xs font-semibold text-slate-700 focus:border-emerald-500 focus:outline-hidden"
          >
            <option value="all">Barcha Holatlar</option>
            <option value="completed">Muvaffaqiyatli</option>
            <option value="running">Bajarilmoqda</option>
            <option value="failed">Xatolik</option>
          </select>

          <!-- Limit Selector -->
          <select
            v-model.number="limitCount"
            @change="onLimitChange"
            class="rounded-xl border border-slate-200 bg-slate-50 px-3 py-2 text-xs font-semibold text-slate-700 focus:border-emerald-500 focus:outline-hidden"
          >
            <option :value="25">25 ta</option>
            <option :value="50">50 ta</option>
            <option :value="100">100 ta</option>
            <option :value="200">200 ta</option>
          </select>
        </div>
      </div>
    </div>

    <!-- Execution Table -->
    <div class="rounded-2xl border border-slate-200 bg-white overflow-hidden shadow-2xs">
      <div class="overflow-x-auto">
        <table class="w-full text-left text-xs">
          <thead class="border-b border-slate-200 bg-slate-50 font-semibold text-slate-600 uppercase tracking-wider text-[10px]">
            <tr>
              <th class="px-4 py-3.5">ID</th>
              <th class="px-4 py-3.5">Amal Turi</th>
              <th class="px-4 py-3.5">Nishon Reja / Baza</th>
              <th class="px-4 py-3.5">Ishga Tushiruvchi</th>
              <th class="px-4 py-3.5">Holat</th>
              <th class="px-4 py-3.5">Davomiyligi</th>
              <th class="px-4 py-3.5">Boshlangan Vaqt</th>
              <th class="px-4 py-3.5">Fayl / Hajm</th>
              <th class="px-4 py-3.5 text-right">Amallar</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-slate-100">
            <tr v-if="filteredExecutions.length === 0">
              <td colspan="9" class="px-4 py-12 text-center text-slate-400">
                <i class="pi pi-inbox text-3xl block mb-2 text-slate-300"></i>
                Filtrga mos keluvchi jarayonlar topilmadi.
              </td>
            </tr>

            <tr
              v-for="ex in filteredExecutions"
              :key="ex.id"
              class="hover:bg-slate-50 transition"
            >
              <!-- Execution ID -->
              <td class="px-4 py-3 font-mono font-bold text-slate-900">#{{ ex.id }}</td>

              <!-- Command Type Badge -->
              <td class="px-4 py-3">
                <span
                  class="rounded-md border px-2 py-0.5 font-mono text-[10px] font-bold uppercase tracking-wider"
                  :class="getCommandBadgeClass(ex.commandType)"
                >
                  {{ ex.commandType }}
                </span>
              </td>

              <!-- Target Name -->
              <td class="px-4 py-3 font-semibold text-slate-900">
                <div class="truncate max-w-[180px]">{{ ex.jobName || 'Tizim Amali' }}</div>
                <div v-if="ex.isDryRun" class="text-[10px] font-normal text-amber-700">
                  (Simulyatsiya - Dry Run)
                </div>
              </td>

              <!-- Trigger -->
              <td class="px-4 py-3">
                <span
                  class="rounded px-1.5 py-0.5 text-[10px] font-medium"
                  :class="{
                    'bg-slate-100 text-slate-700': ex.trigger === 'manual',
                    'bg-indigo-50 text-indigo-700': ex.trigger === 'schedule' || ex.trigger === 'cron',
                    'bg-emerald-50 text-emerald-700': ex.trigger === 'hangfire',
                  }"
                >
                  {{ ex.trigger }}
                </span>
              </td>

              <!-- Status -->
              <td class="px-4 py-3">
                <span
                  class="inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-[11px] font-semibold"
                  :class="{
                    'bg-emerald-100 text-emerald-800': ex.status === 'completed',
                    'bg-amber-100 text-amber-800 animate-pulse': ex.status === 'running',
                    'bg-rose-100 text-rose-800': ex.status === 'failed',
                    'bg-slate-100 text-slate-600': ex.status === 'cancelled',
                  }"
                >
                  <span
                    class="h-1.5 w-1.5 rounded-full"
                    :class="{
                      'bg-emerald-500': ex.status === 'completed',
                      'bg-amber-500': ex.status === 'running',
                      'bg-rose-500': ex.status === 'failed',
                      'bg-slate-400': ex.status === 'cancelled',
                    }"
                  ></span>
                  {{ ex.status }}
                </span>
              </td>

              <!-- Duration -->
              <td class="px-4 py-3 font-mono text-slate-600">
                {{ formatDuration(ex.durationMs) }}
              </td>

              <!-- Started At -->
              <td class="px-4 py-3 text-slate-500">
                {{ formatDateTime(ex.startedAt) }}
              </td>

              <!-- Output size / file -->
              <td class="px-4 py-3">
                <div v-if="ex.outputSize" class="font-mono font-semibold text-slate-800">
                  {{ formatBytes(ex.outputSize) }}
                </div>
                <div v-else-if="ex.outputFile" class="font-mono text-[10px] text-slate-500 truncate max-w-[120px]">
                  {{ ex.outputFile }}
                </div>
                <div v-else class="text-slate-400 text-[11px]">-</div>
              </td>

              <!-- Actions -->
              <td class="px-4 py-3 text-right">
                <div class="flex items-center justify-end gap-1.5">
                  <button
                    @click="viewExecutionLogs(ex)"
                    class="rounded-lg border border-slate-200 bg-white px-2.5 py-1 font-semibold text-slate-700 hover:bg-slate-50 hover:border-slate-300 transition cursor-pointer"
                    title="Jurnalni ko'rish"
                  >
                    Jurnal
                  </button>

                  <button
                    @click="openDetailModal(ex)"
                    class="rounded-lg border border-slate-200 bg-white px-2 py-1 text-slate-500 hover:text-slate-900 hover:bg-slate-50 transition cursor-pointer"
                    title="Tafsilotlar"
                  >
                    <i class="pi pi-info-circle"></i>
                  </button>

                  <button
                    v-if="ex.status === 'running'"
                    @click="cancelExecution(ex)"
                    class="rounded-lg border border-rose-300 bg-rose-50 px-2 py-1 text-rose-700 hover:bg-rose-100 transition cursor-pointer"
                    title="Bekor qilish"
                  >
                    <i class="pi pi-stop-circle"></i>
                  </button>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <!-- Execution Details Modal -->
    <div
      v-if="showDetailModal && selectedExecution"
      class="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 backdrop-blur-xs p-4"
    >
      <div class="w-full max-w-2xl rounded-2xl border border-slate-200 bg-white p-6 shadow-xl space-y-4">
        <div class="flex items-center justify-between border-b border-slate-200 pb-3">
          <div>
            <h3 class="text-sm font-bold text-slate-900 flex items-center gap-2">
              <i class="pi pi-info-circle text-indigo-600"></i>
              Jarayon Tafsilotlari #{{ selectedExecution.id }}
            </h3>
            <div class="text-[11px] text-slate-500 mt-0.5">
              {{ selectedExecution.jobName }} • {{ selectedExecution.commandType }}
            </div>
          </div>
          <button
            @click="showDetailModal = false"
            class="text-slate-400 hover:text-slate-700 cursor-pointer"
          >
            <i class="pi pi-times"></i>
          </button>
        </div>

        <div class="grid grid-cols-2 sm:grid-cols-3 gap-3 text-xs">
          <div class="rounded-xl border border-slate-200 bg-slate-50 p-3">
            <span class="text-[11px] text-slate-500 block">Holat</span>
            <span class="font-bold uppercase tracking-wider text-slate-900">{{ selectedExecution.status }}</span>
          </div>

          <div class="rounded-xl border border-slate-200 bg-slate-50 p-3">
            <span class="text-[11px] text-slate-500 block">Davomiyligi</span>
            <span class="font-bold font-mono text-slate-900">{{ formatDuration(selectedExecution.durationMs) }}</span>
          </div>

          <div class="rounded-xl border border-slate-200 bg-slate-50 p-3">
            <span class="text-[11px] text-slate-500 block">Exit Code</span>
            <span class="font-bold font-mono" :class="selectedExecution.exitCode === 0 ? 'text-emerald-700' : 'text-rose-700'">
              {{ selectedExecution.exitCode ?? 'N/A' }}
            </span>
          </div>

          <div class="rounded-xl border border-slate-200 bg-slate-50 p-3">
            <span class="text-[11px] text-slate-500 block">Boshlangan</span>
            <span class="font-medium text-slate-800">{{ formatDateTime(selectedExecution.startedAt) }}</span>
          </div>

          <div class="rounded-xl border border-slate-200 bg-slate-50 p-3">
            <span class="text-[11px] text-slate-500 block">Tugallangan</span>
            <span class="font-medium text-slate-800">{{ formatDateTime(selectedExecution.finishedAt) }}</span>
          </div>

          <div class="rounded-xl border border-slate-200 bg-slate-50 p-3">
            <span class="text-[11px] text-slate-500 block">Trigger</span>
            <span class="font-semibold text-slate-800">{{ selectedExecution.trigger }}</span>
          </div>
        </div>

        <!-- CLI Command -->
        <div>
          <label class="block text-xs font-semibold text-slate-700 mb-1">Bajarilgan Buyruq:</label>
          <pre class="max-h-24 overflow-x-auto rounded-xl border border-slate-200 bg-slate-50 p-3 font-mono text-xs text-slate-900">{{ selectedExecution.cliCommand }}</pre>
        </div>

        <!-- Error details if any -->
        <div v-if="selectedExecution.errorDetails" class="rounded-xl border border-rose-200 bg-rose-50 p-3 text-xs text-rose-900">
          <div class="font-bold flex items-center gap-1.5 mb-1">
            <i class="pi pi-exclamation-triangle text-rose-600"></i> Xatolik Tafsiloti:
          </div>
          <div class="font-mono break-all">{{ selectedExecution.errorDetails }}</div>
        </div>

        <div class="flex justify-end gap-2 border-t border-slate-200 pt-3">
          <button
            @click="showDetailModal = false"
            class="rounded-xl border border-slate-200 bg-slate-50 px-4 py-2 text-xs font-semibold text-slate-700 hover:bg-slate-100 cursor-pointer"
          >
            Yopish
          </button>
          <button
            @click="showDetailModal = false; viewExecutionLogs(selectedExecution)"
            class="rounded-xl bg-emerald-600 px-4 py-2 text-xs font-semibold text-white hover:bg-emerald-500 cursor-pointer"
          >
            To'liq Jurnalni Ochish
          </button>
        </div>
      </div>
    </div>
  </div>
</template>
