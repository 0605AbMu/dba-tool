<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
import { useRouter } from 'vue-router'
import { useDbaStore, formatBytes, formatDuration } from '@/stores/dbaStore'
import { api } from '@/services/api'
import { useModalBackdrop } from '@/composables/useModalBackdrop'
import ProcessDiagnosticCard from './ProcessDiagnosticCard.vue'

const store = useDbaStore()
const router = useRouter()
const { onBackdropMouseDown, onBackdropClick } = useModalBackdrop(() => {
  store.liveDrawerOpen = false
})
const terminalEl = ref<HTMLElement | null>(null)
const autoScroll = ref(true)
const logSearch = ref('')

watch(
  () => store.liveDrawerOpen,
  async (open) => {
    if (open) {
      await nextTick()
      if (terminalEl.value) {
        terminalEl.value.scrollTop = terminalEl.value.scrollHeight
      }
    }
  }
)

watch(
  () => store.liveLines.length,
  async () => {
    if (autoScroll.value && terminalEl.value) {
      await nextTick()
      terminalEl.value.scrollTop = terminalEl.value.scrollHeight
    }
  }
)

const filteredLines = computed(() => {
  if (!logSearch.value.trim()) return store.liveLines
  const q = logSearch.value.toLowerCase()
  return store.liveLines.filter(
    (l) => l.line.toLowerCase().includes(q) || (l.stream && l.stream.toLowerCase().includes(q))
  )
})

const activeJob = computed(() => {
  if (!store.activeJobName) return null
  return store.jobs.find((j) => j.name === store.activeJobName)
})

const completedExecution = computed(() => {
  if (!store.activeExecutionId) return null
  return store.executions.find((e) => e.id === store.activeExecutionId)
})

