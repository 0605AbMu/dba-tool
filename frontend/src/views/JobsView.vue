<script setup lang="ts">
import { onMounted, ref, computed } from 'vue'
import { useDbaStore, ENGINE_METADATA, formatDateTime } from '@/stores/dbaStore'
import { api } from '@/services/api'
import type { BackupJob } from '@/types/models'
import BackupJobWizardModal from '@/components/BackupJobWizardModal.vue'

const store = useDbaStore()

// State
const showWizard = ref(false)
const selectedJobForEdit = ref<BackupJob | null>(null)
const searchQuery = ref('')
const filterEngine = ref('all')
const filterStatus = ref('all')

onMounted(async () => {
  await Promise.allSettled([store.fetchAllProfiles(), store.fetchJobs()])
})

function openCreateWizard() {
  selectedJobForEdit.value = null
  showWizard.value = true
}

function openEditWizard(job: BackupJob) {
  selectedJobForEdit.value = { ...job }
  showWizard.value = true
}

function onJobSaved() {
  store.fetchJobs()
}

async function runJobNow(job: BackupJob, dryRun = false) {
  try {
    store.liveDrawerOpen = true
    await api.runJob(job.id, dryRun)
    store.notify(
      'info',
      dryRun ? 'Sinov Boshlandi' : 'Zaxiralash Boshlandi',
      `"${job.name}" bo'yicha zaxira jarayoni bajarilmoqda...`
    )
  } catch (e: unknown) {
    store.notify('error', 'Jarayonni boshlashda xatolik', (e as Error).message)
  }
}

async function runMaintenanceNow(job: BackupJob) {
  try {
    store.liveDrawerOpen = true
    await api.maintainJob(job.id)
    store.notify('info', 'Profilaktika Boshlandi', `"${job.name}" bo'yicha ma'lumotlar bazasi profilaktikasi bajarilmoqda...`)
  } catch (e: unknown) {
    store.notify('error', 'Profilaktikada xatolik', (e as Error).message)
  }
}

async function toggleJobStatus(job: BackupJob) {
  try {
    const updated = await api.toggleJob(job.id)
    job.isEnabled = updated.isEnabled
    store.notify(
      'success',
      job.isEnabled ? 'Reja Faollashtirildi' : 'Reja To\'xtatildi',
      `Avtomatik reja ${job.isEnabled ? 'faollashtirildi' : 'to\'xtatildi'}: "${job.name}"`
    )
  } catch (e: unknown) {
    store.notify('error', 'Holatni o\'zgartirishda xatolik', (e as Error).message)
  }
}

async function deleteJobItem(job: BackupJob) {
  if (!confirm(`"${job.name}" zaxira rejasini o'chirishni tasdiqlaysizmi? Bu uning barcha avtomatik jadvallarini ham to'xtatadi.`)) return
  try {
    await api.deleteJob(job.id)
    store.notify('success', 'Reja O\'chirildi', `"${job.name}" muvaffaqiyatli o'chirildi.`)
    await store.fetchJobs()
  } catch (e: unknown) {
    store.notify('error', 'O\'chirishda xatolik', (e as Error).message)
  }
}

function getJobEngine(job: BackupJob): string {
  if (job.connectionProfile?.type) return job.connectionProfile.type
  const conn = store.connections.find((c) => c.id === job.connectionProfileId)
  return conn?.type || 'postgres'
}

function getJobConnectionName(job: BackupJob): string {
  if (job.connectionProfile?.name) return job.connectionProfile.name
  const conn = store.connections.find((c) => c.id === job.connectionProfileId)
  return conn?.name || `Conn #${job.connectionProfileId}`
}

function getJobEncryptionType(job: BackupJob): string {
  if (!job.encryptionProfileId) return 'none'
  const enc = store.encryptionProfiles.find((e) => e.id === job.encryptionProfileId)
  return enc?.type || 'encrypted'
}

