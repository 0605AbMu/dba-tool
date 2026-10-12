<script setup lang="ts">
import { onMounted, ref } from 'vue'
import {
  useDbaStore,
  formatBytes,
  formatDuration,
  formatDateTime,
  ENGINE_METADATA,
} from '@/stores/dbaStore'
import { api } from '@/services/api'
import { useModalBackdrop } from '@/composables/useModalBackdrop'
import type { BackupArtifact, SidecarMetadata } from '@/types/models'
import RestoreWizardModal from '@/components/RestoreWizardModal.vue'

const store = useDbaStore()
const { onBackdropMouseDown, onBackdropClick } = useModalBackdrop()
const selectedStorageProfileId = ref<number | null>(null)
const prefixSearch = ref('')
const loading = ref(false)

// Sidecar Inspector Modal
const showSidecarModal = ref(false)
const activeArtifact = ref<BackupArtifact | null>(null)
const activeSidecar = ref<SidecarMetadata | null>(null)

// Restore Wizard Modal
const showRestoreModal = ref(false)

// Prune Dialog Modal
const showPruneModal = ref(false)
const pruneForm = ref({
  storageProfileId: undefined as number | undefined,
  last: 5,
  within: '',
  daily: 7,
  weekly: 4,
  monthly: 6,
  yearly: 2,
})

// Cold Storage Archive Dialog Modal (dbb archive)
const showArchiveModal = ref(false)
const archiveForm = ref({
  srcStorageProfileId: undefined as number | undefined,
  dstStorageProfileId: undefined as number | undefined,
  dstPath: './data/archive',
  last: 3,
  within: '',
})

onMounted(async () => {
  await Promise.allSettled([store.fetchAllProfiles(), loadBackups()])
})

async function loadBackups() {
  loading.value = true
  try {
    await store.fetchBackups({
      storageProfileId: selectedStorageProfileId.value,
      prefix: prefixSearch.value.trim() || undefined,
    })
  } catch (e: unknown) {
    store.notify('error', 'Failed to list backups', (e as Error).message)
  } finally {
    loading.value = false
  }
}

function getCompressionRatio(item: BackupArtifact): string | null {
  const raw = item.rawSize ?? item.sidecar?.raw_size
  if (!raw || !item.size || raw <= 0) return null
  const ratio = ((1 - item.size / raw) * 100).toFixed(1)
  return `${ratio}% saved`
}

function getJobName(item: BackupArtifact): string | null {
  if (item.jobName) return item.jobName
  const job = item.sidecar?.job
  if (!job) return null
  if (typeof job === 'string') return job
  if (typeof job === 'object' && job.name) return job.name
  return null
}

function getJobSchedule(item: BackupArtifact): string | null {
  const job = item.sidecar?.job
  if (job && typeof job === 'object' && job.schedule) {
    return job.schedule
  }
  return null
}

function getSidecarJobName(sidecar?: SidecarMetadata | null): string | null {
  if (!sidecar?.job) return null
  if (typeof sidecar.job === 'string') return sidecar.job
  if (typeof sidecar.job === 'object' && sidecar.job.name) return sidecar.job.name
  return null
}

function getSidecarJobSchedule(sidecar?: SidecarMetadata | null): string | null {
  if (sidecar?.job && typeof sidecar.job === 'object' && sidecar.job.schedule) {
    return sidecar.job.schedule
  }
  return null
}

function getEncryptionType(item: BackupArtifact): string | null {
  if (item.encryption && item.encryption !== 'none') return item.encryption
  const enc = item.sidecar?.encryption
  if (!enc) return null
  if (typeof enc === 'string' && enc !== 'none') return enc
  if (typeof enc === 'object' && enc.type && enc.type !== 'none') return enc.type
  return null
}

async function openSidecarInspector(item: BackupArtifact) {
  activeArtifact.value = item
  activeSidecar.value = item.sidecar || null
  showSidecarModal.value = true
  try {
    const data = await api.getBackupSidecar(item.filename, item.storageProfileId)
    activeSidecar.value = data
  } catch {
    // Fallback to embedded sidecar if any
  }
}

