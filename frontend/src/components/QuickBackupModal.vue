<script setup lang="ts">
import { ref } from 'vue'
import { useDbaStore, ENGINE_METADATA } from '@/stores/dbaStore'
import { api } from '@/services/api'
import { useModalBackdrop } from '@/composables/useModalBackdrop'
import type { BackupJob } from '@/types/models'

const props = defineProps<{
  show: boolean
}>()

const emit = defineEmits<{
  (e: 'close'): void
  (e: 'started'): void
}>()

const { onBackdropMouseDown, onBackdropClick } = useModalBackdrop(() => emit('close'))

const store = useDbaStore()
const selectedJobId = ref<number | null>(null)
const isDryRun = ref(false)
const running = ref(false)

function onShow() {
  if (store.jobs.length > 0 && !selectedJobId.value) {
    const active = store.jobs.find((j) => j.isEnabled) || store.jobs[0]
    selectedJobId.value = active?.id || null
  }
}

async function triggerQuickBackup() {
  if (!selectedJobId.value) {
    store.notify('warn', 'Select Job', 'Please select a backup job to execute.')
    return
  }

  running.value = true
  try {
    const job = store.jobs.find((j) => j.id === selectedJobId.value)
    store.liveDrawerOpen = true
    emit('close')
    await api.runJob(selectedJobId.value, isDryRun.value)
    store.notify(
      'info',
      isDryRun.value ? 'Dry-Run In Progress' : 'Backup Triggered',
      `Executing "${job?.name || selectedJobId.value}"...`
    )
    emit('started')
  } catch (e: unknown) {
    store.notify('error', 'Trigger Failed', (e as Error).message)
  } finally {
    running.value = false
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
      class="flex w-full max-w-lg flex-col rounded-2xl border border-slate-200 bg-white shadow-2xl"
    >
      <!-- Header -->
      <div class="flex items-center justify-between border-b border-slate-200 px-6 py-4">
        <div class="flex items-center gap-3">
          <div
            class="flex h-10 w-10 items-center justify-center rounded-xl bg-emerald-50 text-emerald-700 border border-emerald-200"
          >
            <i class="pi pi-play text-lg"></i>
          </div>
          <div>
            <h2 class="text-base font-bold text-slate-900">1-Click Immediate Backup</h2>
            <p class="text-xs text-slate-500">Trigger an on-demand database snapshot right now</p>
          </div>
        </div>

        <button
          @click="emit('close')"
          class="rounded-xl border border-slate-200 bg-slate-50 p-2 text-slate-500 hover:bg-slate-100 cursor-pointer"
        >
          <i class="pi pi-times"></i>
        </button>
      </div>

      <!-- Body -->
      <div class="p-6 space-y-4">
        <div>
          <label class="block text-xs font-bold text-slate-800 mb-1.5">Select Backup Job Pipeline</label>
          <div class="space-y-2">
            <label
              v-for="job in store.jobs"
              :key="job.id"
              class="flex items-center justify-between rounded-xl border p-3 cursor-pointer transition"
              :class="
                selectedJobId === job.id
                  ? 'border-emerald-500 bg-emerald-50/40 ring-2 ring-emerald-500/20'
                  : 'border-slate-200 bg-white hover:border-slate-300'
              "
            >
              <div class="flex items-center gap-3">
                <input
                  type="radio"
                  name="quickJob"
                  :value="job.id"
                  v-model="selectedJobId"
                  class="accent-emerald-600"
                />
                <div>
                  <div class="flex items-center gap-2">
                    <span class="font-bold text-xs text-slate-900">{{ job.name }}</span>
                    <span
                      class="rounded px-1.5 py-0.2 text-[9px] font-mono font-bold uppercase"
                      :class="job.strategy === 'full' ? 'bg-emerald-100 text-emerald-800' : 'bg-blue-100 text-blue-800'"
                    >
                      {{ job.strategy }}
                    </span>
                  </div>
                  <span class="text-[11px] text-slate-500">
                    Database: {{ job.databaseNames }} • Compression: {{ job.compressionType }}
                  </span>
                </div>
              </div>

              <span
                class="rounded-full px-2 py-0.5 text-[10px] font-semibold"
                :class="job.isEnabled ? 'bg-emerald-100 text-emerald-800' : 'bg-slate-100 text-slate-500'"
              >
                {{ job.isEnabled ? 'Active' : 'Disabled' }}
              </span>
            </label>
          </div>
        </div>

        <!-- Dry Run Mode Toggle -->
        <label
          class="flex items-center justify-between rounded-xl border border-slate-200 bg-slate-50 p-3 cursor-pointer"
        >
          <div>
            <span class="text-xs font-bold text-slate-800 block">Dry-Run Simulation Mode</span>
            <span class="text-[11px] text-slate-500">Validate the dump without writing output files</span>
          </div>
          <input type="checkbox" v-model="isDryRun" class="rounded accent-amber-600 h-4 w-4" />
        </label>
      </div>

      <!-- Footer -->
      <div class="flex items-center justify-between border-t border-slate-200 bg-slate-50 px-6 py-4">
        <button
          type="button"
          @click="emit('close')"
          class="rounded-xl border border-slate-200 bg-white px-4 py-2 text-xs font-semibold text-slate-600 hover:bg-slate-100 cursor-pointer"
        >
          Cancel
        </button>

        <button
          type="button"
          @click="triggerQuickBackup"
          :disabled="!selectedJobId || running"
          class="inline-flex items-center gap-2 rounded-xl bg-emerald-600 px-5 py-2 text-xs font-bold text-white shadow-sm shadow-emerald-600/20 hover:bg-emerald-500 disabled:opacity-50 cursor-pointer"
        >
          <i :class="running ? 'pi pi-spin pi-spinner' : 'pi pi-play'"></i>
          {{ isDryRun ? 'Launch Dry-Run' : 'Start Backup Now' }}
        </button>
      </div>
    </div>
  </div>
</template>
