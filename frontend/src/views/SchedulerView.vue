<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useDbaStore, formatDateTime, ENGINE_METADATA } from '@/stores/dbaStore'
import { api } from '@/services/api'
import type { SchedulerJobInfo } from '@/types/models'

const store = useDbaStore()
const activeTab = ref<'recurring' | 'hangfire-ui'>('recurring')
const busyJobId = ref<number | null>(null)

onMounted(async () => {
  await store.fetchSchedulerJobs()
})

async function triggerNow(item: SchedulerJobInfo) {
  busyJobId.value = item.jobId
  try {
    store.liveDrawerOpen = true
    await api.triggerSchedulerJob(item.jobId)
    store.notify('info', 'Reja Ishga Tushirildi', `"${item.jobName}" rejasi darhol bajarilishga yuborildi.`)
    await store.fetchSchedulerJobs()
  } catch (e: unknown) {
    store.notify('error', 'Ishga tushirishda xatolik', (e as Error).message)
  } finally {
    busyJobId.value = null
  }
}

async function pauseJob(item: SchedulerJobInfo) {
  busyJobId.value = item.jobId
  try {
    await api.pauseSchedulerJob(item.jobId)
    store.notify('warn', 'Reja To\'xtatildi', `"${item.jobName}" avtomatik rejasi to'xtatildi.`)
    await store.fetchSchedulerJobs()
  } catch (e: unknown) {
    store.notify('error', 'To\'xtatishda xatolik', (e as Error).message)
  } finally {
    busyJobId.value = null
  }
}

async function resumeJob(item: SchedulerJobInfo) {
  busyJobId.value = item.jobId
  try {
    await api.resumeSchedulerJob(item.jobId)
    store.notify('success', 'Reja Faollashtirildi', `"${item.jobName}" rejasining avtomatik jadvallari faollashtirildi.`)
    await store.fetchSchedulerJobs()
  } catch (e: unknown) {
    store.notify('error', 'Faollashtirishda xatolik', (e as Error).message)
  } finally {
    busyJobId.value = null
  }
}
</script>

