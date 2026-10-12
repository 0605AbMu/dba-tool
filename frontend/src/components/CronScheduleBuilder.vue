<script setup lang="ts">
import { ref, computed, watch } from 'vue'

const props = withDefaults(
  defineProps<{
    modelValue?: string | null
    disabled?: boolean
  }>(),
  {
    modelValue: '0 2 * * *',
    disabled: false,
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: string): void
}>()

type BuilderMode = 'presets' | 'custom' | 'advanced'
const mode = ref<BuilderMode>('presets')

// Preset Options
const presets = [
  { label: 'Every hour', desc: 'Runs at minute 0 of every hour', cron: '0 * * * *', icon: 'pi pi-clock', badge: 'High Freq' },
  { label: 'Every 6 hours', desc: '00:00, 06:00, 12:00, 18:00', cron: '0 */6 * * *', icon: 'pi pi-sync', badge: 'Standard' },
  { label: 'Daily at 02:00 AM', desc: 'Recommended low-traffic window', cron: '0 2 * * *', icon: 'pi pi-moon', badge: 'Best Practice' },
  { label: 'Daily at 00:00 Midnight', desc: 'Runs at start of every day', cron: '0 0 * * *', icon: 'pi pi-calendar', badge: 'Standard' },
  { label: 'Twice daily (02:00 & 14:00)', desc: 'Every 12 hours at peak change', cron: '0 2,14 * * *', icon: 'pi pi-refresh', badge: 'Balanced' },
  { label: 'Weekly on Sunday 03:00', desc: 'Full snapshot once a week', cron: '0 3 * * 0', icon: 'pi pi-history', badge: 'Archival' },
  { label: 'Weekdays at 01:00 AM', desc: 'Monday through Friday only', cron: '0 1 * * 1-5', icon: 'pi pi-briefcase', badge: 'Business' },
  { label: 'Monthly on the 1st', desc: 'Monthly archive at 04:00 AM', cron: '0 4 1 * *', icon: 'pi pi-file', badge: 'Long-term' },
]

// Custom Builder State
const customFreq = ref<'hourly' | 'daily' | 'weekly'>('daily')
const customHour = ref('02')
const customMinute = ref('00')
const customDays = ref<number[]>([0]) // 0 = Sunday

const daysOfWeek = [
  { id: 1, label: 'Mon' },
  { id: 2, label: 'Tue' },
  { id: 3, label: 'Wed' },
  { id: 4, label: 'Thu' },
  { id: 5, label: 'Fri' },
  { id: 6, label: 'Sat' },
  { id: 0, label: 'Sun' },
]

// Raw Advanced Cron Input
const rawCron = ref(props.modelValue || '0 2 * * *')

watch(
  () => props.modelValue,
  (val) => {
    if (val && val !== rawCron.value) {
      rawCron.value = val
    }
  }
)

function selectPreset(cron: string) {
  rawCron.value = cron
  emit('update:modelValue', cron)
}

function updateCustomCron() {
  let expr = '0 2 * * *'
  const h = customHour.value.padStart(2, '0')
  const m = customMinute.value.padStart(2, '0')

  if (customFreq.value === 'hourly') {
    expr = `${parseInt(m, 10)} * * * *`
  } else if (customFreq.value === 'daily') {
    expr = `${parseInt(m, 10)} ${parseInt(h, 10)} * * *`
  } else if (customFreq.value === 'weekly') {
    const daysStr = customDays.value.length ? customDays.value.sort().join(',') : '*'
    expr = `${parseInt(m, 10)} ${parseInt(h, 10)} * * ${daysStr}`
  }

  rawCron.value = expr
  emit('update:modelValue', expr)
}

function toggleDay(dayId: number) {
  if (customDays.value.includes(dayId)) {
    if (customDays.value.length > 1) {
      customDays.value = customDays.value.filter((d) => d !== dayId)
    }
  } else {
    customDays.value.push(dayId)
  }
  updateCustomCron()
}

function onRawInput(val: string) {
  rawCron.value = val.trim()
  emit('update:modelValue', rawCron.value)
}