async function verifyBackupItem(item: BackupArtifact) {
  try {
    store.liveDrawerOpen = true
    await api.verifyBackup({
      filename: item.filename,
      storageProfileId: item.storageProfileId,
    })
    store.notify('info', 'Tekshiruv Boshlandi', `"${item.filename}" zaxira faylining yaxlitligi tekshirilmoqda...`)
  } catch (e: unknown) {
    store.notify('error', 'Tekshirishda xatolik', (e as Error).message)
  }
}

function openRestoreWizard(item: BackupArtifact) {
  activeArtifact.value = item
  showRestoreModal.value = true
}

function openPruneDialog() {
  pruneForm.value.storageProfileId =
    selectedStorageProfileId.value || store.storageProfiles[0]?.id
  showPruneModal.value = true
}

async function runPrune(dryRun: boolean) {
  try {
    if (!dryRun) {
      showPruneModal.value = false
    }
    store.liveDrawerOpen = true
    await api.pruneBackups({
      ...pruneForm.value,
      dryRun,
    })
    store.notify(
      'info',
      dryRun ? 'Tozalash Sinovi' : 'Tozalash Boshlandi',
      'Eski zaxira nusxalarini tozalash jarayoni boshlandi...'
    )
  } catch (e: unknown) {
    store.notify('error', 'Tozalashda xatolik', (e as Error).message)
  }
}

function openArchiveDialog() {
  archiveForm.value.srcStorageProfileId =
    selectedStorageProfileId.value || store.storageProfiles[0]?.id
  archiveForm.value.dstStorageProfileId =
    store.storageProfiles[1]?.id || store.storageProfiles[0]?.id
  showArchiveModal.value = true
}

async function runArchive(dryRun: boolean) {
  try {
    if (!dryRun) {
      showArchiveModal.value = false
    }
    store.liveDrawerOpen = true
    await api.archiveBackups({
      ...archiveForm.value,
      dryRun,
    })
    store.notify(
      'info',
      dryRun ? 'Arxivlash Sinovi' : 'Arxivlash Boshlandi',
      'Zaxira fayllarini arxiv xotirasiga ko\'chirish boshlandi...'
    )
  } catch (e: unknown) {
    store.notify('error', 'Arxivlashda xatolik', (e as Error).message)
  }
}

async function deleteArtifact(item: BackupArtifact) {
  if (!confirm(`Delete backup artifact "${item.filename}" and its sidecar metadata?`)) return
  try {
    await api.deleteBackup(item.filename, item.storageProfileId)
    store.notify('success', 'Backup Deleted', `Deleted ${item.filename}`)
    await loadBackups()
  } catch (e: unknown) {
    store.notify('error', 'Delete Failed', (e as Error).message)
  }
}
</script>