<template>
  <div class="space-y-6">
    <!-- Header -->
    <div class="flex flex-col justify-between gap-4 sm:flex-row sm:items-center">
      <div>
        <div class="flex items-center gap-2">
          <span
            class="rounded-full border border-indigo-200 bg-indigo-50 px-2.5 py-0.5 text-xs font-semibold text-indigo-700"
          >
            Avtomatlashtirilgan Rejalar
          </span>
          <span class="text-xs text-slate-500 font-medium">Vaqtbay Jadval &amp; Taqvim</span>
        </div>
        <h1 class="mt-1 text-xl font-bold text-slate-900">
          Avtomatlashtirilgan Zaxira va Profilaktika Rejalari
        </h1>
        <p class="text-xs text-slate-600">
          Barcha avtomatik jadvallarni boshqaring, istalgan vaqtda darhol ishga tushiring yoki jadvallarni tahrirlang.
        </p>
      </div>

      <div class="flex items-center gap-2.5">
        <button
          @click="store.fetchSchedulerJobs()"
          class="rounded-xl border border-slate-200 bg-white px-3.5 py-2 text-xs font-semibold text-slate-700 hover:bg-slate-50 shadow-2xs cursor-pointer"
        >
          <i class="pi pi-refresh mr-1.5"></i>Yangilash
        </button>
        <a
          :href="api.hangfireDashboardUrl"
          target="_blank"
          rel="noopener"
          class="inline-flex items-center gap-2 rounded-xl bg-indigo-600 px-4 py-2 text-xs font-semibold text-white shadow-sm shadow-indigo-600/20 hover:bg-indigo-500"
        >
          <i class="pi pi-external-link"></i>
          Kengaytirilgan Rejalar Paneli
        </a>
      </div>
    </div>

    <!-- Tabs: Recurring Jobs Table vs Embedded Hangfire Dashboard -->
    <div class="flex border-b border-slate-200">
      <button
        @click="activeTab = 'recurring'"
        class="flex items-center gap-2 border-b-2 px-5 py-3 text-xs font-semibold transition cursor-pointer"
        :class="
          activeTab === 'recurring'
            ? 'border-emerald-600 text-emerald-700'
            : 'border-transparent text-slate-500 hover:text-slate-900'
        "
      >
        <i class="pi pi-calendar-clock"></i>
        Rejalashtirilgan Zaxira Vazifalari ({{ store.schedulerJobs.length }})
      </button>
      <button
        @click="activeTab = 'hangfire-ui'"
        class="flex items-center gap-2 border-b-2 px-5 py-3 text-xs font-semibold transition cursor-pointer"
        :class="
          activeTab === 'hangfire-ui'
            ? 'border-indigo-600 text-indigo-700'
            : 'border-transparent text-slate-500 hover:text-slate-900'
        "
      >
        <i class="pi pi-gauge"></i>
        Server Bajarilish Grafigi va Monitoring
      </button>
    </div>

    <!-- TAB 1: Recurring Jobs Management Table -->
    <div
      v-if="activeTab === 'recurring'"
      class="rounded-2xl border border-slate-200 bg-white shadow-2xs overflow-hidden"
    >
      <div class="overflow-x-auto">
        <table class="w-full text-left text-xs">
          <thead>
            <tr class="border-b border-slate-200 bg-slate-50 text-[11px] uppercase text-slate-500">
              <th class="px-4 py-3.5">Vazifa Nomi &amp; Baza</th>
              <th class="px-3 py-3.5">Rejalashtirilgan Vaqt (Jadval)</th>
              <th class="px-3 py-3.5">Reja Holati</th>
              <th class="px-3 py-3.5">Oxirgi Bajarilish</th>
              <th class="px-3 py-3.5">Keyingi Bajarilish</th>
              <th class="px-4 py-3.5 text-right">Amallar</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-slate-100">
            <tr v-if="store.schedulerJobs.length === 0" class="text-slate-400">
              <td colspan="6" class="py-10 text-center">No scheduled jobs registered yet.</td>
            </tr>
            <tr
              v-for="item in store.schedulerJobs"
              :key="item.jobId"
              class="hover:bg-slate-50/80"
            >
              <td class="px-4 py-3.5">
                <div class="flex items-center gap-2">
                  <span
                    class="rounded border px-2 py-0.5 font-mono text-[10px] font-semibold uppercase"
                    :class="
                      ENGINE_METADATA[item.engineType || 'sqlite']?.badgeClass ||
                      'bg-slate-100 text-slate-700 border-slate-200'
                    "
                  >
                    {{ item.engineType || 'sqlite' }}
                  </span>
                  <span class="font-mono font-bold text-slate-900">{{ item.jobName }}</span>
                </div>
                <div v-if="item.connectionName" class="mt-0.5 text-[11px] text-slate-500">
                  Connection: {{ item.connectionName }}
                </div>
              </td>

              <td class="px-3 py-3.5">
                <code
                  class="rounded border border-indigo-200 bg-indigo-50 px-2.5 py-1 font-mono text-xs font-semibold text-indigo-700"
                >
                  {{ item.scheduleCron || 'Manual' }}
                </code>
              </td>

              <td class="px-3 py-3.5">
                <span
                  class="inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-[11px] font-semibold"
                  :class="
                    item.isPaused || !item.isEnabled
                      ? 'bg-amber-50 text-amber-700 border border-amber-200'
                      : 'bg-emerald-50 text-emerald-700 border border-emerald-200'
                  "
                >
                  <span
                    class="h-1.5 w-1.5 rounded-full"
                    :class="item.isPaused || !item.isEnabled ? 'bg-amber-500' : 'bg-emerald-500'"
                  ></span>
                  {{ item.isPaused ? 'Paused' : item.triggerState || (item.isEnabled ? 'Recurring' : 'Disabled') }}
                </span>
              </td>

              <td class="px-3 py-3.5">
                <div class="text-slate-800">{{ formatDateTime(item.lastRunAt) }}</div>
                <div
                  class="text-[10px] font-mono font-semibold uppercase"
                  :class="{
                    'text-emerald-700': item.lastStatus === 'completed',
                    'text-rose-600': item.lastStatus === 'failed',
                    'text-slate-400': !item.lastStatus || item.lastStatus === 'idle',
                  }"
                >
                  {{ item.lastStatus || 'idle' }}
                </div>
              </td>

              <td class="px-3 py-3.5 font-mono font-semibold text-emerald-700">
                {{ formatDateTime(item.nextRunAt) }}
              </td>

              <td class="px-4 py-3.5 text-right">
                <div class="flex items-center justify-end gap-2">
                  <button
                    @click="triggerNow(item)"
                    :disabled="busyJobId === item.jobId"
                    class="inline-flex items-center gap-1.5 rounded-xl bg-emerald-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-emerald-500 disabled:opacity-50 cursor-pointer"
                  >
                    <i class="pi pi-bolt"></i>
                    Trigger Now
                  </button>
                  <button
                    v-if="!item.isPaused && item.isEnabled"
                    @click="pauseJob(item)"
                    :disabled="busyJobId === item.jobId"
                    class="inline-flex items-center gap-1 rounded-xl border border-amber-200 bg-amber-50 px-3 py-1.5 text-xs font-semibold text-amber-800 hover:bg-amber-100 cursor-pointer"
                  >
                    <i class="pi pi-pause"></i>
                    Pause
                  </button>
                  <button
                    v-else
                    @click="resumeJob(item)"
                    :disabled="busyJobId === item.jobId"
                    class="inline-flex items-center gap-1 rounded-xl border border-indigo-200 bg-indigo-50 px-3 py-1.5 text-xs font-semibold text-indigo-700 hover:bg-indigo-100 cursor-pointer"
                  >
                    <i class="pi pi-play"></i>
                    Resume
                  </button>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <!-- TAB 2: Embedded Hangfire Dashboard -->
    <div
      v-else
      class="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-2xs"
    >
      <div class="flex items-center justify-between border-b border-slate-200 bg-slate-50 px-4 py-2.5 text-xs">
        <span class="font-medium text-slate-700">
          Server Bajarilish Grafigi va Monitoring
        </span>
        <a
          :href="api.hangfireDashboardUrl"
          target="_blank"
          rel="noopener"
          class="font-semibold text-indigo-700 hover:underline"
        >
          Alohida oynada ochish <i class="pi pi-external-link ml-1"></i>
        </a>
      </div>
      <iframe
        :src="api.hangfireDashboardUrl"
        class="h-[640px] w-full border-0 bg-white"
        title="Server Bajarilish Grafigi"
      ></iframe>
    </div>
  </div>
</template>
