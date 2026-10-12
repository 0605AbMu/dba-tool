<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useDbaStore, ENGINE_METADATA } from '@/stores/dbaStore'
import { api } from '@/services/api'
import { useModalBackdrop } from '@/composables/useModalBackdrop'
import type { BackupJob, ConnectionProfile } from '@/types/models'

const props = defineProps<{
  show: boolean
  job?: BackupJob | null
}>()

const emit = defineEmits<{
  (e: 'close'): void
  (e: 'saved', job: BackupJob): void
}>()

const { onBackdropMouseDown, onBackdropClick } = useModalBackdrop(() => emit('close'))

const store = useDbaStore()

const saving = ref(false)
const discoveringDbs = ref(false)
const discoveredDatabases = ref<string[]>([])
const discoveryMessage = ref<string | null>(null)

// Trigger & Schedule modes
const triggerMode = ref<'manual' | 'scheduled'>('scheduled')
const presetSchedule = ref<'daily' | '6h' | '12h' | 'hourly' | 'weekly' | 'custom'>('daily')
const customCron = ref('0 2 * * *')

watch(triggerMode, (mode) => {
  if (mode === 'scheduled') {
    form.value.isEnabled = true
  }
})

const defaultForm = (): Partial<BackupJob> => ({
  name: '',
  description: '',
  isEnabled: true,
  connectionProfileId: store.connections[0]?.id || 1,
  databaseNames: 'ALL',
  excludeDatabases: '',
  splitDb: true,
  strategy: 'full',
  schemaOnly: false,
  createLatest: false,
  fullEvery: 7,
  fullAfter: '7d',
  postgresGlobals: false,
  compressionType: 'zstd',
  compressionLevel: 3,
  checksum: 'md5',
  storageProfileId: store.storageProfiles[0]?.id || 1,
  encryptionProfileId: null,
  scheduleCron: '0 2 * * *',
  scheduleIntervalMinutes: 0,
  retentionLast: 5,
  retentionDaily: 7,
  retentionWeekly: 4,
  retentionMonthly: 6,
  retentionYearly: 2,
  standaloneMaintenanceOnly: false,
  logLevel: 'info',
  timingsEnabled: true,
  progressEnabled: true,
})

const form = ref<Partial<BackupJob>>(defaultForm())

const selectedConn = computed<ConnectionProfile | undefined>(() => {
  return store.connections.find((c) => c.id === form.value.connectionProfileId)
})

const connEngine = computed(() => selectedConn.value?.type || 'postgres')

function getPresetCron(preset: string): string {
  switch (preset) {
    case 'daily':
      return '0 2 * * *'
    case '6h':
      return '0 */6 * * *'
    case '12h':
      return '0 */12 * * *'
    case 'hourly':
      return '0 * * * *'
    case 'weekly':
      return '0 2 * * 0'
    default:
      return '0 2 * * *'
  }
}

watch(
  () => props.show,
  (isShown) => {
    if (isShown) {
      discoveredDatabases.value = []
      discoveryMessage.value = null
      if (props.job) {
        form.value = {
          ...props.job,
          isEnabled: props.job.isEnabled !== false && String(props.job.isEnabled) !== '0',
        }
        if (props.job.scheduleCron && props.job.scheduleCron.trim() !== '') {
          triggerMode.value = 'scheduled'
          const c = props.job.scheduleCron.trim()
          if (c === '0 2 * * *') presetSchedule.value = 'daily'
          else if (c === '0 */6 * * *') presetSchedule.value = '6h'
          else if (c === '0 */12 * * *') presetSchedule.value = '12h'
          else if (c === '0 * * * *') presetSchedule.value = 'hourly'
          else if (c === '0 2 * * 0') presetSchedule.value = 'weekly'
          else {
            presetSchedule.value = 'custom'
            customCron.value = c
          }
        } else {
          triggerMode.value = 'manual'
          presetSchedule.value = 'daily'
        }
      } else {
        form.value = defaultForm()
        form.value.isEnabled = true
        triggerMode.value = 'scheduled'
        presetSchedule.value = 'daily'
        autoGenerateJobName()
      }
      if (form.value.connectionProfileId) {
        discoverDatabases()
      }
    }
  },
  { immediate: true }
)