function downloadLog() {
  const content = store.liveLines
    .map((l) => `[${l.stream.toUpperCase()}] ${l.line}`)
    .join('\n')
  const blob = new Blob([content], { type: 'text/plain;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `dbb-execution-${store.activeExecutionId || 'live'}.log`
  a.click()
  URL.revokeObjectURL(url)
}

function viewInBackups() {
  store.liveDrawerOpen = false
  router.push('/backups')
}

async function verifyCompletedArtifact() {
  const exec = completedExecution.value
  if (!exec?.outputFile) return
  try {
    await api.verifyBackup({
      filename: exec.outputFile,
    })
  } catch (e: unknown) {
    store.notify('error', 'Tekshiruv xatosi', (e as Error).message)
  }
}

function getLineClass(line: string, parsedJson?: Record<string, unknown> | null) {
  const level = typeof parsedJson?.level === 'string' ? parsedJson.level.toLowerCase() : ''
  const lower = line.toLowerCase()

  if (
    level.includes('err') ||
    line.includes('✗') ||
    lower.includes('error') ||
    lower.includes('failed') ||
    lower.includes('denied') ||
    lower.includes('fatal')
  ) {
    return 'text-rose-400 bg-rose-950/20 px-1 rounded font-semibold'
  }
  if (level.includes('warn') || lower.includes('warn')) {
    return 'text-amber-300'
  }
  if (line.includes('✓') || lower.includes('complete') || lower.includes('success')) {
    return 'text-emerald-400'
  }
  return 'text-slate-200'
}
</script>

<template>
  <Transition name="fade">
    <div
      v-if="store.liveDrawerOpen"
      class="fixed inset-0 z-50 flex justify-end bg-slate-900/40 backdrop-blur-xs"
      @mousedown="onBackdropMouseDown"
      @click="onBackdropClick"
    >
      <div
        class="flex h-full w-full max-w-4xl flex-col border-l border-slate-200 bg-white shadow-2xl"
      >
        <!-- Top Drawer Header -->
        <div class="flex items-center justify-between border-b border-slate-200 bg-slate-50 px-6 py-4">
          <div class="flex items-center gap-3">
            <div
              class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border"
              :class="{
                'border-amber-200 bg-amber-50 text-amber-700': store.activeStatus === 'running',
                'border-emerald-200 bg-emerald-50 text-emerald-700': store.activeStatus === 'completed',
                'border-rose-200 bg-rose-50 text-rose-700': store.activeStatus === 'failed',
                'border-slate-200 bg-slate-100 text-slate-600': store.activeStatus !== 'running' && store.activeStatus !== 'completed' && store.activeStatus !== 'failed',
              }"
            >
              <i
                :class="{
                  'pi pi-spin pi-spinner text-lg': store.activeStatus === 'running',
                  'pi pi-check text-lg font-bold': store.activeStatus === 'completed',
                  'pi pi-times text-lg font-bold': store.activeStatus === 'failed',
                  'pi pi-play text-lg': store.activeStatus !== 'running' && store.activeStatus !== 'completed' && store.activeStatus !== 'failed',
                }"
              ></i>
            </div>
            <div>
              <div class="flex items-center gap-2">
                <h3 class="text-base font-bold text-slate-900">
                  {{ store.activeJobName || 'Zaxira Jarayoni' }}
                </h3>
                <span
                  v-if="store.activeExecutionId"
                  class="rounded-md border border-slate-200 bg-white px-2 py-0.5 font-mono text-[11px] font-semibold text-slate-600"
                >
                  #{{ store.activeExecutionId }}
                </span>
                <span
                  class="inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-xs font-bold"
                  :class="{
                    'bg-amber-100 text-amber-800 border border-amber-300':
                      store.activeStatus === 'running',
                    'bg-emerald-100 text-emerald-800 border border-emerald-300':
                      store.activeStatus === 'completed',
                    'bg-rose-100 text-rose-800 border border-rose-300':
                      store.activeStatus === 'failed',
                    'bg-slate-100 text-slate-700 border border-slate-300':
                      store.activeStatus !== 'running' &&
                      store.activeStatus !== 'completed' &&
                      store.activeStatus !== 'failed',
                  }"
                >
                  <span
                    v-if="store.activeStatus === 'running'"
                    class="h-1.5 w-1.5 animate-ping rounded-full bg-amber-500"
                  ></span>
                  {{
                    store.activeStatus === 'running'
                      ? 'JARAYONDA'
                      : store.activeStatus === 'completed'
                      ? 'MUVAFFAQIYATLI'
                      : store.activeStatus === 'failed'
                      ? 'XATOLIK'
                      : store.activeStatus.toUpperCase()
                  }}
                </span>
              </div>
              <p class="mt-0.5 max-w-xl truncate text-xs text-slate-500 font-medium">
                {{ store.activeCommand || 'db-backup v5.1.0 orkestratsiya jarayoni' }}
              </p>
            </div>
          </div>

          <div class="flex items-center gap-2">
            <button
              v-if="store.activeStatus === 'running'"
              @click="store.cancelActiveExecution()"
              class="inline-flex items-center gap-1.5 rounded-xl border border-rose-300 bg-rose-50 px-3 py-1.5 text-xs font-semibold text-rose-700 hover:bg-rose-100 cursor-pointer shadow-2xs"
            >
              <i class="pi pi-stop-circle"></i>
              To'xtatish
            </button>
            <button
              @click="store.liveDrawerOpen = false"
              class="flex h-8 w-8 items-center justify-center rounded-lg text-slate-400 hover:bg-slate-200/60 hover:text-slate-800 cursor-pointer"
            >
              <i class="pi pi-times"></i>
            </button>
          </div>
        </div>

        <!-- Failure Diagnostic Card (When Failed) -->
        <div v-if="store.activeDiagnostic || store.activeStatus === 'failed'" class="p-4 bg-rose-50/50 border-b border-rose-200">
          <ProcessDiagnosticCard
            :diagnostic="
              store.activeDiagnostic || {
                stage: store.activeStage || 'FAILED',
                title: 'Jarayon To\'xtatildi',
                summary: 'Zaxira nusxasi bajarilishida xatolik yuz berdi.',
                actionableSteps: [
                  'Ulanish profilida login va parolni tekshiring.',
                  'Server tarmog\'i va ruxsatlarini ko\'rib chiqing.',
                  'Job-ni qayta ishga tushiring.'
                ],
                suggestedActionUrl: '/profiles?tab=connections'
              }
            "
            :job-id="store.activeStateEvent?.jobId || activeJob?.id"
            :job-name="store.activeJobName"
          />
        </div>

        <!-- Success Compact Summary Banner (When Completed) -->
        <div
          v-else-if="store.activeStatus === 'completed' && completedExecution"
          class="border-b border-emerald-200 bg-emerald-50/50 px-6 py-3"
        >
          <div class="flex flex-wrap items-center justify-between gap-3 text-xs">
            <div class="flex items-center gap-3">
              <i class="pi pi-check-circle text-emerald-600 font-bold text-base"></i>
              <div>
                <span class="font-bold text-slate-900">{{ completedExecution.outputFile || 'Muvaffaqiyatli yakunlandi' }}</span>
                <span class="text-slate-500 ml-2">({{ formatBytes(completedExecution.outputSize) }}, {{ formatDuration(completedExecution.durationMs) }})</span>
              </div>
            </div>
            <div class="flex items-center gap-2">
              <button
                type="button"
                @click="viewInBackups"
                class="inline-flex items-center gap-1 rounded-lg border border-slate-200 bg-white px-2.5 py-1 text-xs font-semibold text-slate-700 hover:bg-slate-50 cursor-pointer"
              >
                <i class="pi pi-folder-open text-slate-500"></i>
                <span>Backups Bo'limida Ko'rish</span>
              </button>
              <button
                v-if="completedExecution.outputFile"
                type="button"
                @click="verifyCompletedArtifact"
                class="inline-flex items-center gap-1 rounded-lg bg-emerald-600 px-3 py-1 text-xs font-semibold text-white hover:bg-emerald-500 cursor-pointer"
              >
                <i class="pi pi-shield"></i>
                <span>Yaxlitlikni Tekshirish</span>
              </button>
            </div>
          </div>
        </div>

        <!-- Terminal View (Direct & Uncluttered) -->
        <div class="flex flex-1 flex-col overflow-hidden bg-slate-950 text-slate-100">
          <!-- Terminal Controls Bar -->
          <div class="flex flex-wrap items-center justify-between gap-3 border-b border-slate-800 bg-slate-900 px-5 py-2.5 text-xs">
            <div class="flex items-center gap-3">
              <span class="font-mono text-emerald-400 text-xs">Terminal Chiqishi</span>
              <span class="font-mono text-slate-400 text-xs">({{ filteredLines.length }} qator)</span>
            </div>

            <div class="flex items-center gap-3">
              <input
                v-model="logSearch"
                type="text"
                placeholder="Loglardan qidirish..."
                class="rounded-lg border border-slate-700 bg-slate-800 px-2.5 py-1 font-mono text-xs text-white placeholder-slate-500 focus:border-emerald-500 focus:outline-hidden"
              />

              <label class="flex items-center gap-1.5 text-slate-400 cursor-pointer">
                <input type="checkbox" v-model="autoScroll" class="rounded" />
                <span>Auto-scroll</span>
              </label>

              <button
                @click="downloadLog"
                class="inline-flex items-center gap-1 rounded-lg border border-slate-700 bg-slate-800 px-2.5 py-1 text-slate-300 hover:bg-slate-700 cursor-pointer"
              >
                <i class="pi pi-download"></i>
                <span>Yuklab Olish</span>
              </button>
            </div>
          </div>

          <!-- Monospace Terminal Lines -->
          <div
            ref="terminalEl"
            class="flex-1 overflow-y-auto p-4 font-mono text-xs leading-relaxed space-y-1 select-text"
          >
            <div v-if="filteredLines.length === 0" class="py-8 text-center text-slate-500">
              Hali loglar yo'q yoki qidiruv bo'yicha mos kelmadi.
            </div>
            <div
              v-for="(item, idx) in filteredLines"
              :key="idx"
              class="flex items-start gap-3 break-all py-0.5"
              :class="getLineClass(item.line, item.parsedJson)"
            >
              <span class="shrink-0 text-slate-500 select-none text-[10px] font-mono">
                {{ item.timestamp ? new Date(item.timestamp).toLocaleTimeString() : '' }}
              </span>
              <span class="flex-1 whitespace-pre-wrap font-mono">{{ item.line }}</span>
            </div>
          </div>
        </div>

        <!-- Drawer Footer -->
        <div class="flex items-center justify-between border-t border-slate-200 bg-slate-50 px-6 py-3 text-xs text-slate-500">
          <div class="flex items-center gap-2">
            <span class="h-2 w-2 rounded-full" :class="store.signalrState === 'connected' ? 'bg-emerald-500' : 'bg-rose-500'"></span>
            <span>Jonli aloqa: {{ store.signalrState === 'connected' ? 'Ulangan' : 'Kutilmoqda' }}</span>
          </div>
          <button
            @click="store.liveDrawerOpen = false"
            class="rounded-xl bg-slate-800 px-4 py-1.5 text-xs font-semibold text-white hover:bg-slate-700 cursor-pointer"
          >
            Yopish
          </button>
        </div>
      </div>
    </div>
  </Transition>
</template>
