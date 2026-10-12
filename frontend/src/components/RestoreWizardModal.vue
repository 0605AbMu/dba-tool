<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useDbaStore, formatBytes, formatDateTime, ENGINE_METADATA } from '@/stores/dbaStore'
import { api } from '@/services/api'
import type { BackupArtifact, ConnectionProfile } from '@/types/models'

const props = defineProps<{
  show: boolean
  artifact: BackupArtifact | null
}>()

const emit = defineEmits<{
  (e: 'close'): void
  (e: 'started'): void
}>()

const store = useDbaStore()

// 4-Step Wizard: 1: Inspect -> 2: Target DB -> 3: Dry-Run Simulation -> 4: Confirm & Execute
const currentStep = ref(1)
const dryRunRunning = ref(false)
const dryRunSuccess = ref<boolean | null>(null)
const dryRunMessage = ref('')
const confirmDangerChecked = ref(false)
const restoring = ref(false)

const form = ref({
  filename: '',
  storageProfileId: undefined as number | undefined,
  connectionProfileId: 1,
  targetDatabase: '',
  createDb: true,
  passphrase: '',
  identity: '',
  base: '',
  compress: '',
  encryption: '',
})

watch(
  () => props.artifact,
  (item) => {
    if (item) {
      currentStep.value = 1
      dryRunSuccess.value = null
      confirmDangerChecked.value = false
      const engine = item.engine || item.sidecar?.type || 'postgres'
      const matchingConn = store.connections.find((c) => c.type === engine) || store.connections[0]

      form.value = {
        filename: item.filename,
        storageProfileId: item.storageProfileId,
        connectionProfileId: matchingConn?.id || 1,
        targetDatabase: item.database || String(item.sidecar?.db || 'main'),
        createDb: true,
        passphrase: '',
        identity: '',
        base: item.sidecar?.base || '',
        compress: '',
        encryption: '',
      }
    }
  },
  { immediate: true }
)

const selectedConn = computed<ConnectionProfile | undefined>(() => {
  return store.connections.find((c) => c.id === form.value.connectionProfileId) || store.connections[0]
})

const artifactEngine = computed(() => props.artifact?.engine || props.artifact?.sidecar?.type || 'postgres')
const engineMeta = computed(() => ENGINE_METADATA[artifactEngine.value] || ENGINE_METADATA.postgres)

async function runDryRunSimulation() {
  dryRunRunning.value = true
  dryRunSuccess.value = null
  dryRunMessage.value = ''
  try {
    store.liveDrawerOpen = true
    await api.restoreBackup({
      ...form.value,
      dryRun: true,
    })
    dryRunSuccess.value = true
    dryRunMessage.value = 'Tayyorgarlik muvaffaqiyatli! Zaxira fayli yaroqli va ma\'lumotlar bazasi bilan aloqa mavjud.'
    store.notify('success', 'Sinov Muvaffaqiyatli', 'Ma\'lumotlar bazasini tiklash oldidan sinov muvaffaqiyatli o\'tdi.')
  } catch (e: unknown) {
    dryRunSuccess.value = false
    dryRunMessage.value = (e as Error).message
    store.notify('error', 'Sinovda xatolik', (e as Error).message)
  } finally {
    dryRunRunning.value = false
  }
}

async function executeRealRestore() {
  if (!confirmDangerChecked.value) {
    store.notify('warn', 'Tasdiqlash talab etiladi', 'Iltimos, tiklashni davom ettirish uchun tasdiqlash katakchasini belgilang.')
    return
  }

  restoring.value = true
  try {
    emit('close')
    store.liveDrawerOpen = true
    await api.restoreBackup({
      ...form.value,
      dryRun: false,
    })
    store.notify('info', 'Tiklash Boshlandi', `"${form.value.filename}" zaxirasidan ma'lumotlar qayta tiklanmoqda...`)
    emit('started')
  } catch (e: unknown) {
    store.notify('error', 'Tiklashni boshlashda xatolik', (e as Error).message)
  } finally {
    restoring.value = false
  }
}
</script>