<template>
  <div class="space-y-6">
    <!-- Header & Toolbar -->
    <div class="flex flex-col justify-between gap-4 lg:flex-row lg:items-center">
      <div>
        <h1 class="text-xl font-bold text-slate-900">Zaxira Nusxalari &amp; Tiklash Markazi</h1>
        <p class="text-xs text-slate-600">
          Barcha olingan zaxira nusxalarini ko'rish, fayl yaxlitligini tekshirish, bazani qayta tiklash, bulutga arxivlash va eski nusxalarni tozalash.
        </p>
      </div>

      <div class="flex flex-wrap items-center gap-2.5">
        <!-- Storage Profile Filter -->
        <select
          v-model="selectedStorageProfileId"
          @change="loadBackups"
          class="rounded-xl border border-slate-300 bg-white px-3 py-2 text-xs text-slate-800 shadow-2xs"
        >
          <option :value="null">Barcha Saqlash Joylari</option>
          <option v-for="sp in store.storageProfiles" :key="sp.id" :value="sp.id">
            {{ sp.name }} ({{ sp.backend }})
          </option>
        </select>

        <!-- Prefix Search -->
        <div class="relative">
          <i class="pi pi-search absolute left-3 top-2.5 text-xs text-slate-400"></i>
          <input
            v-model="prefixSearch"
            @keyup.enter="loadBackups"
            type="text"
            placeholder="Fayl yoki baza bo'yicha qidirish..."
            class="rounded-xl border border-slate-300 bg-white pl-8 pr-3 py-2 text-xs text-slate-900 placeholder-slate-400 shadow-2xs focus:border-emerald-600 focus:outline-none"
          />
        </div>

        <button
          @click="loadBackups"
          class="rounded-xl border border-slate-200 bg-white px-3 py-2 text-xs font-semibold text-slate-700 hover:bg-slate-50 shadow-2xs cursor-pointer"
        >
          <i class="pi pi-refresh mr-1"></i>Yangilash
        </button>

        <button
          @click="openArchiveDialog"
          class="inline-flex items-center gap-1.5 rounded-xl border border-indigo-200 bg-indigo-50 px-3.5 py-2 text-xs font-semibold text-indigo-700 hover:bg-indigo-100 cursor-pointer"
        >
          <i class="pi pi-box"></i>
          Bulutga Arxivlash
        </button>

        <button
          @click="openPruneDialog"
          class="inline-flex items-center gap-1.5 rounded-xl border border-amber-200 bg-amber-50 px-3.5 py-2 text-xs font-semibold text-amber-800 hover:bg-amber-100 cursor-pointer"
        >
          <i class="pi pi-filter-slash"></i>
          Eski Nusxalarni Tozalash
        </button>
      </div>
    </div>

    <!-- Backups Table -->
    <div class="rounded-2xl border border-slate-200 bg-white shadow-2xs overflow-hidden">
      <div class="overflow-x-auto">
        <table class="w-full text-left text-xs">
          <thead>
            <tr class="border-b border-slate-200 bg-slate-50 text-[11px] uppercase text-slate-500">
              <th class="px-4 py-3.5">Artifact Filename</th>
              <th class="px-3 py-3.5">Engine &amp; DB</th>
              <th class="px-3 py-3.5">Strategy</th>
              <th class="px-3 py-3.5">Compressed vs Raw Size</th>
              <th class="px-3 py-3.5">Tags &amp; Checksum</th>
              <th class="px-3 py-3.5">Created At</th>
              <th class="px-4 py-3.5 text-right">Actions</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-slate-100">
            <tr v-if="store.backups.length === 0" class="text-slate-400">
              <td colspan="7" class="py-10 text-center">
                No backup artifacts found in storage. Trigger a job in the Jobs view or Dashboard!
              </td>
            </tr>
            <tr
              v-for="item in store.backups"
              :key="item.filename"
              class="hover:bg-slate-50/80"
            >
              <!-- Filename -->
              <td class="px-4 py-3.5">
                <div class="flex items-center gap-2">
                  <i class="pi pi-file-export text-emerald-600"></i>
                  <span class="font-mono font-bold text-slate-900">{{ item.filename }}</span>
                </div>
                <div v-if="getJobName(item)" class="mt-0.5 flex flex-wrap items-center gap-1.5 text-[11px] text-slate-500">
                  <span>Job:</span>
                  <span class="font-mono font-semibold text-slate-700">{{ getJobName(item) }}</span>
                  <span
                    v-if="getJobSchedule(item)"
                    class="rounded border border-slate-200 bg-slate-100 px-1.5 py-0.2 font-mono text-[10px] text-slate-500"
                    :title="`Reja jadvali: ${getJobSchedule(item)}`"
                  >
                    {{ getJobSchedule(item) }}
                  </span>
                  <span v-if="item.durationMs || item.sidecar?.duration_ms" class="text-slate-400">
                    • {{ formatDuration(item.durationMs ?? item.sidecar?.duration_ms) }}
                  </span>
                </div>
              </td>

              <!-- Engine & DB -->
              <td class="px-3 py-3.5">
                <div class="flex items-center gap-1.5">
                  <span
                    class="rounded border px-1.5 py-0.5 font-mono text-[10px] font-semibold uppercase"
                    :class="
                      ENGINE_METADATA[item.engine || String(item.sidecar?.type || 'sqlite')]
                        ?.badgeClass || 'bg-slate-100 text-slate-700 border-slate-200'
                    "
                  >
                    {{ item.engine || item.sidecar?.type || 'sqlite' }}
                  </span>
                  <span class="font-mono font-semibold text-slate-900">
                    {{ item.database || item.sidecar?.db || 'main' }}
                  </span>
                </div>
                <div class="mt-0.5 font-mono text-[10px] text-slate-500 truncate max-w-[180px]">
                  {{ item.host || item.sidecar?.host || 'local' }}
                </div>
              </td>

              <!-- Strategy -->
              <td class="px-3 py-3.5">
                <span
                  class="rounded border border-indigo-200 bg-indigo-50 px-2 py-0.5 font-mono text-[11px] font-semibold uppercase text-indigo-700"
                >
                  {{ item.schemaOnly || item.sidecar?.schema_only ? 'schema' : item.strategy || item.sidecar?.strategy || 'full' }}
                </span>
              </td>

              <!-- Size & Ratio -->
              <td class="px-3 py-3.5 font-mono">
                <div class="font-bold text-emerald-700">{{ formatBytes(item.size) }}</div>
                <div
                  v-if="item.rawSize || item.sidecar?.raw_size"
                  class="text-[11px] text-slate-500"
                >
                  Raw: {{ formatBytes(item.rawSize ?? item.sidecar?.raw_size) }}
                  <span
                    v-if="getCompressionRatio(item)"
                    class="ml-1 rounded border border-emerald-200 bg-emerald-50 px-1 py-0.2 text-[10px] font-semibold text-emerald-700"
                  >
                    {{ getCompressionRatio(item) }}
                  </span>
                </div>
              </td>

              <!-- Compression + Encryption + Checksum -->
              <td class="px-3 py-3.5">
                <div class="flex flex-wrap items-center gap-1">
                  <span
                    class="rounded border border-cyan-200 bg-cyan-50 px-1.5 py-0.5 font-mono text-[10px] font-semibold text-cyan-700"
                  >
                    {{ item.compression || item.sidecar?.compress || 'zstd' }}
                  </span>
                  <span
                    v-if="getEncryptionType(item)"
                    class="rounded border border-amber-200 bg-amber-50 px-1.5 py-0.5 font-mono text-[10px] font-semibold text-amber-700"
                  >
                    <i class="pi pi-lock text-[9px] mr-0.5"></i>
                    {{ getEncryptionType(item) }}
                  </span>
                  <span
                    v-if="item.hasSidecar !== false"
                    class="rounded border border-emerald-200 bg-emerald-50 px-1.5 py-0.5 font-mono text-[10px] font-semibold text-emerald-700"
                    title="Sidecar .json v2 present"
                  >
                    sidecar v2
                  </span>
                </div>
              </td>

              <!-- Created At -->
              <td class="px-3 py-3.5 text-slate-600">
                {{ formatDateTime(item.createdAt || item.sidecar?.timestamp) }}
              </td>

              <!-- Actions -->
              <td class="px-4 py-3.5 text-right">
                <div class="flex items-center justify-end gap-1.5">
                  <button
                    @click="openSidecarInspector(item)"
                    class="rounded-lg border border-slate-200 bg-slate-50 px-2.5 py-1.5 text-[11px] font-semibold text-slate-700 hover:bg-slate-100 cursor-pointer"
                    title="Xavfsizlik va yaxlitlik metama'lumotlari"
                  >
                    <i class="pi pi-shield mr-1 text-indigo-600"></i>Yaxlitlik
                  </button>
                  <button
                    @click="verifyBackupItem(item)"
                    class="rounded-lg border border-emerald-200 bg-emerald-50 px-2.5 py-1.5 text-[11px] font-semibold text-emerald-700 hover:bg-emerald-100 cursor-pointer"
                    title="Fayl yaxlitligini tekshirish"
                  >
                    <i class="pi pi-check-circle mr-1"></i>Tekshirish
                  </button>
                  <button
                    @click="openRestoreWizard(item)"
                    class="rounded-lg border border-indigo-200 bg-indigo-50 px-2.5 py-1.5 text-[11px] font-semibold text-indigo-700 hover:bg-indigo-100 cursor-pointer"
                    title="Bazani tiklash ustasi"
                  >
                    <i class="pi pi-replay mr-1"></i>Tiklash
                  </button>
                  <a
                    :href="api.getBackupDownloadUrl(item.filename, item.storageProfileId)"
                    target="_blank"
                    class="rounded-lg border border-slate-200 bg-slate-50 p-1.5 text-slate-600 hover:bg-slate-100"
                    title="Zaxira faylini yuklab olish"
                  >
                    <i class="pi pi-download"></i>
                  </a>
                  <button
                    @click="deleteArtifact(item)"
                    class="rounded-lg border border-rose-200 bg-rose-50 p-1.5 text-rose-700 hover:bg-rose-100 cursor-pointer"
                    title="O'chirish"
                  >
                    <i class="pi pi-trash"></i>
                  </button>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <!-- MODAL 1: Sidecar v2 JSON Metadata Inspector -->
    <div
      v-if="showSidecarModal"
      class="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4 backdrop-blur-xs"
      @mousedown="onBackdropMouseDown"
      @click="onBackdropClick($event, () => (showSidecarModal = false))"
    >
      <div
        class="flex max-h-[88vh] w-full max-w-3xl flex-col rounded-2xl border border-slate-200 bg-white shadow-2xl"
      >
        <div class="flex items-center justify-between border-b border-slate-200 px-6 py-4">
          <div>
            <div class="flex items-center gap-2">
              <span class="rounded border border-indigo-200 bg-indigo-50 px-2 py-0.5 font-mono text-xs font-semibold text-indigo-700">
                {{ activeSidecar?.format || 'dbbackup.sidecar' }} v{{ activeSidecar?.schema_version || 2 }}
              </span>
              <h3 class="font-mono text-sm font-bold text-slate-900">
                {{ activeArtifact?.filename }}.json
              </h3>
            </div>
          </div>
          <button
            @click="showSidecarModal = false"
            class="rounded-lg p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-800 cursor-pointer"
          >
            <i class="pi pi-times"></i>
          </button>
        </div>

        <div class="flex-1 overflow-y-auto p-6 space-y-5 text-xs">
          <!-- Originating Job & Trigger Context -->
          <div
            v-if="getSidecarJobName(activeSidecar) || activeSidecar?.trigger"
            class="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-slate-200 bg-slate-50 p-3.5"
          >
            <div v-if="getSidecarJobName(activeSidecar)" class="flex items-center gap-2">
              <i class="pi pi-bolt text-indigo-600 text-sm"></i>
              <span class="text-slate-500 font-medium">Job:</span>
              <span class="font-mono font-bold text-slate-900 text-sm">{{ getSidecarJobName(activeSidecar) }}</span>
              <span
                v-if="getSidecarJobSchedule(activeSidecar)"
                class="rounded border border-slate-200 bg-white px-2 py-0.5 font-mono text-[11px] text-slate-600 shadow-2xs"
              >
                {{ getSidecarJobSchedule(activeSidecar) }}
              </span>
            </div>
            <div v-if="activeSidecar?.trigger" class="flex items-center gap-1.5">
              <span class="text-slate-500 font-medium">Trigger:</span>
              <span class="rounded border border-indigo-200 bg-indigo-50 px-2 py-0.5 font-mono text-[11px] font-semibold uppercase text-indigo-700">
                {{ activeSidecar.trigger }}
              </span>
            </div>
          </div>

          <div class="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <div class="rounded-xl border border-slate-200 bg-slate-50 p-3">
              <div class="text-slate-500">Server Engine</div>
              <div class="mt-1 font-mono font-bold text-slate-900">
                {{ activeSidecar?.server?.engine || activeSidecar?.type || 'sqlite' }}
                {{ activeSidecar?.server?.version || '' }}
              </div>
            </div>
            <div class="rounded-xl border border-slate-200 bg-slate-50 p-3">
              <div class="text-slate-500">Duration</div>
              <div class="mt-1 font-mono font-bold text-emerald-700">
                {{ formatDuration(activeSidecar?.duration_ms) }}
              </div>
            </div>
            <div class="rounded-xl border border-slate-200 bg-slate-50 p-3">
              <div class="text-slate-500">Raw -> Compressed</div>
              <div class="mt-1 font-mono font-bold text-cyan-700">
                {{ formatBytes(activeSidecar?.raw_size) }} -> {{ formatBytes(activeSidecar?.size) }}
              </div>
            </div>
            <div class="rounded-xl border border-slate-200 bg-slate-50 p-3">
              <div class="text-slate-500">Session / Run ID</div>
              <div class="mt-1 font-mono text-[11px] font-semibold text-indigo-700 truncate">
                {{ activeSidecar?.session_id || '-' }} / {{ activeSidecar?.run_id || '-' }}
              </div>
            </div>
          </div>

          <!-- Checksums Box -->
          <div class="rounded-xl border border-slate-200 bg-slate-50 p-4 space-y-2">
            <div class="font-semibold text-slate-700">Cryptographic &amp; Stream Checksums</div>
            <div class="grid grid-cols-1 gap-2 sm:grid-cols-2 font-mono text-[11px]">
              <div>
                <span class="text-slate-500">compressed_md5: </span>
                <span class="font-semibold text-emerald-700">{{ activeSidecar?.checksums?.compressed_md5 || 'N/A' }}</span>
              </div>
              <div>
                <span class="text-slate-500">uncompressed_md5: </span>
                <span class="font-semibold text-cyan-700">{{ activeSidecar?.checksums?.uncompressed_md5 || 'N/A' }}</span>
              </div>
              <div v-if="activeSidecar?.checksums?.compressed_sha1">
                <span class="text-slate-500">compressed_sha1: </span>
                <span class="font-semibold text-emerald-700">{{ activeSidecar.checksums.compressed_sha1 }}</span>
              </div>
              <div v-if="activeSidecar?.checksums?.uncompressed_sha1">
                <span class="text-slate-500">uncompressed_sha1: </span>
                <span class="font-semibold text-cyan-700">{{ activeSidecar.checksums.uncompressed_sha1 }}</span>
              </div>
            </div>
          </div>

          <!-- Raw JSON Viewer -->
          <div>
            <div class="mb-2 font-semibold text-slate-700">Yaxlitlik Metama'lumotlari Hujjati</div>
            <pre
              class="max-h-72 overflow-auto rounded-xl border border-slate-200 bg-slate-50 p-4 font-mono text-[11px] text-slate-800"
            >{{ JSON.stringify(activeSidecar, null, 2) }}</pre>
          </div>
        </div>
      </div>
    </div>

    <!-- MODAL 2: 4-Step Guided Restore Wizard (dbb restore) -->
    <RestoreWizardModal
      :show="showRestoreModal"
      :artifact="activeArtifact"
      @close="showRestoreModal = false"
      @started="loadBackups"
    />

    <!-- MODAL 3: Prune / Retention Dialog (dbb prune) -->
    <div
      v-if="showPruneModal"
      class="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4 backdrop-blur-xs"
      @mousedown="onBackdropMouseDown"
      @click="onBackdropClick($event, () => (showPruneModal = false))"
    >
      <div class="w-full max-w-xl rounded-2xl border border-slate-200 bg-white shadow-2xl">
        <div class="flex items-center justify-between border-b border-slate-200 px-6 py-4">
          <div>
            <h3 class="text-base font-bold text-slate-900">Eski Zaxira Nusxalarini Tozalash</h3>
            <p class="text-xs text-slate-500">Xavfsiz sinovdan o'tkazing yoki eskirgan nusxalarni tozalang</p>
          </div>
          <button
            @click="showPruneModal = false"
            class="rounded-lg p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-800 cursor-pointer"
          >
            <i class="pi pi-times"></i>
          </button>
        </div>

        <div class="p-6 space-y-4 text-xs">
          <div>
            <label class="mb-1 block font-semibold text-slate-700">Storage Profile</label>
            <select
              v-model="pruneForm.storageProfileId"
              class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 text-slate-900"
            >
              <option v-for="sp in store.storageProfiles" :key="sp.id" :value="sp.id">
                {{ sp.name }} ({{ sp.path || sp.bucket }})
              </option>
            </select>
          </div>

          <div class="grid grid-cols-2 gap-3 sm:grid-cols-3">
            <div>
              <label class="mb-1 block text-slate-600">Keep Last (<code>--last</code>)</label>
              <input
                v-model.number="pruneForm.last"
                type="number"
                min="0"
                class="w-full rounded-xl border border-slate-300 bg-white px-3 py-2 font-mono text-slate-900"
              />
            </div>
            <div>
              <label class="mb-1 block text-slate-600">Within Window (<code>--within</code>)</label>
              <input
                v-model="pruneForm.within"
                type="text"
                placeholder="e.g. 1440m"
                class="w-full rounded-xl border border-slate-300 bg-white px-3 py-2 font-mono text-slate-900"
              />
            </div>
            <div>
              <label class="mb-1 block text-slate-600">Daily Tier (<code>--daily</code>)</label>
              <input
                v-model.number="pruneForm.daily"
                type="number"
                min="0"
                class="w-full rounded-xl border border-slate-300 bg-white px-3 py-2 font-mono text-slate-900"
              />
            </div>
            <div>
              <label class="mb-1 block text-slate-600">Weekly Tier (<code>--weekly</code>)</label>
              <input
                v-model.number="pruneForm.weekly"
                type="number"
                min="0"
                class="w-full rounded-xl border border-slate-300 bg-white px-3 py-2 font-mono text-slate-900"
              />
            </div>
            <div>
              <label class="mb-1 block text-slate-600">Monthly Tier (<code>--monthly</code>)</label>
              <input
                v-model.number="pruneForm.monthly"
                type="number"
                min="0"
                class="w-full rounded-xl border border-slate-300 bg-white px-3 py-2 font-mono text-slate-900"
              />
            </div>
            <div>
              <label class="mb-1 block text-slate-600">Yearly Tier (<code>--yearly</code>)</label>
              <input
                v-model.number="pruneForm.yearly"
                type="number"
                min="0"
                class="w-full rounded-xl border border-slate-300 bg-white px-3 py-2 font-mono text-slate-900"
              />
            </div>
          </div>
        </div>

        <div class="flex items-center justify-between border-t border-slate-200 px-6 py-4">
          <button
            @click="showPruneModal = false"
            class="rounded-xl border border-slate-200 bg-slate-50 px-4 py-2 text-xs font-semibold text-slate-700 cursor-pointer"
          >
            Close
          </button>
          <div class="flex items-center gap-2.5">
            <button
              @click="runPrune(true)"
              class="inline-flex items-center gap-1.5 rounded-xl border border-amber-200 bg-amber-50 px-4 py-2 text-xs font-semibold text-amber-800 hover:bg-amber-100 cursor-pointer"
            >
              <i class="pi pi-eye"></i>
              Preview Prune (--dry-run)
            </button>
            <button
              @click="runPrune(false)"
              class="inline-flex items-center gap-1.5 rounded-xl bg-rose-600 px-4 py-2 text-xs font-semibold text-white hover:bg-rose-500 cursor-pointer"
            >
              <i class="pi pi-trash"></i>
              Execute Prune
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- MODAL 4: Cold Storage Archive Dialog (dbb archive) -->
    <div
      v-if="showArchiveModal"
      class="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4 backdrop-blur-xs"
      @mousedown="onBackdropMouseDown"
      @click="onBackdropClick($event, () => (showArchiveModal = false))"
    >
      <div class="w-full max-w-xl rounded-2xl border border-slate-200 bg-white shadow-2xl">
        <div class="flex items-center justify-between border-b border-slate-200 px-6 py-4">
          <div>
            <h3 class="text-base font-bold text-slate-900">Uzoq Muddatli Saqlashga Arxivlash</h3>
            <p class="text-xs text-slate-500">Zaxira nusxalarini ikkilamchi yoki bulutli arxiv xotirasiga ko'chirish</p>
          </div>
          <button
            @click="showArchiveModal = false"
            class="rounded-lg p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-800 cursor-pointer"
          >
            <i class="pi pi-times"></i>
          </button>
        </div>

        <div class="p-6 space-y-4 text-xs">
          <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div>
              <label class="mb-1 block font-semibold text-slate-700">Source Storage Profile</label>
              <select
                v-model="archiveForm.srcStorageProfileId"
                class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 text-slate-900"
              >
                <option v-for="sp in store.storageProfiles" :key="sp.id" :value="sp.id">
                  {{ sp.name }} ({{ sp.backend }})
                </option>
              </select>
            </div>
            <div>
              <label class="mb-1 block font-semibold text-slate-700">Destination Storage Profile</label>
              <select
                v-model="archiveForm.dstStorageProfileId"
                class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 text-slate-900"
              >
                <option v-for="sp in store.storageProfiles" :key="sp.id" :value="sp.id">
                  {{ sp.name }} ({{ sp.backend }})
                </option>
              </select>
            </div>
          </div>

          <div>
            <label class="mb-1 block font-semibold text-slate-700">Destination Path / Prefix Override (<code>--dst-path</code>)</label>
            <input
              v-model="archiveForm.dstPath"
              type="text"
              placeholder="./data/archive"
              class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 font-mono text-slate-900"
            />
          </div>

          <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div>
              <label class="mb-1 block font-semibold text-slate-700">Archive Last N Backups (<code>--last</code>)</label>
              <input
                v-model.number="archiveForm.last"
                type="number"
                min="1"
                class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 font-mono text-slate-900"
              />
            </div>
            <div>
              <label class="mb-1 block font-semibold text-slate-700">Archive Within Window (<code>--within</code>)</label>
              <input
                v-model="archiveForm.within"
                type="text"
                placeholder="e.g. 24h or 7d"
                class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 font-mono text-slate-900"
              />
            </div>
          </div>
        </div>

        <div class="flex items-center justify-between border-t border-slate-200 px-6 py-4">
          <button
            @click="showArchiveModal = false"
            class="rounded-xl border border-slate-200 bg-slate-50 px-4 py-2 text-xs font-semibold text-slate-700 cursor-pointer"
          >
            Close
          </button>
          <div class="flex items-center gap-2.5">
            <button
              @click="runArchive(true)"
              class="inline-flex items-center gap-1.5 rounded-xl border border-amber-200 bg-amber-50 px-4 py-2 text-xs font-semibold text-amber-800 hover:bg-amber-100 cursor-pointer"
            >
              <i class="pi pi-eye"></i>
              Dry Run (--dry-run)
            </button>
            <button
              @click="runArchive(false)"
              class="inline-flex items-center gap-1.5 rounded-xl bg-indigo-600 px-4 py-2 text-xs font-semibold text-white hover:bg-indigo-500 cursor-pointer"
            >
              <i class="pi pi-box"></i>
              Execute Archive
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