// Human Readable Description
const humanExplanation = computed(() => {
  const parts = rawCron.value.trim().split(/\s+/)
  if (parts.length !== 5) return 'Custom cron schedule'

  const [min, hour, dom, mon, dow] = parts

  if (min === '0' && hour === '*' && dom === '*' && mon === '*' && dow === '*') {
    return 'Every hour at the start of the hour'
  }
  if (min === '0' && hour.startsWith('*/') && dom === '*' && mon === '*' && dow === '*') {
    const interval = hour.replace('*/', '')
    return `Every ${interval} hours`
  }
  if (dom === '*' && mon === '*' && dow === '*' && !hour.includes('*') && !min.includes('*')) {
    return `Every day at ${hour.padStart(2, '0')}:${min.padStart(2, '0')} UTC`
  }
  if (dom === '*' && mon === '*' && dow === '1-5' && !hour.includes('*')) {
    return `Every weekday (Mon–Fri) at ${hour.padStart(2, '0')}:${min.padStart(2, '0')} UTC`
  }
  if (dom === '*' && mon === '*' && dow === '0' && !hour.includes('*')) {
    return `Every Sunday at ${hour.padStart(2, '0')}:${min.padStart(2, '0')} UTC`
  }
  if (dom === '1' && mon === '*' && dow === '*' && !hour.includes('*')) {
    return `Monthly on the 1st day at ${hour.padStart(2, '0')}:${min.padStart(2, '0')} UTC`
  }

  return `Custom: Minute ${min}, Hour ${hour}, Day ${dom}, Month ${mon}, Weekday ${dow}`
})

const isPresetActive = (cron: string) => rawCron.value.trim() === cron.trim()
</script>