function formatCronHuman(cron?: string | null): string {
  if (!cron || !cron.trim()) return 'Faqat qo\'lda (Manual)'
  const parts = cron.trim().split(/\s+/)
  if (parts.length !== 5) return cron

  const [min, hour, dom, mon, dow] = parts
  if (min === '0' && hour === '*' && dom === '*' && mon === '*' && dow === '*') return 'Har soatda'
  if (min === '0' && hour.startsWith('*/') && dom === '*' && mon === '*' && dow === '*') {
    return `Har ${hour.replace('*/', '')} soatda`
  }
  if (dom === '*' && mon === '*' && dow === '*' && !hour.includes('*')) {
    return `Har kuni soat ${hour.padStart(2, '0')}:${min.padStart(2, '0')} da`
  }
  if (dow === '0' && !hour.includes('*')) {
    return `Har hafta yakshanba ${hour.padStart(2, '0')}:${min.padStart(2, '0')} da`
  }
  return cron
}

// Filtered Jobs
const filteredJobs = computed(() => {
  return store.jobs.filter((j) => {
    if (searchQuery.value.trim()) {
      const q = searchQuery.value.toLowerCase()
      const matchName = j.name.toLowerCase().includes(q)
      const matchDb = (j.databaseNames || '').toLowerCase().includes(q)
      const matchDesc = (j.description || '').toLowerCase().includes(q)
      if (!matchName && !matchDb && !matchDesc) return false
    }

    if (filterEngine.value !== 'all') {
      if (getJobEngine(j) !== filterEngine.value) return false
    }

    if (filterStatus.value === 'active' && !j.isEnabled) return false
    if (filterStatus.value === 'disabled' && j.isEnabled) return false

    return true
  })
})
</script>

