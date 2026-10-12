<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useDbaStore, ENGINE_METADATA } from '@/stores/dbaStore'
import { api } from '@/services/api'

const store = useDbaStore()
const yamlContent = ref('')
const configPath = ref('data/configs/db-backup.yml')
const busy = ref(false)

// License, Version Check & Telemetry State
const versionCheckResult = ref<Record<string, unknown> | null>(null)
const licenseInfo = ref<Record<string, unknown> | null>(null)
const statsInfo = ref<Record<string, unknown> | null>(null)
const licenseKeyInput = ref('')
const licensePathInput = ref('')

onMounted(async () => {
  await Promise.allSettled([
    loadYaml(),
    store.fetchSystemInfo(),
    loadLicenseStatus(),
    loadUsageStats(),
  ])
})

async function loadYaml() {
  busy.value = true
  try {
    const res = await api.getConfigYaml()
    yamlContent.value = res.yamlContent
    if (res.path) configPath.value = res.path
  } catch (e: unknown) {
    store.notify('error', 'Failed to load YAML', (e as Error).message)
  } finally {
    busy.value = false
  }
}

async function saveYaml() {
  busy.value = true
  try {
    await api.saveConfigYaml(yamlContent.value)
    store.notify('success', 'YAML Saved', 'Saved custom db-backup.yml configuration to disk.')
  } catch (e: unknown) {
    store.notify('error', 'Failed to save YAML', (e as Error).message)
  } finally {
    busy.value = false
  }
}

async function regenerateFromDb() {
  busy.value = true
  try {
    const res = await api.regenerateConfigYaml()
    yamlContent.value = res.yamlContent
    store.notify(
      'success',
      'YAML Regenerated',
      'Rebuilt db-backup.yml from SQLite profiles and jobs.'
    )
  } catch (e: unknown) {
    store.notify('error', 'Regenerate Failed', (e as Error).message)
  } finally {
    busy.value = false
  }
}

function copyYaml() {
  navigator.clipboard.writeText(yamlContent.value)
  store.notify('info', 'Copied to Clipboard', 'db-backup.yml copied.')
}

async function checkUpstream() {
  busy.value = true
  try {
    versionCheckResult.value = await api.checkUpstreamVersion()
    store.notify('info', 'Version Check Completed', 'Checked upstream release status via dbb version check.')
  } catch (e: unknown) {
    store.notify('error', 'Version Check Failed', (e as Error).message)
  } finally {
    busy.value = false
  }
}

async function loadLicenseStatus() {
  try {
    licenseInfo.value = await api.getLicenseStatus()
  } catch {
    licenseInfo.value = null
  }
}

async function installNewLicense() {
  if (!licenseKeyInput.value.trim() && !licensePathInput.value.trim()) {
    store.notify('warn', 'License Input Required', 'Provide a license key or file path.')
    return
  }
  busy.value = true
  try {
    const res = await api.installLicense({
      licenseKey: licenseKeyInput.value.trim() || undefined,
      licensePath: licensePathInput.value.trim() || undefined,
    })
    licenseInfo.value = res
    store.notify('success', 'License Installed', 'Executed dbb license install.')
    await store.fetchSystemInfo()
  } catch (e: unknown) {
    store.notify('error', 'License Install Failed', (e as Error).message)
  } finally {
    busy.value = false
  }
}

async function loadUsageStats() {
  try {
    statsInfo.value = await api.getUsageStats()
  } catch {
    statsInfo.value = null
  }
}
</script>