<template>
  <div class="space-y-3 rounded-2xl border border-slate-200 bg-slate-50/70 p-4">
    <!-- Header & Mode Tabs -->
    <div class="flex flex-wrap items-center justify-between gap-2 border-b border-slate-200/80 pb-3">
      <div class="flex items-center gap-2">
        <i class="pi pi-calendar-clock text-emerald-600 text-sm"></i>
        <span class="text-xs font-bold text-slate-800">Schedule &amp; Execution Frequency</span>
      </div>

      <div class="inline-flex rounded-xl bg-slate-200/70 p-0.5 text-[11px] font-semibold text-slate-600">
        <button
          type="button"
          @click="mode = 'presets'"
          class="rounded-lg px-2.5 py-1 transition cursor-pointer"
          :class="mode === 'presets' ? 'bg-white text-emerald-700 shadow-2xs' : 'hover:text-slate-900'"
        >
          Common Presets
        </button>
        <button
          type="button"
          @click="mode = 'custom'"
          class="rounded-lg px-2.5 py-1 transition cursor-pointer"
          :class="mode === 'custom' ? 'bg-white text-emerald-700 shadow-2xs' : 'hover:text-slate-900'"
        >
          Visual Builder
        </button>
        <button
          type="button"
          @click="mode = 'advanced'"
          class="rounded-lg px-2.5 py-1 transition cursor-pointer"
          :class="mode === 'advanced' ? 'bg-white text-indigo-700 shadow-2xs' : 'hover:text-slate-900'"
        >
          Raw Cron
        </button>
      </div>
    </div>

    <!-- Active Human Description Preview Banner -->
    <div class="flex items-center justify-between rounded-xl border border-emerald-200 bg-emerald-50/90 px-3.5 py-2.5">
      <div class="flex items-center gap-2 text-xs">
        <i class="pi pi-check-circle text-emerald-600 text-sm"></i>
        <span class="font-medium text-emerald-950">{{ humanExplanation }}</span>
      </div>
      <code class="rounded border border-emerald-300 bg-white px-2 py-0.5 font-mono text-xs font-bold text-emerald-800 shadow-2xs">
        {{ rawCron }}
      </code>
    </div>

    <!-- Tab 1: Presets Grid -->
    <div v-if="mode === 'presets'" class="grid grid-cols-1 gap-2 sm:grid-cols-2">
      <button
        v-for="p in presets"
        :key="p.cron"
        type="button"
        @click="selectPreset(p.cron)"
        :disabled="disabled"
        class="flex items-start gap-3 rounded-xl border p-3 text-left transition cursor-pointer"
        :class="
          isPresetActive(p.cron)
            ? 'border-emerald-500 bg-white ring-2 ring-emerald-500/20 shadow-2xs'
            : 'border-slate-200 bg-white hover:border-slate-300 hover:bg-slate-50/50'
        "
      >
        <div
          class="mt-0.5 flex h-7 w-7 shrink-0 items-center justify-center rounded-lg"
          :class="isPresetActive(p.cron) ? 'bg-emerald-100 text-emerald-700' : 'bg-slate-100 text-slate-500'"
        >
          <i :class="p.icon" class="text-xs"></i>
        </div>
        <div class="min-w-0 flex-1">
          <div class="flex items-center justify-between">
            <span class="text-xs font-bold text-slate-800">{{ p.label }}</span>
            <span
              class="rounded px-1.5 py-0.2 text-[9px] font-semibold uppercase"
              :class="isPresetActive(p.cron) ? 'bg-emerald-100 text-emerald-800' : 'bg-slate-100 text-slate-500'"
            >
              {{ p.badge }}
            </span>
          </div>
          <div class="mt-0.5 text-[11px] text-slate-500 truncate">{{ p.desc }}</div>
        </div>
      </button>
    </div>

    <!-- Tab 2: Custom Visual Builder -->
    <div v-else-if="mode === 'custom'" class="space-y-4 rounded-xl border border-slate-200 bg-white p-4">
      <!-- Frequency Type -->
      <div>
        <label class="block text-[11px] font-semibold text-slate-600 mb-1.5">Trigger Frequency</label>
        <div class="grid grid-cols-3 gap-2">
          <button
            type="button"
            @click="customFreq = 'hourly'; updateCustomCron()"
            class="rounded-xl border py-2 text-xs font-semibold transition cursor-pointer"
            :class="customFreq === 'hourly' ? 'border-emerald-500 bg-emerald-50 text-emerald-800' : 'border-slate-200 text-slate-600 hover:bg-slate-50'"
          >
            Hourly
          </button>
          <button
            type="button"
            @click="customFreq = 'daily'; updateCustomCron()"
            class="rounded-xl border py-2 text-xs font-semibold transition cursor-pointer"
            :class="customFreq === 'daily' ? 'border-emerald-500 bg-emerald-50 text-emerald-800' : 'border-slate-200 text-slate-600 hover:bg-slate-50'"
          >
            Daily
          </button>
          <button
            type="button"
            @click="customFreq = 'weekly'; updateCustomCron()"
            class="rounded-xl border py-2 text-xs font-semibold transition cursor-pointer"
            :class="customFreq === 'weekly' ? 'border-emerald-500 bg-emerald-50 text-emerald-800' : 'border-slate-200 text-slate-600 hover:bg-slate-50'"
          >
            Weekly
          </button>
        </div>
      </div>

      <!-- Time Selector -->
      <div v-if="customFreq !== 'hourly'" class="flex items-center gap-3">
        <div>
          <label class="block text-[11px] font-semibold text-slate-600 mb-1">Time (UTC)</label>
          <div class="flex items-center gap-1.5 font-mono">
            <select
              v-model="customHour"
              @change="updateCustomCron"
              class="rounded-lg border border-slate-300 bg-slate-50 px-2.5 py-1.5 text-xs font-semibold text-slate-800"
            >
              <option v-for="h in 24" :key="h - 1" :value="String(h - 1).padStart(2, '0')">
                {{ String(h - 1).padStart(2, '0') }}:00
              </option>
            </select>
            <span class="text-slate-400 font-bold">:</span>
            <select
              v-model="customMinute"
              @change="updateCustomCron"
              class="rounded-lg border border-slate-300 bg-slate-50 px-2.5 py-1.5 text-xs font-semibold text-slate-800"
            >
              <option value="00">00</option>
              <option value="15">15</option>
              <option value="30">30</option>
              <option value="45">45</option>
            </select>
          </div>
        </div>
        <div class="text-[11px] text-slate-500 pt-5">
          UTC time is used to maintain precision across server timezones.
        </div>
      </div>

      <!-- Days of Week Selector (for Weekly) -->
      <div v-if="customFreq === 'weekly'">
        <label class="block text-[11px] font-semibold text-slate-600 mb-1.5">Active Days</label>
        <div class="flex flex-wrap gap-1.5">
          <button
            v-for="d in daysOfWeek"
            :key="d.id"
            type="button"
            @click="toggleDay(d.id)"
            class="h-8 w-10 rounded-lg border text-xs font-semibold transition cursor-pointer"
            :class="customDays.includes(d.id) ? 'border-emerald-500 bg-emerald-600 text-white shadow-2xs' : 'border-slate-200 bg-slate-50 text-slate-600 hover:bg-slate-100'"
          >
            {{ d.label }}
          </button>
        </div>
      </div>
    </div>

    <!-- Tab 3: Raw Advanced Cron -->
    <div v-else class="space-y-2 rounded-xl border border-slate-200 bg-white p-4">
      <label class="block text-[11px] font-semibold text-slate-700">
        Linux 5-Field Standard Cron Expression
      </label>
      <input
        type="text"
        :value="rawCron"
        @input="onRawInput(($event.target as HTMLInputElement).value)"
        placeholder="0 2 * * *"
        class="w-full rounded-xl border border-slate-300 px-3.5 py-2 font-mono text-xs font-bold text-slate-900 focus:border-indigo-500 focus:outline-hidden"
      />
      <div class="flex items-center justify-between text-[11px] text-slate-500">
        <span>Format: <code>minute hour day-of-month month day-of-week</code></span>
        <span>Hangfire compatible</span>
      </div>
    </div>
  </div>
</template>