<template>
  <div class="space-y-5">
    <!-- Header -->
    <div class="flex flex-col justify-between gap-4 sm:flex-row sm:items-center">
      <div>
        <h1 class="text-xl font-bold text-slate-900">Zaxiralash va Profilaktika Rejalari</h1>
        <p class="text-xs text-slate-600">
          Avtomatlashtirilgan zaxiralash jadvallari, saqlash muddatlari va profilaktika amallari.
        </p>
      </div>
      <div class="flex items-center gap-2.5">
        <button
          @click="store.fetchJobs()"
          class="rounded-xl border border-slate-200 bg-white px-3.5 py-2 text-xs font-semibold text-slate-700 hover:bg-slate-50 cursor-pointer shadow-2xs"
        >
          <i class="pi pi-refresh mr-1.5"></i>Refresh
        </button>
        <button
          @click="openCreateWizard"
          class="inline-flex items-center gap-2 rounded-xl bg-emerald-600 px-4 py-2 text-xs font-semibold text-white shadow-sm shadow-emerald-600/20 hover:bg-emerald-500 cursor-pointer"
        >
          <i class="pi pi-plus"></i>
          Create Backup Job
        </button>
      </div>
    </div>

    <!-- Filter & Search Toolbar -->
    <div
      class="flex flex-col gap-3 rounded-2xl border border-slate-200 bg-white p-3.5 shadow-2xs sm:flex-row sm:items-center sm:justify-between"
    >
      <div class="relative flex-1 max-w-md">
        <i class="pi pi-search absolute left-3.5 top-1/2 -translate-y-1/2 text-slate-400 text-xs"></i>
        <input
          type="text"
          v-model="searchQuery"
          placeholder="Search jobs by name, database, or description..."
          class="w-full rounded-xl border border-slate-200 bg-slate-50/70 pl-9 pr-3.5 py-1.5 text-xs text-slate-800 placeholder-slate-400 focus:border-emerald-500 focus:bg-white focus:outline-hidden"
        />
      </div>

      <div class="flex flex-wrap items-center gap-2 text-xs">
        <!-- Engine Filter -->
        <select
          v-model="filterEngine"
          class="rounded-xl border border-slate-200 bg-slate-50 px-3 py-1.5 font-semibold text-slate-700 focus:outline-hidden"
        >
          <option value="all">All Database Engines</option>
          <option v-for="(meta, engine) in ENGINE_METADATA" :key="engine" :value="engine">
            {{ meta.label }}
          </option>
        </select>

        <!-- Status Filter -->
        <select
          v-model="filterStatus"
          class="rounded-xl border border-slate-200 bg-slate-50 px-3 py-1.5 font-semibold text-slate-700 focus:outline-hidden"
        >
          <option value="all">All Statuses</option>
          <option value="active">Active Only</option>
          <option value="disabled">Paused Only</option>
        </select>
      </div>
    </div>

    <!-- Empty State -->
    <div
      v-if="filteredJobs.length === 0"
      class="rounded-2xl border border-dashed border-slate-300 bg-white p-12 text-center"
    >
      <div class="mx-auto flex h-12 w-12 items-center justify-center rounded-2xl bg-slate-100 text-slate-400">
        <i class="pi pi-briefcase text-xl"></i>
      </div>
      <h3 class="mt-3 text-sm font-bold text-slate-800">No Backup Jobs Found</h3>
      <p class="mt-1 text-xs text-slate-500 max-w-sm mx-auto">
        {{ store.jobs.length === 0 ? 'Create your first automated backup pipeline in just 2 minutes using our guided wizard.' : 'No jobs matched your current filter criteria.' }}
      </p>
      <button
        v-if="store.jobs.length === 0"
        @click="openCreateWizard"
        class="mt-4 inline-flex items-center gap-2 rounded-xl bg-emerald-600 px-4 py-2 text-xs font-semibold text-white shadow-sm hover:bg-emerald-500 cursor-pointer"
      >
        <i class="pi pi-plus"></i>
        Launch Backup Wizard
      </button>
    </div>

    <!-- Job Cards Grid -->
    <div v-else class="grid grid-cols-1 gap-4">
      <div
        v-for="job in filteredJobs"
        :key="job.id"
        class="rounded-2xl border border-slate-200 bg-white p-5 shadow-2xs transition hover:border-slate-300"
      >
        <div class="flex flex-col justify-between gap-4 lg:flex-row lg:items-center">
          <!-- Left: Identity & Badges -->
          <div class="space-y-2.5">
            <div class="flex flex-wrap items-center gap-2">
              <!-- Engine Badge -->
              <span
                class="inline-flex items-center gap-1.5 rounded-lg border px-2.5 py-1 text-xs font-semibold"
                :class="
                  ENGINE_METADATA[getJobEngine(job)]?.badgeClass ||
                  'bg-slate-100 text-slate-700 border-slate-200'
                "
              >
                <i :class="ENGINE_METADATA[getJobEngine(job)]?.icon || 'pi pi-database'"></i>
                {{ ENGINE_METADATA[getJobEngine(job)]?.label || getJobEngine(job) }}
              </span>

              <h3 class="font-mono text-base font-bold text-slate-900">{{ job.name }}</h3>

              <!-- Active Status Badge -->
              <span
                class="rounded-full px-2.5 py-0.5 text-[11px] font-semibold"
                :class="
                  job.isEnabled
                    ? 'bg-emerald-50 text-emerald-700 border border-emerald-200'
                    : 'bg-slate-100 text-slate-500 border border-slate-200'
                "
              >
                {{ job.isEnabled ? 'Active' : 'Paused' }}
              </span>

              <!-- Strategy Badge -->
              <span
                class="rounded border border-indigo-200 bg-indigo-50 px-2 py-0.5 font-mono text-[11px] font-semibold uppercase text-indigo-700"
              >
                {{ job.standaloneMaintenanceOnly ? 'Profilaktika' : job.schemaOnly ? 'Faqat Struktura' : job.strategy === 'full' ? 'To\'liq' : job.strategy === 'incremental' ? 'O\'zgarishlar' : 'Qiyosiy' }}
              </span>

              <!-- Compression Badge -->
              <span
                class="rounded border border-cyan-200 bg-cyan-50 px-2 py-0.5 font-mono text-[11px] font-semibold text-cyan-700"
              >
                {{ job.compressionType }} (L{{ job.compressionLevel }})
              </span>

              <!-- Encryption Badge -->
              <span
                v-if="job.encryptionProfileId"
                class="inline-flex items-center gap-1 rounded border border-amber-200 bg-amber-50 px-2 py-0.5 font-mono text-[11px] font-semibold text-amber-700"
              >
                <i class="pi pi-lock text-[10px]"></i>
                {{ getJobEncryptionType(job).toUpperCase() }}
              </span>

              <!-- Archive Badge -->
              <span
                v-if="job.archiveEnabled"
                class="rounded border border-purple-200 bg-purple-50 px-2 py-0.5 font-mono text-[11px] font-semibold text-purple-700"
              >
                Bulut Arxiv
              </span>
            </div>

            <p v-if="job.description" class="text-xs text-slate-600">{{ job.description }}</p>

            <!-- Meta Row -->
            <div class="flex flex-wrap items-center gap-x-5 gap-y-1.5 text-xs text-slate-500">
              <span>
                Ulanish: <strong class="text-slate-800">{{ getJobConnectionName(job) }}</strong>
              </span>
              <span>
                Baza: <code class="rounded border border-emerald-200 bg-emerald-50 px-1.5 py-0.5 text-emerald-700 font-semibold">{{ job.databaseNames }}</code>
              </span>
              <span class="flex items-center gap-1.5">
                <i
                  class="text-[11px]"
                  :class="job.scheduleCron ? 'pi pi-clock text-indigo-600' : 'pi pi-hand text-amber-600'"
                ></i>
                <span>Triger:</span>
                <span
                  class="rounded px-1.5 py-0.2 font-semibold text-[11px]"
                  :class="
                    job.scheduleCron
                      ? 'bg-indigo-50 text-indigo-800 border border-indigo-200'
                      : 'bg-amber-50 text-amber-800 border border-amber-200'
                  "
                >
                  {{ formatCronHuman(job.scheduleCron) }}
                </span>
              </span>
              <span v-if="job.scheduleCron">
                Keyingi: <strong class="text-slate-800">{{ formatDateTime(job.nextRunAt) }}</strong>
              </span>
              <span>
                Oxirgi holat:
                <strong
                  :class="{
                    'text-emerald-700': job.lastStatus === 'completed',
                    'text-rose-600': job.lastStatus === 'failed',
                    'text-amber-600': job.lastStatus === 'running',
                    'text-slate-500': !job.lastStatus || job.lastStatus === 'idle',
                  }"
                >
                  {{ (job.lastStatus || 'kutilmoqda').toUpperCase() }}
                </strong>
              </span>
            </div>
          </div>

          <!-- Right: Action Buttons -->
          <div class="flex flex-wrap items-center gap-2">
            <button
              @click="runJobNow(job, false)"
              class="inline-flex items-center gap-1.5 rounded-xl bg-emerald-600 px-3.5 py-2 text-xs font-semibold text-white hover:bg-emerald-500 cursor-pointer shadow-2xs"
              title="Qo'lda (Manual) hoziroq zaxiralash jarayonini ishga tushirish"
            >
              <i class="pi pi-play"></i>
              Zaxiralash (Run Now)
            </button>
            <button
              @click="runJobNow(job, true)"
              class="inline-flex items-center gap-1.5 rounded-xl border border-amber-200 bg-amber-50 px-3 py-2 text-xs font-semibold text-amber-700 hover:bg-amber-100 cursor-pointer"
              title="Xavfsiz sinovdan o'tkazish"
            >
              <i class="pi pi-eye"></i>
              Sinov
            </button>

            <button
              @click="toggleJobStatus(job)"
              class="rounded-xl border border-slate-200 bg-slate-50 px-2.5 py-2 text-xs text-slate-700 hover:bg-slate-100 cursor-pointer"
              :title="job.isEnabled ? 'Rejani to\'xtatish' : 'Rejani faollashtirish'"
            >
              <i :class="job.isEnabled ? 'pi pi-pause' : 'pi pi-check'"></i>
            </button>
            <button
              @click="openEditWizard(job)"
              class="rounded-xl border border-slate-200 bg-slate-50 px-2.5 py-2 text-xs text-slate-700 hover:bg-slate-100 cursor-pointer"
              title="Edit in Guided Wizard"
            >
              <i class="pi pi-pencil"></i>
            </button>
            <button
              @click="deleteJobItem(job)"
              class="rounded-xl border border-rose-200 bg-rose-50 px-2.5 py-2 text-xs text-rose-700 hover:bg-rose-100 cursor-pointer"
              title="Delete Job"
            >
              <i class="pi pi-trash"></i>
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- 5-Step Guided Wizard Component -->
    <BackupJobWizardModal
      :show="showWizard"
      :job="selectedJobForEdit"
      @close="showWizard = false"
      @saved="onJobSaved"
    />
  </div>
</template>