<template>
  <div class="space-y-6">
    <!-- Header -->
    <div class="flex flex-col justify-between gap-4 sm:flex-row sm:items-center">
      <div>
        <h1 class="text-xl font-bold text-slate-900">
          <code class="text-emerald-700">db-backup.yml</code> Config Studio &amp; Binary Reference
        </h1>
        <p class="text-xs text-slate-600">
          Inspect, edit, or regenerate the unified <code class="text-indigo-700">nfrastack/db-backup</code> YAML configuration and inspect binary capabilities, license, and usage stats.
        </p>
      </div>

      <div class="flex flex-wrap items-center gap-2.5">
        <button
          @click="copyYaml"
          class="rounded-xl border border-slate-200 bg-white px-3.5 py-2 text-xs font-semibold text-slate-700 hover:bg-slate-50 shadow-2xs cursor-pointer"
        >
          <i class="pi pi-copy mr-1.5"></i>Copy YAML
        </button>
        <button
          @click="regenerateFromDb"
          :disabled="busy"
          class="inline-flex items-center gap-1.5 rounded-xl border border-indigo-200 bg-indigo-50 px-3.5 py-2 text-xs font-semibold text-indigo-700 hover:bg-indigo-100 cursor-pointer"
        >
          <i class="pi pi-sync"></i>
          Regenerate from DB
        </button>
        <button
          @click="saveYaml"
          :disabled="busy"
          class="inline-flex items-center gap-1.5 rounded-xl bg-emerald-600 px-4 py-2 text-xs font-semibold text-white hover:bg-emerald-500 cursor-pointer"
        >
          <i class="pi pi-save"></i>
          Save db-backup.yml
        </button>
      </div>
    </div>

    <div class="grid grid-cols-1 gap-6 xl:grid-cols-12">
      <!-- YAML Monospace Editor (Left 7 cols) -->
      <div class="xl:col-span-7 flex flex-col rounded-2xl border border-slate-200 bg-white shadow-2xs overflow-hidden">
        <div class="flex items-center justify-between border-b border-slate-200 bg-slate-50 px-4 py-3 text-xs">
          <div class="flex items-center gap-2">
            <i class="pi pi-file-edit text-emerald-600"></i>
            <span class="font-mono font-bold text-slate-800">{{ configPath }}</span>
          </div>
          <span class="font-mono text-[11px] text-slate-500">
            {{ yamlContent.length }} bytes
          </span>
        </div>

        <textarea
          v-model="yamlContent"
          rows="26"
          spellcheck="false"
          class="w-full flex-1 bg-slate-50/40 p-4 font-mono text-xs leading-relaxed text-slate-800 focus:outline-none"
        ></textarea>
      </div>

      <!-- Binary Version, License, Stats & Maintain Ops Reference (Right 5 cols) -->
      <div class="xl:col-span-5 space-y-5">
        <!-- dbb version Card -->
        <div class="rounded-2xl border border-slate-200 bg-white p-5 shadow-2xs space-y-3">
          <div class="flex items-center justify-between">
            <h3 class="text-sm font-bold text-slate-900">
              Binary Telemetry (<code class="text-emerald-700">dbb version</code>)
            </h3>
            <div class="flex items-center gap-2">
              <button
                @click="checkUpstream"
                :disabled="busy"
                class="rounded-lg border border-indigo-200 bg-indigo-50 px-2.5 py-1 text-[11px] font-semibold text-indigo-700 hover:bg-indigo-100 cursor-pointer"
              >
                <i class="pi pi-cloud-download mr-1"></i>Check Upstream
              </button>
              <span
                class="rounded-full border border-emerald-200 bg-emerald-50 px-2.5 py-0.5 font-mono text-[11px] font-semibold uppercase text-emerald-700"
              >
                {{ store.systemInfo?.edition || 'community' }}
              </span>
            </div>
          </div>

          <div class="grid grid-cols-2 gap-3 text-xs">
            <div class="rounded-xl border border-slate-200 bg-slate-50 p-3">
              <div class="text-slate-500">Version</div>
              <div class="mt-0.5 font-mono font-bold text-slate-900">
                {{ store.systemInfo?.version || '5.1.0' }}
              </div>
            </div>
            <div class="rounded-xl border border-slate-200 bg-slate-50 p-3">
              <div class="text-slate-500">Platform / Runtime</div>
              <div class="mt-0.5 font-mono font-bold text-indigo-700">
                {{ store.systemInfo?.platform || 'linux/amd64' }} ({{ store.systemInfo?.goVersion || 'Go' }})
              </div>
            </div>
          </div>

          <div class="rounded-xl border border-slate-200 bg-slate-50 p-3 text-xs">
            <div class="text-slate-500">Resolved Binary Path</div>
            <div class="mt-0.5 font-mono font-semibold text-emerald-700 break-all">
              {{ store.systemInfo?.binaryPath || 'bin/dbb' }}
            </div>
          </div>

          <div
            v-if="versionCheckResult"
            class="rounded-xl border border-indigo-200 bg-indigo-50/60 p-3 font-mono text-[11px] text-indigo-900"
          >
            {{ versionCheckResult.output || JSON.stringify(versionCheckResult) }}
          </div>
        </div>

        <!-- License & Telemetry Stats Card (dbb license & dbb stats) -->
        <div class="rounded-2xl border border-slate-200 bg-white p-5 shadow-2xs space-y-3 text-xs">
          <div class="flex items-center justify-between">
            <h3 class="text-sm font-bold text-slate-900">
              License &amp; Telemetry (<code class="text-indigo-700">dbb license</code> / <code class="text-emerald-700">dbb stats</code>)
            </h3>
            <button
              @click="loadUsageStats"
              class="rounded-lg border border-slate-200 bg-slate-50 px-2 py-1 text-[11px] font-medium text-slate-700 hover:bg-slate-100 cursor-pointer"
            >
              <i class="pi pi-refresh mr-1"></i>Refresh Stats
            </button>
          </div>

          <div v-if="licenseInfo" class="rounded-xl border border-slate-200 bg-slate-50 p-3 font-mono text-[11px] text-slate-700 whitespace-pre-wrap">
            {{ licenseInfo.output || JSON.stringify(licenseInfo, null, 2) }}
          </div>

          <div class="grid grid-cols-1 gap-2 sm:grid-cols-3">
            <input
              v-model="licenseKeyInput"
              type="text"
              placeholder="NSLv1 license key..."
              class="sm:col-span-2 rounded-xl border border-slate-300 bg-white px-3 py-1.5 font-mono text-slate-900"
            />
            <button
              @click="installNewLicense"
              :disabled="busy"
              class="rounded-xl bg-indigo-600 px-3 py-1.5 font-semibold text-white hover:bg-indigo-500 cursor-pointer"
            >
              Install License
            </button>
          </div>

          <div v-if="statsInfo" class="rounded-xl border border-slate-200 bg-slate-50 p-3">
            <div class="mb-1 font-semibold text-slate-700">Usage Telemetry (<code>dbb stats</code>)</div>
            <pre class="max-h-36 overflow-auto font-mono text-[11px] text-slate-700">{{ statsInfo.output || JSON.stringify(statsInfo, null, 2) }}</pre>
          </div>
        </div>

        <!-- dbb maintain --list-ops Reference -->
        <div class="rounded-2xl border border-slate-200 bg-white p-5 shadow-2xs space-y-3">
          <h3 class="text-sm font-bold text-slate-900">
            Maintenance Matrix (<code class="text-indigo-700">dbb maintain --list-ops</code>)
          </h3>
          <pre
            v-if="store.systemInfo?.maintainOpsOutput"
            class="max-h-56 overflow-auto rounded-xl border border-slate-200 bg-slate-50 p-3.5 font-mono text-[11px] text-slate-700"
          >{{ store.systemInfo.maintainOpsOutput }}</pre>
          <div v-else class="space-y-2 text-xs">
            <div
              v-for="(meta, eng) in ENGINE_METADATA"
              :key="eng"
              class="flex items-center justify-between rounded-lg border border-slate-200 bg-slate-50 px-3 py-2"
            >
              <span class="font-semibold text-slate-800">{{ meta.label }}</span>
              <span class="font-mono text-[11px] text-indigo-700">
                check_tables, optimize, analyze, vacuum
              </span>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
