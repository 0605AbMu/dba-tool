<script setup lang="ts">
import { onMounted, ref, computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useDbaStore } from '@/stores/dbaStore'
import { api } from '@/services/api'
import LiveStreamDrawer from '@/components/LiveStreamDrawer.vue'

const store = useDbaStore()
const route = useRoute()
const router = useRouter()

const profilesExpanded = ref(true)

watch(
  () => route.path,
  (newPath) => {
    if (newPath.startsWith('/profiles')) {
      profilesExpanded.value = true
    }
  },
  { immediate: true }
)

const totalProfilesCount = computed(() => {
  return (
    store.connections.length +
    store.storageProfiles.length +
    store.encryptionProfiles.length +
    (Array.isArray(store.notifications) ? store.notifications.length : 0)
  )
})

const profileSubItems = [
  {
    path: '/profiles/connections',
    tab: 'connections',
    label: 'Baza Ulanishlari',
    icon: 'pi pi-database',
    badgeKey: 'connections',
  },
  {
    path: '/profiles/storage',
    tab: 'storage',
    label: 'Saqlash Omborlari',
    icon: 'pi pi-cloud-upload',
    badgeKey: 'storage',
  },
  {
    path: '/profiles/encryption',
    tab: 'encryption',
    label: 'Shifrlash Profillari',
    icon: 'pi pi-lock',
    badgeKey: 'encryption',
  },
  {
    path: '/profiles/notifications',
    tab: 'notifications',
    label: 'Xabarnoma Kanallari',
    icon: 'pi pi-bell',
    badgeKey: 'notifications',
  },
]

function getBadgeCount(key: string): number {
  switch (key) {
    case 'connections':
      return store.connections.length
    case 'storage':
      return store.storageProfiles.length
    case 'encryption':
      return store.encryptionProfiles.length
    case 'notifications':
      return Array.isArray(store.notifications) ? store.notifications.length : 0
    default:
      return 0
  }
}

function isProfilesActive(): boolean {
  return route.path.startsWith('/profiles')
}

function isSubActive(tab: string): boolean {
  if (!route.path.startsWith('/profiles')) return false
  const currentTab = (route.params.tab as string) || (route.query.tab as string) || 'connections'
  return currentTab === tab
}

function onProfilesHeaderClick() {
  if (!route.path.startsWith('/profiles')) {
    profilesExpanded.value = true
    router.push('/profiles/connections')
  } else {
    profilesExpanded.value = !profilesExpanded.value
  }
}

const pageTitle = computed(() => {
  if (route.path.startsWith('/profiles')) {
    const tab = (route.params.tab as string) || (route.query.tab as string) || 'connections'
    if (tab === 'storage') return 'Saqlash Omborlari'
    if (tab === 'encryption') return 'Shifrlash Profillari'
    if (tab === 'notifications') return 'Xabarnoma Kanallari'
    return 'Ma\'lumotlar Bazasi Ulanishlari'
  }
  if (route.path.startsWith('/operations')) {
    return 'Ma\'lumotlar Bazasi Amallari & Diagnostika'
  }
  if (route.path.startsWith('/executions')) {
    return 'Bajarilgan Amallar va Jarayonlar Tarixi'
  }
  return (route.meta.title as string) || 'Ma\'lumotlar Bazasi Zaxiralash Markazi'
})

const navItems = [
  { path: '/', label: 'Boshqaruv Paneli', icon: 'pi pi-objects-column', badge: null, isSubmenu: false },
  { path: '/jobs', label: 'Zaxiralash Rejalari', icon: 'pi pi-briefcase', badge: 'jobs', isSubmenu: false },
  { path: '/backups', label: 'Zaxira Nusxalari & Tiklash', icon: 'pi pi-folder-open', badge: 'backups', isSubmenu: false },
  { path: '/operations', label: 'Baza Amallari & Diagnostika', icon: 'pi pi-wrench', badge: null, isSubmenu: false },
  { path: '/executions', label: 'Amallar Tarixi', icon: 'pi pi-history', badge: 'executions', isSubmenu: false },
  { path: '/profiles', label: 'Ulanishlar va Sozlamalar', icon: 'pi pi-database', badge: 'profiles', isSubmenu: true },
  { path: '/scheduler', label: 'Avtomatik Rejalar', icon: 'pi pi-calendar-clock', badge: null, isSubmenu: false },
]

const advancedItems = [
  { path: '/config', label: 'Konfiguratsiya Muharriri', icon: 'pi pi-file-edit', badge: null },
]

