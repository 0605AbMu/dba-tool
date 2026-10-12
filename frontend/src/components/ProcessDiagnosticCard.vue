<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useDbaStore } from '@/stores/dbaStore'
import { api } from '@/services/api'
import type { ProcessErrorDiagnostic } from '@/types/models'

const props = defineProps<{
  diagnostic: ProcessErrorDiagnostic
  jobId?: number | null
  jobName?: string
}>()

const router = useRouter()
const store = useDbaStore()
const copied = ref(false)
const showTechDetails = ref(false)
const retrying = ref(false)

function copyReport() {
  const text = `DBA TOOL - XATOLIK HISOBOTI:
Jarayon: ${props.jobName || 'Zaxira jarayoni'}
Bosqich: ${props.diagnostic.stage}
Xatolik: ${props.diagnostic.title}
Sababi: ${props.diagnostic.summary}
Tafsilot: ${props.diagnostic.technicalDetails || 'Noma\'lum'}
Tavsiyalar:
${props.diagnostic.actionableSteps.map((s, i) => `${i + 1}. ${s}`).join('\n')}`

  navigator.clipboard.writeText(text).then(() => {
    copied.value = true
    store.notify('info', 'Nusxalandi', 'Xatolik hisoboti buferga nusxalandi.')
    setTimeout(() => {
      copied.value = false
    }, 2500)
  })
}

function navigateToFix() {
  if (props.diagnostic.suggestedActionUrl) {
    store.liveDrawerOpen = false
    router.push(props.diagnostic.suggestedActionUrl)
  }
}

async function retryJob() {
  if (!props.jobId) return
  retrying.value = true
  try {
    store.liveDrawerOpen = true
    await api.runJob(props.jobId, false)
    store.notify('info', 'Qayta Ishga Tushirildi', `"${props.jobName}" qayta boshlandi...`)
  } catch (e: unknown) {
    store.notify('error', 'Qayta ishga tushirishda xatolik', (e as Error).message)
  } finally {
    retrying.value = false
  }
}
</script>

<template>
  <div class="rounded-2xl border border-rose-200 bg-rose-50/70 p-5 shadow-xs space-y-4">
    <!-- Header -->
    <div class="flex items-start justify-between gap-3">
      <div class="flex items-start gap-3">
        <div class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-rose-600 text-white shadow-md shadow-rose-600/30">
          <i class="pi pi-exclamation-triangle text-lg font-bold"></i>
        </div>
        <div>
          <div class="flex flex-wrap items-center gap-2">
            <h3 class="text-sm font-bold text-rose-950">
              {{ diagnostic.title }}
            </h3>
            <span class="rounded-md border border-rose-300 bg-rose-100 px-2 py-0.5 font-mono text-[11px] font-bold text-rose-800">
              Bosqich: {{ diagnostic.stage }}
            </span>
          </div>
          <p class="mt-1 text-xs text-rose-900 leading-relaxed font-medium">
            {{ diagnostic.summary }}
          </p>
        </div>
      </div>

      <button
        type="button"
        @click="copyReport"
        class="shrink-0 inline-flex items-center gap-1.5 rounded-xl border border-rose-300 bg-white px-2.5 py-1.5 text-xs font-semibold text-rose-700 hover:bg-rose-50 cursor-pointer transition shadow-2xs"
        title="Xatolik hisobotini nusxalash"
      >
        <i :class="copied ? 'pi pi-check text-emerald-600' : 'pi pi-copy'"></i>
        <span>{{ copied ? 'Nusxalandi' : 'Nusxalash' }}</span>
      </button>
    </div>

    <!-- Actionable Troubleshooting Checklist -->
    <div class="rounded-xl border border-rose-200 bg-white/90 p-4 space-y-2">
      <div class="flex items-center gap-1.5 text-xs font-bold text-slate-800">
        <i class="pi pi-wrench text-rose-600"></i>
        <span>Qanday tuzatish kerak? (Tavsiya etilgan amallar):</span>
      </div>

      <ul class="space-y-1.5 text-xs text-slate-700 pl-1">
        <li
          v-for="(step, idx) in diagnostic.actionableSteps"
          :key="idx"
          class="flex items-start gap-2"
        >
          <span class="flex h-4 w-4 shrink-0 items-center justify-center rounded-full bg-rose-100 text-[10px] font-bold text-rose-700 mt-0.5">
            {{ idx + 1 }}
          </span>
          <span class="leading-relaxed">{{ step }}</span>
        </li>
      </ul>
    </div>

    <!-- Collapsible Technical Error String -->
    <div v-if="diagnostic.technicalDetails" class="space-y-1.5">
      <button
        type="button"
        @click="showTechDetails = !showTechDetails"
        class="inline-flex items-center gap-1 text-[11px] font-semibold text-rose-700 hover:text-rose-900 cursor-pointer"
      >
        <i :class="showTechDetails ? 'pi pi-chevron-down' : 'pi pi-chevron-right'"></i>
        <span>{{ showTechDetails ? 'Texnik xatolik matnini yashirish' : 'Texnik xatolik matnini ko\'rish' }}</span>
      </button>

      <div
        v-if="showTechDetails"
        class="rounded-xl border border-rose-200 bg-slate-900 p-3 font-mono text-[11px] text-rose-300 overflow-x-auto whitespace-pre-wrap max-h-36"
      >
        {{ diagnostic.technicalDetails }}
      </div>
    </div>

    <!-- Quick Action Buttons -->
    <div class="flex flex-wrap items-center justify-end gap-2.5 pt-1 border-t border-rose-200/60">
      <button
        v-if="diagnostic.suggestedActionUrl"
        type="button"
        @click="navigateToFix"
        class="inline-flex items-center gap-1.5 rounded-xl bg-slate-900 px-4 py-2 text-xs font-semibold text-white hover:bg-slate-800 cursor-pointer shadow-sm transition"
      >
        <i class="pi pi-arrow-up-right"></i>
        <span>Sozlamalarga o'tish</span>
      </button>

      <button
        v-if="jobId"
        type="button"
        @click="retryJob"
        :disabled="retrying"
        class="inline-flex items-center gap-1.5 rounded-xl bg-rose-600 px-4 py-2 text-xs font-semibold text-white hover:bg-rose-500 disabled:opacity-50 cursor-pointer shadow-sm shadow-rose-600/30 transition"
      >
        <i :class="retrying ? 'pi pi-spin pi-spinner' : 'pi pi-refresh'"></i>
        <span>{{ retrying ? 'Qayta urinilmoqda...' : 'Qayta ishga tushirish' }}</span>
      </button>
    </div>
  </div>
</template>
