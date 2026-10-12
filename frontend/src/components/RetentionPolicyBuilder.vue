<script setup lang="ts">
import { ref, computed } from 'vue'

const props = withDefaults(
  defineProps<{
    retentionLast?: number
    retentionDaily?: number
    retentionWeekly?: number
    retentionMonthly?: number
    retentionYearly?: number
    retentionWithin?: string | null
    disabled?: boolean
  }>(),
  {
    retentionLast: 5,
    retentionDaily: 7,
    retentionWeekly: 4,
    retentionMonthly: 6,
    retentionYearly: 2,
    retentionWithin: '',
    disabled: false,
  }
)

const emit = defineEmits<{
  (e: 'update:retentionLast', val: number): void
  (e: 'update:retentionDaily', val: number): void
  (e: 'update:retentionWeekly', val: number): void
  (e: 'update:retentionMonthly', val: number): void
  (e: 'update:retentionYearly', val: number): void
  (e: 'update:retentionWithin', val: string): void
}>()

type RetentionMode = 'simple' | 'gfs'
const mode = ref<RetentionMode>('simple')

const simplePresets = [
  { label: '7 Days Rolling', desc: 'Lightweight dev/test', last: 5, daily: 7, weekly: 1, monthly: 0, yearly: 0, badge: 'Dev' },
  { label: '14 Days Standard', desc: 'Standard production', last: 10, daily: 14, weekly: 4, monthly: 1, yearly: 0, badge: 'Standard' },
  { label: '30 Days Extended', desc: 'Compliance & audit', last: 15, daily: 30, weekly: 4, monthly: 3, yearly: 0, badge: 'Prod' },
  { label: '1 Year GFS Long-Term', desc: 'Full enterprise compliance', last: 20, daily: 7, weekly: 4, monthly: 12, yearly: 1, badge: 'Enterprise' },
]

function applySimplePreset(p: (typeof simplePresets)[0]) {
  emit('update:retentionLast', p.last)
  emit('update:retentionDaily', p.daily)
  emit('update:retentionWeekly', p.weekly)
  emit('update:retentionMonthly', p.monthly)
  emit('update:retentionYearly', p.yearly)
}

const totalRetainedEstimate = computed(() => {
  const d = props.retentionDaily || 0
  const w = props.retentionWeekly || 0
  const m = props.retentionMonthly || 0
  const y = props.retentionYearly || 0
  return Math.max(props.retentionLast || 0, d + w + m + y)
})
</script>