onMounted(() => {
  store.initSignalR()
  store.fetchDashboard().catch(() => {})
  store.fetchSystemInfo().catch(() => {})
  store.fetchJobs().catch(() => {})
  store.fetchBackups().catch(() => {})
  store.fetchAllProfiles().catch(() => {})
})
</script>

<template>
  <div class="flex min-h-screen bg-slate-50 text-slate-900">
    <!-- Sidebar Navigation -->
    <aside
      class="hidden w-64 shrink-0 flex-col justify-between border-r border-slate-200 bg-white shadow-2xs lg:flex"
    >
      <div>
        <!-- Brand Header -->
        <div class="flex items-center gap-3 border-b border-slate-200 px-5 py-4">
          <div
            class="flex h-10 w-10 items-center justify-center rounded-xl bg-gradient-to-br from-emerald-600 to-indigo-600 text-white shadow-md shadow-emerald-600/20"
          >
            <i class="pi pi-database text-lg"></i>
          </div>
          <div>
            <div class="flex items-center gap-1.5">
              <span class="text-sm font-bold tracking-tight text-slate-900">Zaxira Tizimi</span>
              <span
                class="rounded border border-emerald-200 bg-emerald-50 px-1.5 py-0.2 text-[10px] font-semibold text-emerald-700"
              >
                PRO
              </span>
            </div>
            <div class="text-[11px] font-medium text-slate-500">Database Protection Suite</div>
          </div>
        </div>

        <!-- Navigation Links -->
        <nav class="p-3 space-y-1">
          <template v-for="item in navItems" :key="item.path">
            <!-- Normal Nav Item -->
            <RouterLink
              v-if="!item.isSubmenu"
              :to="item.path"
              class="flex items-center justify-between rounded-xl px-3.5 py-2.5 text-xs font-semibold transition"
              :class="
                route.path === item.path
                  ? 'bg-emerald-50 text-emerald-700 border border-emerald-200 shadow-2xs'
                  : 'text-slate-600 hover:bg-slate-100 hover:text-slate-900 border border-transparent'
              "
            >
              <div class="flex items-center gap-2.5">
                <i :class="item.icon" class="text-sm"></i>
                <span>{{ item.label }}</span>
              </div>
              <span
                v-if="item.badge === 'jobs' && store.jobs.length"
                class="rounded-full bg-slate-100 px-2 py-0.2 font-mono text-[10px] text-slate-700"
              >
                {{ store.jobs.length }}
              </span>
              <span
                v-else-if="item.badge === 'backups' && store.backups.length"
                class="rounded-full bg-emerald-50 px-2 py-0.2 font-mono text-[10px] text-emerald-700"
              >
                {{ store.backups.length }}
              </span>
              <span
                v-else-if="item.badge === 'executions' && store.runningExecutionsCount > 0"
                class="rounded-full bg-amber-100 px-2 py-0.2 font-mono text-[10px] text-amber-800 font-bold animate-pulse"
              >
                {{ store.runningExecutionsCount }}
              </span>
            </RouterLink>

            <!-- Profiles Sub-menu Group -->
            <div v-else class="space-y-1">
              <div
                @click="onProfilesHeaderClick"
                class="flex items-center justify-between rounded-xl px-3.5 py-2.5 text-xs font-semibold transition cursor-pointer select-none"
                :class="
                  isProfilesActive()
                    ? 'bg-emerald-50/70 text-emerald-800 border border-emerald-200/80 shadow-2xs font-bold'
                    : 'text-slate-600 hover:bg-slate-100 hover:text-slate-900 border border-transparent'
                "
              >
                <div class="flex items-center gap-2.5">
                  <i :class="[item.icon, { 'text-emerald-600': isProfilesActive() }]" class="text-sm"></i>
                  <span>{{ item.label }}</span>
                </div>
                <div class="flex items-center gap-1.5">
                  <span
                    v-if="totalProfilesCount > 0"
                    class="rounded-full px-1.5 py-0.2 font-mono text-[10px]"
                    :class="
                      isProfilesActive()
                        ? 'bg-emerald-100 text-emerald-800 font-bold'
                        : 'bg-slate-100 text-slate-600'
                    "
                  >
                    {{ totalProfilesCount }}
                  </span>
                  <button
                    type="button"
                    @click.stop="profilesExpanded = !profilesExpanded"
                    class="p-0.5 text-slate-400 hover:text-slate-600 rounded transition"
                  >
                    <i
                      class="pi pi-chevron-down text-[10px] transition-transform duration-200"
                      :class="{ '-rotate-90': !profilesExpanded }"
                    ></i>
                  </button>
                </div>
              </div>

              <!-- Sub-menu Items -->
              <div
                v-show="profilesExpanded"
                class="ml-3.5 space-y-0.5 border-l-2 border-emerald-200/60 pl-2.5 py-0.5 transition-all"
              >
                <RouterLink
                  v-for="sub in profileSubItems"
                  :key="sub.path"
                  :to="sub.path"
                  class="flex items-center justify-between rounded-lg px-2.5 py-2 text-xs font-medium transition"
                  :class="
                    isSubActive(sub.tab)
                      ? 'bg-emerald-100/70 text-emerald-900 font-bold shadow-2xs'
                      : 'text-slate-600 hover:bg-slate-100 hover:text-slate-900'
                  "
                >
                  <div class="flex items-center gap-2">
                    <i
                      :class="[sub.icon, isSubActive(sub.tab) ? 'text-emerald-700' : 'text-slate-400']"
                      class="text-xs"
                    ></i>
                    <span class="truncate">{{ sub.label }}</span>
                  </div>
                  <span
                    class="rounded-full px-1.5 py-0.2 font-mono text-[10px]"
                    :class="
                      isSubActive(sub.tab)
                        ? 'bg-emerald-200 text-emerald-900 font-bold'
                        : 'bg-slate-100 text-slate-600'
                    "
                  >
                    {{ getBadgeCount(sub.badgeKey) }}
                  </span>
                </RouterLink>
              </div>
            </div>
          </template>
        </nav>

        <!-- Advanced Tools Section -->
        <div class="px-4 pt-2 pb-1">
          <span class="text-[10px] font-bold uppercase tracking-wider text-slate-400">
            Qo'shimcha Sozlamalar
          </span>
        </div>
        <div class="p-3 pt-0 space-y-1">
          <RouterLink
            v-for="item in advancedItems"
            :key="item.path"
            :to="item.path"
            class="flex items-center justify-between rounded-xl px-3.5 py-2 text-xs font-semibold transition"
            :class="
              route.path === item.path
                ? 'bg-indigo-50 text-indigo-700 border border-indigo-200 shadow-2xs'
                : 'text-slate-600 hover:bg-slate-100 hover:text-slate-900 border border-transparent'
            "
          >
            <div class="flex items-center gap-2.5">
              <i :class="item.icon" class="text-xs"></i>
              <span>{{ item.label }}</span>
            </div>
            <span
              v-if="item.badge"
              class="rounded border border-indigo-200 bg-indigo-50 px-1.5 py-0.2 font-mono text-[9px] font-bold text-indigo-700"
            >
              {{ item.badge }}
            </span>
          </RouterLink>
        </div>
      </div>

      <!-- Bottom Sidebar System Info -->
      <div class="p-3 space-y-2 border-t border-slate-200 text-xs">
        <div class="rounded-xl border border-slate-200 bg-slate-50 p-3 space-y-1.5">
          <div class="flex items-center justify-between text-[11px]">
            <span class="text-slate-500">Tizim holati</span>
            <span class="font-semibold text-emerald-700 flex items-center gap-1">
              <span class="h-1.5 w-1.5 rounded-full bg-emerald-500"></span> Faol &amp; Himoyalangan
            </span>
          </div>
          <div class="flex items-center justify-between text-[11px]">
            <span class="text-slate-500">Imkoniyatlar</span>
            <span class="font-semibold text-indigo-700">
              To'liq Faol
            </span>
          </div>
          <div class="flex items-center justify-between text-[11px]">
            <span class="text-slate-500">Avto-rejalashtirish</span>
            <span class="font-semibold text-slate-700">
              Yoqilgan
            </span>
          </div>
        </div>
      </div>
    </aside>

    <!-- Main Content Column -->
    <div class="flex flex-1 flex-col min-w-0">
      <!-- Topbar -->
      <header
        class="sticky top-0 z-30 flex items-center justify-between border-b border-slate-200 bg-white/90 px-6 py-3 backdrop-blur-md"
      >
        <div class="flex items-center gap-3">
          <span class="text-sm font-bold text-slate-800">
            {{ pageTitle }}
          </span>
        </div>

        <div class="flex items-center gap-3">
          <!-- Active Running Job Indicator Banner Button -->
          <button
            @click="store.liveDrawerOpen = true"
            class="inline-flex items-center gap-2 rounded-xl border px-3 py-1.5 text-xs font-semibold transition cursor-pointer"
            :class="
              store.activeStatus === 'running'
                ? 'border-amber-300 bg-amber-50 text-amber-800 animate-pulse'
                : 'border-slate-200 bg-slate-50 text-slate-700 hover:bg-slate-100 hover:border-slate-300'
            "
          >
            <i class="pi pi-sitemap text-emerald-600"></i>
            <span v-if="store.activeStatus === 'running'">
              Bajarilmoqda: <strong>{{ store.activeJobName }}</strong>
            </span>
            <span v-else>Jarayonlar Markazi</span>
            <span
              v-if="store.liveLines.length > 0"
              class="rounded border border-emerald-200 bg-emerald-50 px-1.5 py-0.2 font-mono text-[10px] text-emerald-700"
            >
              {{ store.liveLines.length }} lines
            </span>
          </button>

          <!-- Live SignalR Connection Status Badge -->
          <div
            class="flex items-center gap-2 rounded-full border px-3 py-1 text-xs font-semibold"
            :class="{
              'border-emerald-200 bg-emerald-50 text-emerald-700':
                store.signalrState === 'connected',
              'border-amber-200 bg-amber-50 text-amber-700':
                store.signalrState === 'connecting' || store.signalrState === 'reconnecting',
              'border-rose-200 bg-rose-50 text-rose-700':
                store.signalrState === 'disconnected',
            }"
          >
            <span
              class="h-2 w-2 rounded-full"
              :class="{
                'bg-emerald-500': store.signalrState === 'connected',
                'bg-amber-500 animate-ping':
                  store.signalrState === 'connecting' || store.signalrState === 'reconnecting',
                'bg-rose-500': store.signalrState === 'disconnected',
              }"
            ></span>
            <span>SignalR: {{ store.signalrState }}</span>
          </div>
        </div>
      </header>

      <!-- Active Running Job Banner -->
      <div
        v-if="store.activeStatus === 'running'"
        class="flex items-center justify-between border-b border-amber-200 bg-amber-50 px-6 py-2 text-xs text-amber-900"
      >
        <div class="flex items-center gap-2 font-mono">
          <i class="pi pi-spin pi-spinner text-amber-600"></i>
          <span>
            Active CLI Execution #{{ store.activeExecutionId }} ({{ store.activeJobName }}):
            <code>{{ store.activeCommand }}</code>
          </span>
        </div>
        <div class="flex items-center gap-2">
          <button
            @click="store.liveDrawerOpen = true"
            class="rounded-lg border border-amber-300 bg-white px-2.5 py-1 font-semibold text-amber-800 hover:bg-amber-100 cursor-pointer"
          >
            Open Live Stream
          </button>
          <button
            @click="store.cancelActiveExecution()"
            class="rounded-lg border border-rose-300 bg-rose-50 px-2.5 py-1 font-semibold text-rose-700 hover:bg-rose-100 cursor-pointer"
          >
            Cancel
          </button>
        </div>
      </div>

      <!-- Page Viewport -->
      <main class="flex-1 p-6">
        <RouterView />
      </main>
    </div>

    <!-- Slide-over Live CLI Stream Drawer -->
    <LiveStreamDrawer />

    <!-- Toast Notifications Overlay -->
    <div class="fixed bottom-5 right-5 z-50 flex flex-col gap-2.5 max-w-sm w-full pointer-events-none">
      <div
        v-for="t in store.toasts"
        :key="t.id"
        class="pointer-events-auto flex items-start justify-between gap-3 rounded-xl border p-4 shadow-lg backdrop-blur-md transition bg-white"
        :class="{
          'border-emerald-300 text-emerald-900': t.severity === 'success',
          'border-indigo-300 text-indigo-900': t.severity === 'info',
          'border-amber-300 text-amber-900': t.severity === 'warn',
          'border-rose-300 text-rose-900': t.severity === 'error',
        }"
      >
        <div class="text-xs">
          <div class="font-bold">{{ t.summary }}</div>
          <div v-if="t.detail" class="mt-0.5 text-slate-600">{{ t.detail }}</div>
        </div>
        <button
          @click="store.removeToast(t.id)"
          class="text-slate-400 hover:text-slate-700 cursor-pointer"
        >
          <i class="pi pi-times text-xs"></i>
        </button>
      </div>
    </div>
  </div>
</template>