<template>
  <div
    v-if="show && artifact"
    class="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/50 p-4 backdrop-blur-xs"
    @click.self="emit('close')"
  >
    <div
      class="flex max-h-[92vh] w-full max-w-3xl flex-col rounded-2xl border border-slate-200 bg-white shadow-2xl"
    >
      <!-- Header -->
      <div class="flex items-center justify-between border-b border-slate-200 px-6 py-4">
        <div class="flex items-center gap-3">
          <div
            class="flex h-10 w-10 items-center justify-center rounded-xl bg-amber-50 text-amber-700 border border-amber-200"
          >
            <i class="pi pi-history text-lg"></i>
          </div>
          <div>
            <h2 class="text-base font-bold text-slate-900">Ma'lumotlar Bazasini Qayta Tiklash</h2>
            <p class="text-xs text-slate-500">
              Xavfsiz, bosqichma-bosqich bazani tiklash va oldindan sinovdan o'tkazish
            </p>
          </div>
        </div>

        <button
          @click="emit('close')"
          class="rounded-xl border border-slate-200 bg-slate-50 p-2 text-slate-500 hover:bg-slate-100 cursor-pointer"
        >
          <i class="pi pi-times"></i>
        </button>
      </div>

      <!-- Step Badges -->
      <div class="border-b border-slate-200 bg-slate-50/70 px-6 py-2.5">
        <div class="flex items-center justify-between">
          <span
            class="text-xs font-semibold"
            :class="currentStep === 1 ? 'text-amber-800 font-bold' : 'text-slate-500'"
          >
            1. Source Artifact
          </span>
          <i class="pi pi-chevron-right text-[10px] text-slate-400"></i>
          <span
            class="text-xs font-semibold"
            :class="currentStep === 2 ? 'text-amber-800 font-bold' : 'text-slate-500'"
          >
            2. Target Database
          </span>
          <i class="pi pi-chevron-right text-[10px] text-slate-400"></i>
          <span
            class="text-xs font-semibold"
            :class="currentStep === 3 ? 'text-amber-800 font-bold' : 'text-slate-500'"
          >
            3. Safety Dry-Run
          </span>
          <i class="pi pi-chevron-right text-[10px] text-slate-400"></i>
          <span
            class="text-xs font-semibold"
            :class="currentStep === 4 ? 'text-rose-700 font-bold' : 'text-slate-500'"
          >
            4. Confirm &amp; Restore
          </span>
        </div>
      </div>

      <!-- Body -->
      <div class="flex-1 overflow-y-auto p-6 space-y-4">
        <!-- STEP 1: Artifact Details -->
        <div v-if="currentStep === 1" class="space-y-4">
          <div class="rounded-2xl border border-slate-200 bg-slate-50/60 p-4 space-y-3">
            <div class="flex items-center justify-between">
              <span
                class="inline-flex items-center gap-1.5 rounded-lg border px-2.5 py-1 text-xs font-bold"
                :class="engineMeta.badgeClass"
              >
                <i :class="engineMeta.icon"></i>
                {{ engineMeta.label }}
              </span>
              <span class="font-mono text-xs font-semibold text-slate-600">
                Size: {{ formatBytes(artifact.size) }}
              </span>
            </div>

            <div>
              <span class="text-[11px] font-semibold text-slate-500 uppercase tracking-wider block">
                Archive Filename
              </span>
              <code class="font-mono text-xs font-bold text-slate-900 break-all block mt-0.5">
                {{ artifact.filename }}
              </code>
            </div>

            <div class="grid grid-cols-2 gap-3 pt-2 border-t border-slate-200/80 text-xs">
              <div>
                <span class="text-slate-500">Captured At:</span>
                <strong class="text-slate-800 ml-1">{{ formatDateTime(artifact.createdAt) }}</strong>
              </div>
              <div>
                <span class="text-slate-500">Database:</span>
                <strong class="text-slate-800 ml-1">{{ artifact.database || artifact.sidecar?.db || 'main' }}</strong>
              </div>
              <div>
                <span class="text-slate-500">Strategy:</span>
                <span class="ml-1 uppercase font-mono font-bold text-indigo-700">
                  {{ artifact.sidecar?.strategy || artifact.strategy || 'full' }}
                </span>
              </div>
              <div>
                <span class="text-slate-500">Holati:</span>
                <span class="ml-1 text-emerald-700 font-semibold">Yaxlitligi Tasdiqlangan</span>
              </div>
            </div>
          </div>

          <!-- Base Backup info for Incremental/Differential -->
          <div
            v-if="artifact.sidecar?.base || artifact.strategy === 'incremental' || artifact.strategy === 'differential'"
            class="rounded-xl border border-blue-200 bg-blue-50/70 p-3 text-xs text-blue-950"
          >
            <div class="flex items-center gap-2 font-semibold">
              <i class="pi pi-link text-blue-600"></i>
              Incremental / Differential Chain
            </div>
            <p class="mt-1 text-[11px] text-blue-900">
              This delta restore depends on Base Full Backup:
              <code class="font-mono font-bold">{{ artifact.sidecar?.base || form.base || 'Auto-resolved' }}</code>
            </p>
          </div>
        </div>

        <!-- STEP 2: Target Destination -->
        <div v-else-if="currentStep === 2" class="space-y-4">
          <div>
            <label class="block text-xs font-bold text-slate-800 mb-1">
              Destination Database Connection <span class="text-rose-500">*</span>
            </label>
            <select
              v-model="form.connectionProfileId"
              class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 text-xs font-semibold text-slate-900"
            >
              <option v-for="c in store.connections" :key="c.id" :value="c.id">
                {{ c.name }} ({{ c.type }} @ {{ c.host }}:{{ c.port }})
              </option>
            </select>
          </div>

          <div>
            <label class="block text-xs font-bold text-slate-800 mb-1">
              Target Database Name <span class="text-rose-500">*</span>
            </label>
            <input
              type="text"
              v-model="form.targetDatabase"
              placeholder="e.g. my_database or my_database_test"
              class="w-full rounded-xl border border-slate-300 px-3.5 py-2 font-mono text-xs font-bold text-slate-900 focus:border-amber-500 focus:outline-hidden"
            />
            <div class="mt-1.5 flex items-center gap-2 text-[11px] text-slate-500">
              <span>Quick suggestions:</span>
              <button
                type="button"
                @click="form.targetDatabase = artifact.database || 'main'"
                class="rounded border border-slate-200 bg-slate-50 px-2 py-0.5 hover:bg-slate-100 cursor-pointer"
              >
                Restore to Original ({{ artifact.database || 'main' }})
              </button>
              <button
                type="button"
                @click="form.targetDatabase = `${artifact.database || 'main'}_restored_test`"
                class="rounded border border-emerald-200 bg-emerald-50 px-2 py-0.5 text-emerald-800 hover:bg-emerald-100 cursor-pointer"
              >
                Safe Sandbox ({{ artifact.database || 'main' }}_restored_test)
              </button>
            </div>
          </div>

          <label class="flex items-center gap-2.5 rounded-xl border border-slate-200 bg-slate-50 p-3 cursor-pointer">
            <input type="checkbox" v-model="form.createDb" class="rounded accent-amber-600" />
            <div>
              <span class="text-xs font-bold text-slate-800 block">Agar maqsadli baza mavjud bo'lmasa, uni yaratish</span>
              <span class="text-[11px] text-slate-500">Baza topilmasa, u avtomatik tarzda yaratiladi</span>
            </div>
          </label>

          <!-- Decryption Credentials if needed -->
          <div class="rounded-xl border border-slate-200 bg-slate-50 p-3.5 space-y-2">
            <span class="text-xs font-bold text-slate-800">Shifrdan chiqarish paroli / kaliti (agar shifrlangan bo'lsa)</span>
            <input
              type="password"
              v-model="form.passphrase"
              placeholder="Zaxira shifrlanmagan bo'lsa bo'sh qoldiring"
              class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 text-xs text-slate-900 focus:border-amber-500 focus:outline-hidden"
            />
          </div>
        </div>

        <!-- STEP 3: Safety Dry-Run -->
        <div v-else-if="currentStep === 3" class="space-y-4">
          <div class="rounded-2xl border border-slate-200 bg-slate-50 p-4 text-center space-y-3">
            <div class="mx-auto flex h-12 w-12 items-center justify-center rounded-2xl bg-amber-100 text-amber-800">
              <i class="pi pi-shield text-xl"></i>
            </div>
            <div>
              <h3 class="text-sm font-bold text-slate-900">Xavfsiz Sinov va Tekshiruv</h3>
              <p class="text-xs text-slate-600 max-w-md mx-auto mt-0.5">
                Haqiqiy tiklashni boshlashdan oldin, tizim xavfsiz sinov o'tkazadi va ma'lumotlar bazasi hamda zaxira faylini tekshirib chiqadi.
              </p>
            </div>

            <button
              type="button"
              @click="runDryRunSimulation"
              :disabled="dryRunRunning"
              class="inline-flex items-center gap-2 rounded-xl bg-amber-600 px-5 py-2.5 text-xs font-bold text-white shadow-sm shadow-amber-600/20 hover:bg-amber-500 disabled:opacity-50 cursor-pointer"
            >
              <i :class="dryRunRunning ? 'pi pi-spin pi-spinner' : 'pi pi-play'"></i>
              {{ dryRunRunning ? 'Sinovdan o\'tkazilmoqda...' : 'Xavfsiz Sinovni Ishga Tushirish' }}
            </button>
          </div>

          <div
            v-if="dryRunSuccess !== null"
            class="rounded-xl p-3.5 text-xs border"
            :class="
              dryRunSuccess
                ? 'border-emerald-300 bg-emerald-50 text-emerald-950'
                : 'border-rose-300 bg-rose-50 text-rose-950'
            "
          >
            <div class="flex items-center gap-2 font-bold">
              <i :class="dryRunSuccess ? 'pi pi-check-circle text-emerald-600' : 'pi pi-times-circle text-rose-600'"></i>
              {{ dryRunSuccess ? 'Simulation Succeeded!' : 'Simulation Warning / Error' }}
            </div>
            <p class="mt-1 font-mono text-[11px]">{{ dryRunMessage }}</p>
          </div>
        </div>

        <!-- STEP 4: Confirm Danger & Execute -->
        <div v-else-if="currentStep === 4" class="space-y-4">
          <div class="rounded-2xl border border-rose-200 bg-rose-50/70 p-4 space-y-3">
            <div class="flex items-center gap-2.5 text-rose-800">
              <i class="pi pi-exclamation-triangle text-lg"></i>
              <h3 class="text-sm font-bold">Overwrite Danger Warning</h3>
            </div>
            <p class="text-xs text-rose-900 leading-relaxed">
              Restoring this backup will stream data directly into database
              <strong>{{ form.targetDatabase }}</strong> on
              <strong>{{ selectedConn?.name }} ({{ selectedConn?.host }})</strong>.
              Tables with identical names will be overwritten. This operation cannot be undone.
            </p>

            <label class="flex items-start gap-3 pt-2 border-t border-rose-200 cursor-pointer">
              <input type="checkbox" v-model="confirmDangerChecked" class="mt-0.5 rounded accent-rose-600" />
              <span class="text-xs font-bold text-rose-950">
                I understand this operation writes into {{ form.targetDatabase }} and want to proceed with live restore.
              </span>
            </label>
          </div>
        </div>
      </div>

      <!-- Footer -->
      <div class="flex items-center justify-between border-t border-slate-200 bg-slate-50 px-6 py-4">
        <button
          v-if="currentStep > 1"
          type="button"
          @click="currentStep--"
          class="rounded-xl border border-slate-200 bg-white px-4 py-2 text-xs font-semibold text-slate-700 hover:bg-slate-100 cursor-pointer"
        >
          <i class="pi pi-arrow-left mr-1.5"></i>
          Back
        </button>
        <div v-else></div>

        <div class="flex items-center gap-2">
          <button
            type="button"
            @click="emit('close')"
            class="rounded-xl border border-slate-200 bg-white px-4 py-2 text-xs font-semibold text-slate-600 hover:bg-slate-100 cursor-pointer"
          >
            Cancel
          </button>

          <button
            v-if="currentStep < 4"
            type="button"
            @click="currentStep++"
            class="inline-flex items-center gap-1.5 rounded-xl bg-amber-600 px-5 py-2 text-xs font-semibold text-white shadow-sm shadow-amber-600/20 hover:bg-amber-500 cursor-pointer"
          >
            Next
            <i class="pi pi-arrow-right"></i>
          </button>

          <button
            v-else
            type="button"
            @click="executeRealRestore"
            :disabled="!confirmDangerChecked || restoring"
            class="inline-flex items-center gap-2 rounded-xl bg-rose-600 px-6 py-2 text-xs font-bold text-white shadow-sm shadow-rose-600/20 hover:bg-rose-500 disabled:opacity-50 cursor-pointer"
          >
            <i :class="restoring ? 'pi pi-spin pi-spinner' : 'pi pi-refresh'"></i>
            Execute Live Restore Now
          </button>
        </div>
      </div>
    </div>
  </div>
</template>