function autoGenerateJobName() {
  if (props.job) return
  const conn = selectedConn.value
  const prefix = conn ? conn.name.toLowerCase().replace(/[^a-z0-9]/g, '-') : 'db'
  form.value.name = `${prefix}-backup`
}

function onConnectionChange() {
  autoGenerateJobName()
  discoverDatabases()
}

async function discoverDatabases() {
  if (!form.value.connectionProfileId) return
  discoveringDbs.value = true
  discoveryMessage.value = null
  try {
    const res = await api.getConnectionDatabases(form.value.connectionProfileId)
    discoveredDatabases.value = res.databases || []
    if (discoveredDatabases.value.length > 0) {
      discoveryMessage.value = `${discoveredDatabases.value.length} ta baza topildi`
    } else {
      discoveryMessage.value = 'Bazalar topilmadi yoki ruxsat yo\'q'
    }
  } catch (e: unknown) {
    discoveryMessage.value = (e as Error).message
  } finally {
    discoveringDbs.value = false
  }
}

const currentSelectedDbs = computed<string[]>(() => {
  const raw = form.value.databaseNames?.trim() || ''
  if (!raw) return []
  if (raw === 'ALL') {
    return discoveredDatabases.value.length > 0 ? [...discoveredDatabases.value] : ['ALL']
  }
  return raw.split(',').map((s) => s.trim()).filter(Boolean)
})

const isAllSelected = computed(() => {
  const raw = form.value.databaseNames?.trim()
  if (raw === 'ALL') return true
  if (discoveredDatabases.value.length > 0) {
    return discoveredDatabases.value.every((d) => currentSelectedDbs.value.includes(d))
  }
  return false
})

const selectedCountText = computed(() => {
  if (form.value.databaseNames?.trim() === 'ALL') {
    return `Barchasi tanlangan (${discoveredDatabases.value.length} ta)`
  }
  const count = currentSelectedDbs.value.length
  return `${count} / ${discoveredDatabases.value.length} ta tanlandi`
})

function isDbSelected(name: string): boolean {
  const raw = form.value.databaseNames?.trim()
  if (raw === 'ALL') return true
  return currentSelectedDbs.value.includes(name)
}

function toggleAllDbs() {
  if (isAllSelected.value) {
    form.value.databaseNames = ''
  } else {
    form.value.databaseNames = 'ALL'
  }
}

function toggleDb(name: string) {
  const raw = form.value.databaseNames?.trim() || ''
  let list: string[]

  if (raw === 'ALL') {
    list = discoveredDatabases.value.filter((d) => d !== name)
  } else {
    const current = currentSelectedDbs.value
    if (current.includes(name)) {
      list = current.filter((d) => d !== name)
    } else {
      list = [...current, name]
    }
  }

  if (list.length === 0) {
    form.value.databaseNames = ''
  } else if (discoveredDatabases.value.length > 0 && list.length === discoveredDatabases.value.length) {
    form.value.databaseNames = 'ALL'
  } else {
    form.value.databaseNames = list.join(', ')
  }
}