<template>
  <div class="space-y-3 rounded-2xl border border-slate-200 bg-slate-50/70 p-4">
    <!-- Header & Mode Tabs -->
    <div class="flex flex-wrap items-center justify-between gap-2 border-b border-slate-200/80 pb-3">
      <div class="flex items-center gap-2">
        <i class="pi pi-history text-indigo-600 text-sm"></i>
        <span class="text-xs font-bold text-slate-800">Retention &amp; Disk Pruning Policy</span>
      </div>

      <div class="inline-flex rounded-xl bg-slate-200/70 p-0.5 text-[11px] font-semibold text-slate-600">
        <button
          type="button"
          @click="mode = 'simple'"
          class="rounded-lg px-2.5 py-1 transition cursor-pointer"
          :class="mode === 'simple' ? 'bg-white text-indigo-700 shadow-2xs' : 'hover:text-slate-900'"
        >
          Quick Presets
        </button>
        <button
          type="button"
          @click="mode = 'gfs'"
          class="rounded-lg px-2.5 py-1 transition cursor-pointer"
          :class="mode === 'gfs' ? 'bg-white text-indigo-700 shadow-2xs' : 'hover:text-slate-900'"
        >
          Custom GFS Tiers
        </button>
      </div>
    </div>

    <!-- Active Human Description Preview Banner -->
    <div class="rounded-xl border border-indigo-200 bg-indigo-50/70 p-3 text-xs text-indigo-950">
      <div class="flex items-center justify-between font-semibold">
        <span class="flex items-center gap-1.5">
          <i class="pi pi-shield-check text-indigo-600"></i>
          Guaranteed Protection: Keep minimum <strong>{{ props.retentionLast }}</strong> latest backups
        </span>
        <span class="rounded bg-indigo-200/60 px-2 py-0.5 text-[10px] font-mono text-indigo-800">
          ~{{ totalRetainedEstimate }} max retained copies
        </span>
      </div>
      <div class="mt-1 text-[11px] text-indigo-800/80">
        Pruning automatically preserves {{ props.retentionDaily }} daily, {{ props.retentionWeekly }} weekly,
        {{ props.retentionMonthly }} monthly, and {{ props.retentionYearly }} yearly historical checkpoints.
      </div>
    </div>

    <!-- Mode 1: Simple Presets -->
    <div v-if="mode === 'simple'" class="grid grid-cols-1 gap-2 sm:grid-cols-2">
      <button
        v-for="p in simplePresets"
        :key="p.label"
        type="button"
        @click="applySimplePreset(p)"
        :disabled="disabled"
        class="flex items-start gap-3 rounded-xl border p-3 text-left transition cursor-pointer"
        :class="
          props.retentionDaily === p.daily && props.retentionWeekly === p.weekly
            ? 'border-indigo-500 bg-white ring-2 ring-indigo-500/20 shadow-2xs'
            : 'border-slate-200 bg-white hover:border-slate-300 hover:bg-slate-50/50'
        "
      >
        <div class="min-w-0 flex-1">
          <div class="flex items-center justify-between">
            <span class="text-xs font-bold text-slate-800">{{ p.label }}</span>
            <span
              class="rounded px-1.5 py-0.2 text-[9px] font-semibold uppercase"
              :class="
                props.retentionDaily === p.daily && props.retentionWeekly === p.weekly
                  ? 'bg-indigo-100 text-indigo-800'
                  : 'bg-slate-100 text-slate-500'
              "
            >
              {{ p.badge }}
            </span>
          </div>
          <div class="mt-1 text-[11px] text-slate-500">{{ p.desc }}</div>
          <div class="mt-2 flex items-center gap-2 text-[10px] font-mono text-slate-600">
            <span>Daily: {{ p.daily }}</span>
            <span>•</span>
            <span>Weekly: {{ p.weekly }}</span>
            <span>•</span>
            <span>Monthly: {{ p.monthly }}</span>
          </div>
        </div>
      </button>
    </div>

    <!-- Mode 2: GFS Custom Sliders & Tiers -->
    <div v-else class="space-y-3 rounded-xl border border-slate-200 bg-white p-4">
      <div class="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <!-- Minimum Recent Backups (last) -->
        <div class="rounded-lg border border-slate-200 bg-slate-50/50 p-2.5">
          <div class="flex items-center justify-between text-xs mb-1">
            <span class="font-semibold text-slate-700">Minimum Recent Backups</span>
            <span class="font-mono font-bold text-indigo-700">{{ props.retentionLast }}</span>
          </div>
          <input
            type="range"
            min="1"
            max="30"
            :value="props.retentionLast"
            @input="emit('update:retentionLast', Number(($event.target as HTMLInputElement).value))"
            class="w-full accent-indigo-600 cursor-pointer"
          />
          <span class="text-[10px] text-slate-500">Never prune newer than this count</span>
        </div>

        <!-- Daily Tiers -->
        <div class="rounded-lg border border-slate-200 bg-slate-50/50 p-2.5">
          <div class="flex items-center justify-between text-xs mb-1">
            <span class="font-semibold text-slate-700">Daily Backups to Keep</span>
            <span class="font-mono font-bold text-indigo-700">{{ props.retentionDaily }}</span>
          </div>
          <input
            type="range"
            min="1"
            max="60"
            :value="props.retentionDaily"
            @input="emit('update:retentionDaily', Number(($event.target as HTMLInputElement).value))"
            class="w-full accent-indigo-600 cursor-pointer"
          />
          <span class="text-[10px] text-slate-500">1 per day for N days</span>
        </div>

        <!-- Weekly Tiers -->
        <div class="rounded-lg border border-slate-200 bg-slate-50/50 p-2.5">
          <div class="flex items-center justify-between text-xs mb-1">
            <span class="font-semibold text-slate-700">Weekly Backups to Keep</span>
            <span class="font-mono font-bold text-indigo-700">{{ props.retentionWeekly }}</span>
          </div>
          <input
            type="range"
            min="0"
            max="26"
            :value="props.retentionWeekly"
            @input="emit('update:retentionWeekly', Number(($event.target as HTMLInputElement).value))"
            class="w-full accent-indigo-600 cursor-pointer"
          />
          <span class="text-[10px] text-slate-500">1 per week for N weeks</span>
        </div>

        <!-- Monthly Tiers -->
        <div class="rounded-lg border border-slate-200 bg-slate-50/50 p-2.5">
          <div class="flex items-center justify-between text-xs mb-1">
            <span class="font-semibold text-slate-700">Monthly Backups to Keep</span>
            <span class="font-mono font-bold text-indigo-700">{{ props.retentionMonthly }}</span>
          </div>
          <input
            type="range"
            min="0"
            max="24"
            :value="props.retentionMonthly"
            @input="emit('update:retentionMonthly', Number(($event.target as HTMLInputElement).value))"
            class="w-full accent-indigo-600 cursor-pointer"
          />
          <span class="text-[10px] text-slate-500">1 per month for N months</span>
        </div>
      </div>
    </div>
  </div>
</template>