async function submitJob(runImmediately = false) {
  if (!form.value.name?.trim()) {
    store.notify('warn', 'Tekshiruv', 'Reja nomini kiriting.')
    return
  }
  if (!form.value.connectionProfileId) {
    store.notify('warn', 'Tekshiruv', 'Ma\'lumotlar bazasi ulanishini tanlang.')
    return
  }
  if (!form.value.storageProfileId) {
    store.notify('warn', 'Tekshiruv', 'Saqlash omborini tanlang.')
    return
  }

  // Set scheduleCron based on triggerMode and guarantee active status
  if (triggerMode.value === 'manual') {
    form.value.scheduleCron = ''
    if (form.value.isEnabled === undefined) {
      form.value.isEnabled = true
    }
  } else {
    form.value.isEnabled = true
    form.value.scheduleCron =
      presetSchedule.value === 'custom' ? customCron.value.trim() : getPresetCron(presetSchedule.value)
  }

  if (!props.job) {
    form.value.isEnabled = true
  }

  saving.value = true
  try {
    let saved: BackupJob
    if (props.job?.id) {
      saved = await api.updateJob(props.job.id, form.value)
      store.notify('success', 'Muvaffaqiyatli saqlandi', `"${saved.name}" yangilandi.`)
    } else {
      saved = await api.createJob(form.value)
      store.notify('success', 'Reja Yaratildi', `"${saved.name}" muvaffaqiyatli yaratildi.`)
    }

    emit('saved', saved)
    emit('close')

    // If manual trigger requested immediately
    if (runImmediately) {
      store.liveDrawerOpen = true
      await api.runJob(saved.id, false)
      store.notify('info', 'Zaxiralash Boshlandi', `"${saved.name}" bo'yicha zaxira jarayoni ishga tushirildi...`)
    }
  } catch (e: unknown) {
    store.notify('error', 'Xatolik', (e as Error).message)
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <div
    v-if="show"
    class="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/50 p-4 backdrop-blur-xs"
    @mousedown="onBackdropMouseDown"
    @click="onBackdropClick"
  >
    <div
      class="flex max-h-[92vh] w-full max-w-3xl flex-col rounded-2xl border border-slate-200 bg-white shadow-2xl overflow-hidden"
    >
      <!-- Modal Header -->
      <div class="flex items-center justify-between border-b border-slate-200 px-6 py-4 bg-slate-50/50">
        <div class="flex items-center gap-3">
          <div
            class="flex h-10 w-10 items-center justify-center rounded-xl bg-emerald-50 text-emerald-700 border border-emerald-200"
          >
            <i class="pi pi-briefcase text-lg"></i>
          </div>
          <div>
            <h2 class="text-base font-bold text-slate-900">
              {{ props.job ? `Zaxira Rejasini Tahrirlash: ${props.job.name}` : 'Yangi Zaxira Rejasi Yaratish' }}
            </h2>
            <p class="text-xs text-slate-500">
              Ma'lumotlar bazasini avtomatik jadval yoki qo'lda (manual) zaxiralash sozlamalari
            </p>
          </div>
        </div>

        <button
          @click="emit('close')"
          class="rounded-xl border border-slate-200 bg-white p-2 text-slate-400 hover:text-slate-700 hover:bg-slate-100 cursor-pointer transition"
        >
          <i class="pi pi-times text-xs"></i>
        </button>
      </div>

      <!-- Modal Body (Clean 2-Column Layout) -->
      <div class="flex-1 overflow-y-auto p-6">
        <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
          <!-- 1-Ustun: Baza va Saqlash Parametrlari -->
          <div class="space-y-4">
            <div class="border-b border-slate-200 pb-2">
              <h3 class="text-xs font-bold uppercase tracking-wider text-slate-500 flex items-center gap-1.5">
                <i class="pi pi-database text-emerald-600"></i>
                1. Baza va Saqlash
              </h3>
            </div>

            <!-- Job Name -->
            <div>
              <label class="block text-xs font-bold text-slate-800 mb-1">
                Reja Nomi (Identifier) <span class="text-rose-500">*</span>
              </label>
              <input
                type="text"
                v-model="form.name"
                placeholder="masalan: postgres-main-backup"
                class="w-full rounded-xl border border-slate-300 px-3.5 py-2 font-mono text-xs font-semibold text-slate-900 focus:border-emerald-500 focus:outline-hidden"
              />
            </div>

            <!-- Source Connection -->
            <div>
              <label class="block text-xs font-bold text-slate-800 mb-1">
                Ma'lumotlar Bazasi Ulanishi <span class="text-rose-500">*</span>
              </label>
              <select
                v-model="form.connectionProfileId"
                @change="onConnectionChange"
                class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 text-xs font-semibold text-slate-900 focus:border-emerald-500 focus:outline-hidden"
              >
                <option v-for="c in store.connections" :key="c.id" :value="c.id">
                  {{ c.name }} ({{ c.type.toUpperCase() }} @ {{ c.host }}:{{ c.port }})
                </option>
              </select>
            </div>

            <!-- Database Scope & Checkbox Picker -->
            <div>
              <div class="flex items-center justify-between mb-1.5">
                <label class="block text-xs font-bold text-slate-800">
                  Zaxiralanadigan Baza(lar) <span class="text-rose-500">*</span>
                </label>
                <button
                  type="button"
                  @click="discoverDatabases"
                  :disabled="discoveringDbs"
                  class="text-[11px] font-semibold text-emerald-700 hover:text-emerald-800 cursor-pointer flex items-center gap-1"
                >
                  <i :class="discoveringDbs ? 'pi pi-spin pi-spinner' : 'pi pi-search'" class="text-[10px]"></i>
                  <span>{{ discoveringDbs ? 'Qidirilmoqda...' : 'Serverdan bazalarni olish' }}</span>
                </button>
              </div>

              <!-- Discovered DB Checkboxes Container -->
              <div v-if="discoveredDatabases.length > 0" class="mb-2 rounded-xl border border-slate-200 bg-slate-50/70 p-3 space-y-2.5">
                <!-- Select All (ALL) Master Checkbox Row -->
                <div class="flex items-center justify-between border-b border-slate-200/80 pb-2">
                  <label class="flex items-center gap-2 cursor-pointer select-none text-xs font-bold text-slate-800 hover:text-emerald-700 transition">
                    <input
                      type="checkbox"
                      :checked="isAllSelected"
                      @change="toggleAllDbs"
                      class="h-4 w-4 rounded border-slate-300 text-emerald-600 focus:ring-emerald-500 cursor-pointer"
                    />
                    <span>Barchasi (ALL)</span>
                  </label>
                  <span class="text-[11px] font-semibold text-slate-500">
                    {{ selectedCountText }}
                  </span>
                </div>

                <!-- Database Checkbox Grid -->
                <div class="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 gap-1.5 max-h-48 overflow-y-auto pr-1">
                  <label
                    v-for="db in discoveredDatabases"
                    :key="db"
                    class="flex items-center gap-2 rounded-lg border px-2.5 py-1.5 text-xs cursor-pointer transition select-none truncate"
                    :class="isDbSelected(db)
                      ? 'border-emerald-300 bg-emerald-50/90 text-emerald-950 font-semibold shadow-2xs'
                      : 'border-slate-200 bg-white text-slate-700 hover:bg-slate-100/70'"
                  >
                    <input
                      type="checkbox"
                      :checked="isDbSelected(db)"
                      @change="toggleDb(db)"
                      class="h-3.5 w-3.5 rounded border-slate-300 text-emerald-600 focus:ring-emerald-500 cursor-pointer shrink-0"
                    />
                    <span class="truncate" :title="db">{{ db }}</span>
                  </label>
                </div>
              </div>

              <input
                type="text"
                v-model="form.databaseNames"
                placeholder="ALL (barcha bazalar) yoki aniq nom"
                class="w-full rounded-xl border border-slate-300 px-3.5 py-2 font-mono text-xs font-semibold text-slate-900 focus:border-emerald-500 focus:outline-hidden"
              />
              <span class="text-[10px] text-slate-500 mt-0.5 block">
                Barcha bazalar uchun <code>ALL</code> qoldiring yoki yuqoridagi checkboxlardan tanlang.
              </span>
            </div>

            <!-- Storage Destination -->
            <div>
              <label class="block text-xs font-bold text-slate-800 mb-1">
                Saqlash Ombori <span class="text-rose-500">*</span>
              </label>
              <select
                v-model="form.storageProfileId"
                class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 text-xs font-semibold text-slate-900 focus:border-emerald-500 focus:outline-hidden"
              >
                <option v-for="s in store.storageProfiles" :key="s.id" :value="s.id">
                  {{ s.name }} ({{ s.backend }}: {{ s.path || s.bucket }})
                </option>
              </select>
            </div>

            <!-- Strategy & Encryption Grid -->
            <div class="grid grid-cols-2 gap-3 pt-1">
              <div>
                <label class="block text-xs font-bold text-slate-800 mb-1">Strategiya</label>
                <select
                  v-model="form.strategy"
                  class="w-full rounded-xl border border-slate-300 bg-white px-3 py-2 text-xs font-semibold text-slate-900 focus:border-emerald-500 focus:outline-hidden"
                >
                  <option value="full">To'liq (Full Backup)</option>
                  <option value="incremental">Inkremantal (Incremental)</option>
                  <option value="differential">Differensial (Differential)</option>
                </select>
              </div>

              <div>
                <label class="block text-xs font-bold text-slate-800 mb-1">Shifrlash (Ixtiyoriy)</label>
                <select
                  v-model="form.encryptionProfileId"
                  class="w-full rounded-xl border border-slate-300 bg-white px-3 py-2 text-xs font-semibold text-slate-900 focus:border-emerald-500 focus:outline-hidden"
                >
                  <option :value="null">Shifrlashsiz</option>
                  <option v-for="e in store.encryptionProfiles" :key="e.id" :value="e.id">
                    {{ e.name }} ({{ e.type.toUpperCase() }})
                  </option>
                </select>
              </div>
            </div>

            <!-- Strategy Explanatory Note -->
            <div class="rounded-xl border border-slate-200 bg-slate-50/70 p-2.5 text-[11px] text-slate-600">
              <div v-if="form.strategy === 'full'" class="flex items-start gap-1.5">
                <i class="pi pi-database text-emerald-600 text-xs mt-0.5"></i>
                <div>
                  <strong class="text-slate-800">To'liq Zaxira (Full):</strong>
                  Har gal barcha jadvallar va ma'lumotlar to'liq olinadi. Tiklash eng tez va xavfsiz.
                </div>
              </div>
              <div v-else-if="form.strategy === 'incremental'" class="flex items-start gap-1.5">
                <i class="pi pi-bolt text-indigo-600 text-xs mt-0.5"></i>
                <div>
                  <strong class="text-indigo-900">Inkremantal Zaxira (Incremental):</strong>
                  Faqat oldingi zaxiradan keyingi yangi o'zgarishlar olinadi. Disk va tarmoq trafigini maksimal tejaydi.
                </div>
              </div>
              <div v-else-if="form.strategy === 'differential'" class="flex items-start gap-1.5">
                <i class="pi pi-sliders-h text-purple-600 text-xs mt-0.5"></i>
                <div>
                  <strong class="text-purple-900">Differensial Zaxira (Differential):</strong>
                  Oxirgi To'liq (Full) nusxadan keyingi jamlangan barcha o'zgarishlar olinadi. Tiklash uchun faqat 1 ta Full + oxirgi Diff yetarli.
                </div>
              </div>
            </div>

            <!-- Incremental & Differential Chain Controls (full_every & full_after) -->
            <div
              v-if="form.strategy === 'incremental' || form.strategy === 'differential'"
              class="rounded-xl border border-indigo-200 bg-indigo-50/50 p-3 space-y-2.5 transition-all"
            >
              <div class="flex items-center justify-between border-b border-indigo-200/60 pb-1.5">
                <span class="text-xs font-bold text-indigo-950 flex items-center gap-1.5">
                  <i class="pi pi-sync text-indigo-600 text-xs"></i>
                  To'liq Zaxira Zanjirini Yangilash (Full Sync)
                </span>
                <span class="text-[10px] text-indigo-700 font-mono">full_every &amp; full_after</span>
              </div>
              <div class="grid grid-cols-2 gap-2.5">
                <div>
                  <label class="block text-[11px] font-semibold text-slate-700 mb-1">
                    Har N-marta to'liq zaxira:
                  </label>
                  <input
                    type="number"
                    min="0"
                    max="100"
                    v-model="form.fullEvery"
                    placeholder="7"
                    class="w-full rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-xs font-semibold text-slate-900 focus:border-indigo-500 focus:outline-hidden"
                  />
                  <span class="text-[10px] text-slate-500 mt-0.5 block">0 = o'chirilgan, 7 = har 7-zaxirada Full</span>
                </div>
                <div>
                  <label class="block text-[11px] font-semibold text-slate-700 mb-1">
                    Vaqt bo'yicha to'liq zaxira:
                  </label>
                  <input
                    type="text"
                    v-model="form.fullAfter"
                    placeholder="7d yoki 24h"
                    class="w-full rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-xs font-semibold text-slate-900 focus:border-indigo-500 focus:outline-hidden"
                  />
                  <span class="text-[10px] text-slate-500 mt-0.5 block">Masalan: <code>7d</code> yoki <code>14d</code></span>
                </div>
              </div>
            </div>

            <!-- Additional Dump Options: schemaOnly, createLatest, splitDb -->
            <div class="space-y-1.5 pt-1">
              <label class="block text-xs font-bold text-slate-800">Qo'shimcha Strategiya Imkoniyatlari</label>
              <div class="space-y-1.5 text-xs">
                <label class="flex items-start gap-2.5 rounded-xl border border-slate-200 bg-slate-50/70 p-2.5 cursor-pointer hover:bg-slate-100/70 select-none transition">
                  <input type="checkbox" v-model="form.schemaOnly" class="rounded accent-emerald-600 h-4 w-4 mt-0.5" />
                  <div class="flex-1">
                    <span class="font-bold text-slate-800">Faqat Sxema / Struktura (Schema-only)</span>
                    <span class="text-[11px] text-slate-500 block">Jadval ma'lumotlarsiz faqat DDL strukturasini zaxiralash</span>
                  </div>
                </label>
                <label class="flex items-start gap-2.5 rounded-xl border border-slate-200 bg-slate-50/70 p-2.5 cursor-pointer hover:bg-slate-100/70 select-none transition">
                  <input type="checkbox" v-model="form.createLatest" class="rounded accent-emerald-600 h-4 w-4 mt-0.5" />
                  <div class="flex-1">
                    <span class="font-bold text-slate-800">Eng so'nggi nusxa fayli (create_latest)</span>
                    <span class="text-[11px] text-slate-500 block">Zaxiralash yakunida alohida <code>latest-...</code> faylini ham yaratadi</span>
                  </div>
                </label>
                <label class="flex items-start gap-2.5 rounded-xl border border-slate-200 bg-slate-50/70 p-2.5 cursor-pointer hover:bg-slate-100/70 select-none transition">
                  <input type="checkbox" v-model="form.splitDb" class="rounded accent-emerald-600 h-4 w-4 mt-0.5" />
                  <div class="flex-1">
                    <span class="font-bold text-slate-800">Har bir bazani alohida faylga ajratish (Split DB)</span>
                    <span class="text-[11px] text-slate-500 block">Bir nechta baza tanlangan bo'lsa, har birini o'z fayliga saqlaydi</span>
                  </div>
                </label>
                <label
                  v-if="connEngine === 'postgres'"
                  class="flex items-start gap-2.5 rounded-xl border border-indigo-200 bg-indigo-50/50 p-2.5 cursor-pointer hover:bg-indigo-100/60 select-none transition"
                >
                  <input type="checkbox" v-model="form.postgresGlobals" class="rounded accent-indigo-600 h-4 w-4 mt-0.5" />
                  <div class="flex-1">
                    <span class="font-bold text-indigo-900">PostgreSQL Klaster Global Obyektlari</span>
                    <span class="text-[11px] text-indigo-700 block">Foydalanuvchi rollari, ruxsatlar va tablespace'lar saqlanadi</span>
                  </div>
                </label>
              </div>
            </div>
          </div>

          <!-- 2-Ustun: Ishga Tushirish Tartibi va Trigerlar -->
          <div class="space-y-4">
            <div class="border-b border-slate-200 pb-2">
              <h3 class="text-xs font-bold uppercase tracking-wider text-slate-500 flex items-center gap-1.5">
                <i class="pi pi-play text-emerald-600"></i>
                2. Ishga Tushirish Tartibi (Triger)
              </h3>
            </div>

            <!-- Trigger Mode Selection (Manual vs Scheduled) -->
            <div class="space-y-2">
              <label class="block text-xs font-bold text-slate-800">
                Ishga Tushirish Rejimi <span class="text-rose-500">*</span>
              </label>

              <!-- Option 1: Manual Trigger -->
              <div
                @click="triggerMode = 'manual'"
                class="flex items-start gap-3 rounded-xl border p-3.5 transition cursor-pointer select-none"
                :class="
                  triggerMode === 'manual'
                    ? 'border-emerald-600 bg-emerald-50/60 ring-2 ring-emerald-600/20'
                    : 'border-slate-200 bg-white hover:border-slate-300'
                "
              >
                <input
                  type="radio"
                  name="triggerMode"
                  value="manual"
                  :checked="triggerMode === 'manual'"
                  class="mt-0.5 accent-emerald-600"
                />
                <div class="flex-1">
                  <div class="flex items-center gap-1.5">
                    <i class="pi pi-hand text-emerald-700 text-xs"></i>
                    <span class="text-xs font-bold text-slate-900">Faqat qo'lda ishga tushirish (Manual Trigger)</span>
                  </div>
                  <p class="text-[11px] text-slate-500 mt-0.5">
                    Avtomatik jadval o'rnatilmaydi. Faqat siz tugmani bosganingizda ishga tushiriladi.
                  </p>
                </div>
              </div>

              <!-- Option 2: Automatic Schedule -->
              <div
                @click="triggerMode = 'scheduled'; form.isEnabled = true"
                class="flex items-start gap-3 rounded-xl border p-3.5 transition cursor-pointer select-none"
                :class="
                  triggerMode === 'scheduled'
                    ? 'border-emerald-600 bg-emerald-50/60 ring-2 ring-emerald-600/20'
                    : 'border-slate-200 bg-white hover:border-slate-300'
                "
              >
                <input
                  type="radio"
                  name="triggerMode"
                  value="scheduled"
                  :checked="triggerMode === 'scheduled'"
                  class="mt-0.5 accent-emerald-600"
                />
                <div class="flex-1">
                  <div class="flex items-center gap-1.5">
                    <i class="pi pi-calendar-clock text-indigo-700 text-xs"></i>
                    <span class="text-xs font-bold text-slate-900">Avtomatik jadval bo'yicha (Scheduled Cron)</span>
                  </div>
                  <p class="text-[11px] text-slate-500 mt-0.5">
                    Hangfire fon xizmati orqali belgilangan vaqtda avtomatik zaxiralaydi.
                  </p>
                </div>
              </div>
            </div>

            <!-- Scheduled Cron Options (Only if scheduled mode is selected) -->
            <div
              v-if="triggerMode === 'scheduled'"
              class="rounded-xl border border-indigo-200 bg-indigo-50/40 p-3.5 space-y-3 transition-all"
            >
              <label class="block text-xs font-bold text-indigo-950">
                Takrorlanish Oralig'i
              </label>

              <div class="grid grid-cols-2 gap-2">
                <button
                  type="button"
                  @click="presetSchedule = 'daily'"
                  class="rounded-lg border p-2 text-left text-xs font-semibold transition cursor-pointer"
                  :class="
                    presetSchedule === 'daily'
                      ? 'border-emerald-600 bg-white text-emerald-800 shadow-2xs'
                      : 'border-slate-200 bg-white text-slate-700 hover:bg-slate-50'
                  "
                >
                  🌙 Har kuni (02:00)
                </button>
                <button
                  type="button"
                  @click="presetSchedule = '6h'"
                  class="rounded-lg border p-2 text-left text-xs font-semibold transition cursor-pointer"
                  :class="
                    presetSchedule === '6h'
                      ? 'border-emerald-600 bg-white text-emerald-800 shadow-2xs'
                      : 'border-slate-200 bg-white text-slate-700 hover:bg-slate-50'
                  "
                >
                  ⚡ Har 6 soatda
                </button>
                <button
                  type="button"
                  @click="presetSchedule = '12h'"
                  class="rounded-lg border p-2 text-left text-xs font-semibold transition cursor-pointer"
                  :class="
                    presetSchedule === '12h'
                      ? 'border-emerald-600 bg-white text-emerald-800 shadow-2xs'
                      : 'border-slate-200 bg-white text-slate-700 hover:bg-slate-50'
                  "
                >
                  ⏰ Har 12 soatda
                </button>
                <button
                  type="button"
                  @click="presetSchedule = 'weekly'"
                  class="rounded-lg border p-2 text-left text-xs font-semibold transition cursor-pointer"
                  :class="
                    presetSchedule === 'weekly'
                      ? 'border-emerald-600 bg-white text-emerald-800 shadow-2xs'
                      : 'border-slate-200 bg-white text-slate-700 hover:bg-slate-50'
                  "
                >
                  📅 Har hafta yakshanba
                </button>
              </div>

              <!-- Custom Cron Input -->
              <div class="pt-1">
                <div class="flex items-center justify-between mb-1">
                  <span class="text-[11px] font-semibold text-slate-700">Maxsus Cron ifodasi:</span>
                  <button
                    type="button"
                    @click="presetSchedule = 'custom'"
                    class="text-[10px] text-indigo-700 hover:underline cursor-pointer"
                  >
                    Cron kiritish
                  </button>
                </div>
                <input
                  type="text"
                  v-model="customCron"
                  @focus="presetSchedule = 'custom'"
                  placeholder="0 2 * * *"
                  class="w-full rounded-xl border border-slate-300 bg-white px-3 py-1.5 font-mono text-xs text-slate-800 focus:border-indigo-500 focus:outline-hidden"
                />
              </div>
            </div>

            <!-- Retention Policy (Simple keep last N count) -->
            <div>
              <label class="block text-xs font-bold text-slate-800 mb-1">
                Saqlanadigan Oxirgi Zaxiralar Soni (Retention)
              </label>
              <div class="flex items-center gap-2">
                <input
                  type="number"
                  min="1"
                  max="100"
                  v-model="form.retentionLast"
                  class="w-24 rounded-xl border border-slate-300 px-3 py-2 text-xs font-semibold text-slate-900 focus:border-emerald-500 focus:outline-hidden"
                />
                <span class="text-xs text-slate-500">ta eng so'nggi nusxa saqlanadi (eskisi avto-o'chadi)</span>
              </div>
            </div>

            <!-- Active / Enabled Status Card -->
            <div
              class="rounded-xl border p-3 flex items-center justify-between transition-all"
              :class="form.isEnabled ? 'border-emerald-200 bg-emerald-50/70' : 'border-slate-200 bg-slate-50'"
            >
              <div class="flex items-center gap-2.5">
                <span
                  class="flex h-7 w-7 items-center justify-center rounded-lg text-xs"
                  :class="form.isEnabled ? 'bg-emerald-600 text-white shadow-2xs' : 'bg-slate-200 text-slate-500'"
                >
                  <i class="pi" :class="form.isEnabled ? 'pi-check' : 'pi-pause'"></i>
                </span>
                <div>
                  <div class="text-xs font-bold" :class="form.isEnabled ? 'text-emerald-950' : 'text-slate-700'">
                    Reja Holati: {{ form.isEnabled ? 'Faol (Avtomatik ishlaydi)' : 'To\'xtatilgan (Nofaol)' }}
                  </div>
                  <div class="text-[11px]" :class="form.isEnabled ? 'text-emerald-700' : 'text-slate-500'">
                    {{ form.isEnabled ? 'Yaratilgach darhol jadval bo\'yicha avtomatik ishlaydi' : 'Faqat qo\'lda yoqilganda ishlaydi' }}
                  </div>
                </div>
              </div>
              <label class="relative inline-flex items-center cursor-pointer select-none">
                <input type="checkbox" v-model="form.isEnabled" class="sr-only peer" />
                <div class="w-9 h-5 bg-slate-300 peer-focus:outline-hidden rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-slate-300 after:border after:rounded-full after:h-4 after:w-4 after:transition-all peer-checked:bg-emerald-600"></div>
              </label>
            </div>
          </div>
        </div>
      </div>

      <!-- Modal Footer -->
      <div class="flex items-center justify-between border-t border-slate-200 bg-slate-50 px-6 py-4">
        <button
          type="button"
          @click="emit('close')"
          class="rounded-xl border border-slate-200 bg-white px-4 py-2 text-xs font-semibold text-slate-600 hover:bg-slate-100 cursor-pointer transition"
        >
          Bekor qilish
        </button>

        <div class="flex items-center gap-2.5">
          <!-- Manual Trigger Button: Save & Run Now -->
          <button
            type="button"
            @click="submitJob(true)"
            :disabled="saving"
            class="inline-flex items-center gap-2 rounded-xl border border-emerald-600 bg-emerald-50 px-4 py-2 text-xs font-bold text-emerald-800 hover:bg-emerald-100 cursor-pointer shadow-2xs transition disabled:opacity-50"
            title="Rejani saqlab, hoziroq qo'lda zaxiralashni boshlash"
          >
            <i class="pi pi-play text-xs text-emerald-600"></i>
            <span>Saqlash va Hoziroq Ishga Tushirish</span>
          </button>

          <!-- Standard Save Button -->
          <button
            type="button"
            @click="submitJob(false)"
            :disabled="saving"
            class="inline-flex items-center gap-2 rounded-xl bg-emerald-600 px-5 py-2 text-xs font-bold text-white hover:bg-emerald-500 cursor-pointer shadow-sm transition disabled:opacity-50"
          >
            <i :class="saving ? 'pi pi-spin pi-spinner' : 'pi pi-check'"></i>
            <span>{{ props.job ? 'O\'zgarishlarni Saqlash' : 'Rejani Saqlash' }}</span>
          </button>
        </div>
      </div>
    </div>
  </div>
</template>
