<script setup lang="ts">
import { onMounted, ref, computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useDbaStore, ENGINE_METADATA, formatBytes } from '@/stores/dbaStore'
import { api } from '@/services/api'
import type { ConnectionProfile, StorageProfile, EncryptionProfile, NotificationProfile } from '@/types/models'

const store = useDbaStore()
const route = useRoute()
const router = useRouter()

type TabKey = 'connections' | 'storage' | 'encryption' | 'notifications'
const validTabs: TabKey[] = ['connections', 'storage', 'encryption', 'notifications']

const activeTab = computed<TabKey>({
  get() {
    const raw = (route.params.tab || route.query.tab) as string
    if (raw && validTabs.includes(raw as TabKey)) {
      return raw as TabKey
    }
    return 'connections'
  },
  set(val: TabKey) {
    if (route.params.tab !== val) {
      router.push(`/profiles/${val}`)
    }
  },
})

const tabMetas: Record<TabKey, { heading: string; description: string }> = {
  connections: {
    heading: 'Ma\'lumotlar Bazasi Ulanishlari',
    description: 'PostgreSQL, MySQL, MariaDB, SQLite, MongoDB, Redis, ClickHouse, InfluxDB va CouchDB ulanish nuqtalari.',
  },
  storage: {
    heading: 'Saqlash Omborlari',
    description: 'Mahalliy fayl tizimi, AWS S3 / MinIO, Azure Blob va WebDAV zaxira omborlari.',
  },
  encryption: {
    heading: 'Shifrlash Profillari',
    description: 'Age va GPG kriptografik kalitlar to\'plami va xavfsiz shifrlash usullari.',
  },
  notifications: {
    heading: 'Xabarnoma Kanallari',
    description: 'Telegram, Discord, Slack, Webhook va Email orqali xatolik va muvaffaqiyat bildirishnomalari.',
  },
}

const currentTabMeta = computed(() => {
  return tabMetas[activeTab.value] ?? tabMetas.connections
})

// Connection Modal State
const showConnModal = ref(false)
const editingConnId = ref<number | null>(null)
const testingId = ref<number | null>(null)
const connForm = ref<Partial<ConnectionProfile>>({
  name: '',
  type: 'postgres',
  host: 'localhost',
  port: 5432,
  username: 'postgres',
  password: 'env://PGPASSWORD',
  authSource: '',
  influxVersion: 2,
  influxMode: 'logical',
  tlsEnabled: false,
  tlsCaFile: '',
  tlsCertFile: '',
  tlsKeyFile: '',
  tlsVerify: true,
  tlsVersion: '1.2',
  connectivityMethod: 'simple',
  timeoutSeconds: 30,
  retryIntervalSeconds: 5,
})

// Database Explorer State
const showDbExplorerModal = ref(false)
const exploringConnId = ref<number | null>(null)
const exploredConnName = ref('')
const exploredDatabases = ref<string[]>([])
const exploredMessage = ref('')
const exploredError = ref('')
const exploringLoading = ref(false)

// Storage Modal State
const showStorageModal = ref(false)
const editingStorageId = ref<number | null>(null)
const testingStorageId = ref<number | null>(null)
const storageForm = ref<Partial<StorageProfile>>({
  name: '',
  backend: 'filesystem',
  path: './data/backups',
  bucket: '',
  region: 'us-east-1',
  endpoint: '',
  keyId: '',
  keySecret: '',
  account: '',
  key: '',
  webDavUrl: '',
  webDavUser: '',
  webDavPass: '',
  fileMode: '600',
  dirMode: '700',
  ownerUser: '',
  ownerGroup: '',
  tlsEnabled: false,
  tlsCaFile: '',
  tlsVerify: true,
  isDefault: false,
})

// Encryption Modal State
const showEncModal = ref(false)
const editingEncId = ref<number | null>(null)
const encForm = ref<Partial<EncryptionProfile>>({
  name: '',
  type: 'age',
  recipients: '',
  passphrase: '',
})

// Notification Modal State
const showNotifModal = ref(false)
const editingNotifId = ref<number | null>(null)
const testingNotifId = ref<number | null>(null)
const notifForm = ref<Partial<NotificationProfile>>({
  name: '',
  channel: 'telegram',
  isEnabled: true,
  notifyOnSuccess: false,
  notifyOnFailure: true,
  botToken: '',
  chatId: '',
  webhookUrl: '',
  smtpHost: '',
  smtpPort: 587,
  smtpUsername: '',
  smtpPassword: '',
  smtpUseSsl: true,
  emailFrom: '',
  emailTo: '',
})

onMounted(async () => {
  if (!route.params.tab) {
    router.replace(`/profiles/${activeTab.value}`)
  }
  await store.fetchAllProfiles()
})

function onEngineChange() {
  const meta = ENGINE_METADATA[connForm.value.type || 'postgres']
  if (meta) {
    connForm.value.port = meta.defaultPort
    if (connForm.value.type === 'sqlite') {
      connForm.value.host = './data/database.sqlite'
    } else if (connForm.value.host?.endsWith('.db') || connForm.value.host?.endsWith('.sqlite')) {
      connForm.value.host = 'localhost'
    }
  }
}

function openCreateConn() {
  editingConnId.value = null
  connForm.value = {
    name: '',
    type: 'postgres',
    host: 'localhost',
    port: 5432,
    username: 'postgres',
    password: 'env://PGPASSWORD',
    authSource: '',
    influxVersion: 2,
    influxMode: 'logical',
    tlsEnabled: false,
    tlsCaFile: '',
    tlsCertFile: '',
    tlsKeyFile: '',
    tlsVerify: true,
    connectivityMethod: 'simple',
    timeoutSeconds: 30,
  }
  showConnModal.value = true
}

function openEditConn(c: ConnectionProfile) {
  editingConnId.value = c.id
  connForm.value = { ...c }
  showConnModal.value = true
}

async function saveConnection() {
  if (!connForm.value.name?.trim()) {
    store.notify('warn', 'Validation', 'Connection name is required.')
    return
  }
  try {
    if (editingConnId.value) {
      await api.updateConnection(editingConnId.value, connForm.value)
      store.notify('success', 'Connection Updated', `Saved ${connForm.value.name}`)
    } else {
      await api.createConnection(connForm.value)
      store.notify('success', 'Connection Created', `Created ${connForm.value.name}`)
    }
    showConnModal.value = false
    await store.fetchAllProfiles()
  } catch (e: unknown) {
    store.notify('error', 'Failed to save connection', (e as Error).message)
  }
}

async function deleteConn(c: ConnectionProfile) {
  if (!confirm(`Delete connection profile "${c.name}"?`)) return
  try {
    await api.deleteConnection(c.id)
    store.notify('success', 'Deleted', `Removed ${c.name}`)
    await store.fetchAllProfiles()
  } catch (e: unknown) {
    store.notify('error', 'Failed to delete connection', (e as Error).message)
  }
}

async function testConn(c: ConnectionProfile) {
  testingId.value = c.id
  try {
    const res = await api.testConnection(c.id)
    if (res.success) {
      store.notify(
        'success',
        `Connection OK (${res.latencyMs ?? 1}ms)`,
        res.message || `Verified connectivity to ${c.host}`
      )
    } else {
      store.notify('warn', 'Connectivity Warning', res.message)
    }
  } catch (e: unknown) {
    store.notify('error', 'Connection Test Failed', (e as Error).message)
  } finally {
    testingId.value = null
  }
}

async function exploreConnDatabases(c: ConnectionProfile) {
  exploringConnId.value = c.id
  exploredConnName.value = c.name
  exploringLoading.value = true
  showDbExplorerModal.value = true
  exploredDatabases.value = []
  exploredMessage.value = ''
  exploredError.value = ''
  try {
    const res = await api.getConnectionDatabases(c.id)
    exploredDatabases.value = res.databases || []
    if (res.databases && res.databases.length > 0) {
      exploredMessage.value = res.message || `Discovered ${res.databases.length} database(s)`
      store.notify('success', 'Databases Discovered', exploredMessage.value)
    } else {
      exploredError.value = res.message || 'No databases found or permission restricted on server.'
      store.notify('warn', 'Database Discovery', exploredError.value)
    }
  } catch (e: unknown) {
    const msg = (e as Error).message || 'Database discovery request failed'
    exploredError.value = msg
    store.notify('error', 'Database Discovery Failed', msg)
    exploredDatabases.value = []
  } finally {
    exploringLoading.value = false
  }
}

function openCreateStorage() {
  editingStorageId.value = null
  storageForm.value = {
    name: '',
    backend: 'filesystem',
    path: './data/backups',
    bucket: '',
    region: 'us-east-1',
    endpoint: '',
    keyId: '',
    keySecret: '',
    account: '',
    key: '',
    webDavUrl: '',
    webDavUser: '',
    webDavPass: '',
    fileMode: '600',
    dirMode: '700',
    isDefault: false,
  }
  showStorageModal.value = true
}

function openEditStorage(s: StorageProfile) {
  editingStorageId.value = s.id
  storageForm.value = { ...s }
  showStorageModal.value = true
}

async function saveStorage() {
  if (!storageForm.value.name?.trim()) {
    store.notify('warn', 'Validation', 'Storage profile name is required.')
    return
  }
  try {
    if (editingStorageId.value) {
      await api.updateStorageProfile(editingStorageId.value, storageForm.value)
      store.notify('success', 'Storage Updated', `Updated ${storageForm.value.name}`)
    } else {
      await api.createStorageProfile(storageForm.value)
      store.notify('success', 'Storage Created', `Created ${storageForm.value.name}`)
    }
    showStorageModal.value = false
    await store.fetchAllProfiles()
  } catch (e: unknown) {
    store.notify('error', 'Failed to save storage profile', (e as Error).message)
  }
}

async function deleteStorage(s: StorageProfile) {
  if (!confirm(`Delete storage profile "${s.name}"?`)) return
  try {
    await api.deleteStorageProfile(s.id)
    store.notify('success', 'Deleted', `Removed ${s.name}`)
    await store.fetchAllProfiles()
  } catch (e: unknown) {
    store.notify('error', 'Delete Failed', (e as Error).message)
  }
}

async function testStorageAccess(s: StorageProfile) {
  testingStorageId.value = s.id
  try {
    const res = await api.testStorageProfile(s.id)
    if (res.accessible) {
      const freeTxt = res.freeSpaceBytes ? ` (${formatBytes(res.freeSpaceBytes)} free)` : ''
      store.notify('success', 'Storage Verified', `${res.message}${freeTxt}`)
    } else {
      store.notify('warn', 'Storage Warning', res.message)
    }
  } catch (e: unknown) {
    store.notify('error', 'Storage Test Failed', (e as Error).message)
  } finally {
    testingStorageId.value = null
  }
}

function openCreateEnc() {
  editingEncId.value = null
  encForm.value = {
    name: '',
    type: 'age',
    recipients: '',
    passphrase: '',
  }
  showEncModal.value = true
}

function openEditEnc(e: EncryptionProfile) {
  editingEncId.value = e.id
  encForm.value = { ...e }
  showEncModal.value = true
}

async function saveEncryption() {
  if (!encForm.value.name?.trim()) {
    store.notify('warn', 'Validation', 'Encryption profile name is required.')
    return
  }
  try {
    if (editingEncId.value) {
      await api.updateEncryptionProfile(editingEncId.value, encForm.value)
      store.notify('success', 'Encryption Updated', `Saved ${encForm.value.name}`)
    } else {
      await api.createEncryptionProfile(encForm.value)
      store.notify('success', 'Encryption Created', `Created ${encForm.value.name}`)
    }
    showEncModal.value = false
    await store.fetchAllProfiles()
  } catch (e: unknown) {
    store.notify('error', 'Save Failed', (e as Error).message)
  }
}

async function deleteEnc(e: EncryptionProfile) {
  if (!confirm(`Delete encryption profile "${e.name}"?`)) return
  try {
    await api.deleteEncryptionProfile(e.id)
    store.notify('success', 'Deleted', `Removed ${e.name}`)
    await store.fetchAllProfiles()
  } catch (err: unknown) {
    store.notify('error', 'Delete Failed', (err as Error).message)
  }
}

// Notification Profiles Handlers
function openCreateNotif() {
  editingNotifId.value = null
  notifForm.value = {
    name: '',
    channel: 'telegram',
    isEnabled: true,
    notifyOnSuccess: false,
    notifyOnFailure: true,
    botToken: '',
    chatId: '',
    webhookUrl: '',
    smtpHost: '',
    smtpPort: 587,
    smtpUsername: '',
    smtpPassword: '',
    smtpUseSsl: true,
    emailFrom: '',
    emailTo: '',
  }
  showNotifModal.value = true
}

function openEditNotif(n: NotificationProfile) {
  editingNotifId.value = n.id
  notifForm.value = { ...n }
  showNotifModal.value = true
}

async function saveNotification() {
  if (!notifForm.value.name?.trim()) {
    store.notify('warn', 'Validation', 'Alert channel name is required.')
    return
  }
  try {
    if (editingNotifId.value) {
      await api.updateNotification(editingNotifId.value, notifForm.value)
      store.notify('success', 'Alert Channel Updated', `Saved ${notifForm.value.name}`)
    } else {
      await api.createNotification(notifForm.value)
      store.notify('success', 'Alert Channel Created', `Created ${notifForm.value.name}`)
    }
    showNotifModal.value = false
    await store.fetchNotifications()
  } catch (e: unknown) {
    store.notify('error', 'Save Failed', (e as Error).message)
  }
}

async function deleteNotification(n: NotificationProfile) {
  if (!confirm(`Delete alert channel "${n.name}"?`)) return
  try {
    await api.deleteNotification(n.id)
    store.notify('success', 'Channel Deleted', `Removed ${n.name}`)
    await store.fetchNotifications()
  } catch (e: unknown) {
    store.notify('error', 'Delete Failed', (e as Error).message)
  }
}

async function testNotification(n: NotificationProfile) {
  testingNotifId.value = n.id
  try {
    const res = await api.testNotification(n.id)
    if (res.success) {
      store.notify('success', 'Alert Dispatched', res.message)
    } else {
      store.notify('warn', 'Alert Warning', res.message)
    }
  } catch (e: unknown) {
    store.notify('error', 'Test Alert Failed', (e as Error).message)
  } finally {
    testingNotifId.value = null
  }
}
</script>

<template>
  <div class="space-y-6">
    <!-- Header with Dynamic Sub-menu Info & Actions -->
    <div class="flex flex-col justify-between gap-4 sm:flex-row sm:items-center">
      <div>
        <div class="flex items-center gap-2 text-xs font-medium text-slate-500 mb-1">
          <RouterLink to="/" class="hover:text-emerald-700">Boshqaruv</RouterLink>
          <i class="pi pi-chevron-right text-[9px] text-slate-400"></i>
          <span>Ulanishlar va Sozlamalar</span>
          <i class="pi pi-chevron-right text-[9px] text-slate-400"></i>
          <span class="font-semibold text-emerald-700">{{ currentTabMeta.heading }}</span>
        </div>
        <h1 class="text-xl font-bold text-slate-900">{{ currentTabMeta.heading }}</h1>
        <p class="text-xs text-slate-600">
          {{ currentTabMeta.description }}
        </p>
      </div>

      <div>
        <button
          v-if="activeTab === 'connections'"
          @click="openCreateConn"
          class="inline-flex items-center gap-2 rounded-xl bg-emerald-600 px-4 py-2 text-xs font-semibold text-white hover:bg-emerald-500 cursor-pointer shadow-2xs"
        >
          <i class="pi pi-plus"></i>
          Yangi Baza Ulanishi Qo'shish
        </button>
        <button
          v-else-if="activeTab === 'storage'"
          @click="openCreateStorage"
          class="inline-flex items-center gap-2 rounded-xl bg-emerald-600 px-4 py-2 text-xs font-semibold text-white hover:bg-emerald-500 cursor-pointer shadow-2xs"
        >
          <i class="pi pi-plus"></i>
          Yangi Saqlash Ombori Qo'shish
        </button>
        <button
          v-else-if="activeTab === 'encryption'"
          @click="openCreateEnc"
          class="inline-flex items-center gap-2 rounded-xl bg-emerald-600 px-4 py-2 text-xs font-semibold text-white hover:bg-emerald-500 cursor-pointer shadow-2xs"
        >
          <i class="pi pi-plus"></i>
          Yangi Shifrlash Profili Qo'shish
        </button>
        <button
          v-else
          @click="openCreateNotif"
          class="inline-flex items-center gap-2 rounded-xl bg-emerald-600 px-4 py-2 text-xs font-semibold text-white hover:bg-emerald-500 cursor-pointer shadow-2xs"
        >
          <i class="pi pi-plus"></i>
          Yangi Xabarnoma Kanali Qo'shish
        </button>
      </div>
    </div>


    <!-- TAB 1: Database Connections -->
    <div v-if="activeTab === 'connections'" class="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
      <div
        v-for="conn in store.connections"
        :key="conn.id"
        class="flex flex-col justify-between rounded-2xl border border-slate-200 bg-white p-5 shadow-2xs hover:border-slate-300 transition"
      >
        <div class="space-y-3">
          <div class="flex items-center justify-between">
            <span
              class="inline-flex items-center gap-1.5 rounded-lg border px-2.5 py-1 text-xs font-semibold"
              :class="ENGINE_METADATA[conn.type]?.badgeClass || 'bg-slate-100 text-slate-700 border-slate-200'"
            >
              <i :class="ENGINE_METADATA[conn.type]?.icon || 'pi pi-database'"></i>
              {{ ENGINE_METADATA[conn.type]?.label || conn.type }}
            </span>
            <span
              v-if="conn.tlsEnabled"
              class="rounded border border-emerald-200 bg-emerald-50 px-2 py-0.5 font-mono text-[10px] font-semibold text-emerald-700"
            >
              TLS
            </span>
          </div>

          <div>
            <h3 class="font-mono text-base font-bold text-slate-900">{{ conn.name }}</h3>
            <p class="mt-1 font-mono text-xs text-slate-500 break-all">
              {{ conn.type === 'sqlite' ? conn.host : `${conn.host}:${conn.port}` }}
            </p>
          </div>

          <div class="space-y-1 border-t border-slate-100 pt-2.5 text-xs text-slate-500">
            <div v-if="conn.username">
              User: <code class="font-semibold text-slate-800">{{ conn.username }}</code>
            </div>
            <div v-if="conn.password">
              Auth: <code class="font-semibold text-amber-700">{{ conn.password.startsWith('env://') ? conn.password : '••••••••' }}</code>
            </div>
            <div>
              Connectivity Check: <code class="font-semibold text-indigo-700">{{ conn.connectivityMethod }}</code> ({{ conn.timeoutSeconds }}s)
            </div>
          </div>
        </div>

        <div class="mt-4 flex flex-wrap items-center justify-between gap-2 border-t border-slate-100 pt-3">
          <div class="flex items-center gap-2">
            <button
              @click="testConn(conn)"
              :disabled="testingId === conn.id"
              class="inline-flex items-center gap-1.5 rounded-xl border border-emerald-200 bg-emerald-50 px-3 py-1.5 text-xs font-semibold text-emerald-700 hover:bg-emerald-100 disabled:opacity-50 cursor-pointer"
            >
              <i :class="testingId === conn.id ? 'pi pi-spin pi-spinner' : 'pi pi-bolt'"></i>
              Test
            </button>
            <button
              @click="exploreConnDatabases(conn)"
              class="inline-flex items-center gap-1.5 rounded-xl border border-slate-200 bg-slate-50 px-2.5 py-1.5 text-xs font-semibold text-slate-700 hover:bg-slate-100 cursor-pointer"
              title="Browse available database catalogs"
            >
              <i class="pi pi-search"></i>
              Browse DBs
            </button>
          </div>

          <div class="flex items-center gap-1.5">
            <button
              @click="openEditConn(conn)"
              class="rounded-lg border border-slate-200 bg-slate-50 p-1.5 text-xs text-slate-600 hover:bg-slate-100 cursor-pointer"
              title="Edit Profile"
            >
              <i class="pi pi-pencil"></i>
            </button>
            <button
              @click="deleteConn(conn)"
              class="rounded-lg border border-rose-200 bg-rose-50 p-1.5 text-xs text-rose-700 hover:bg-rose-100 cursor-pointer"
              title="Delete Profile"
            >
              <i class="pi pi-trash"></i>
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- TAB 2: Storage Backends -->
    <div v-if="activeTab === 'storage'" class="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
      <div
        v-for="sp in store.storageProfiles"
        :key="sp.id"
        class="flex flex-col justify-between rounded-2xl border border-slate-200 bg-white p-5 shadow-2xs hover:border-slate-300 transition"
      >
        <div class="space-y-3">
          <div class="flex items-center justify-between">
            <span class="rounded-lg border border-indigo-200 bg-indigo-50 px-2.5 py-1 font-mono text-xs font-semibold uppercase text-indigo-700">
              {{ sp.backend }}
            </span>
            <span
              v-if="sp.isDefault"
              class="rounded-full bg-emerald-50 border border-emerald-200 px-2 py-0.5 text-[10px] font-semibold text-emerald-700"
            >
              Default Storage
            </span>
          </div>

          <div>
            <h3 class="font-mono text-base font-bold text-slate-900">{{ sp.name }}</h3>
            <p class="mt-1 font-mono text-xs text-slate-500 break-all">
              {{ sp.path || sp.bucket }}
            </p>
          </div>

          <div class="space-y-1 border-t border-slate-100 pt-2.5 text-xs text-slate-500">
            <div v-if="sp.backend === 'filesystem'">
              Permissions: <code class="text-slate-800">file:{{ sp.fileMode }}, dir:{{ sp.dirMode }}</code>
            </div>
            <div v-if="sp.region">
              Region: <code class="text-slate-800">{{ sp.region }}</code>
            </div>
            <div v-if="sp.endpoint">
              Endpoint: <code class="text-slate-800 break-all">{{ sp.endpoint }}</code>
            </div>
          </div>
        </div>

        <div class="mt-4 flex items-center justify-between border-t border-slate-100 pt-3">
          <button
            @click="testStorageAccess(sp)"
            :disabled="testingStorageId === sp.id"
            class="inline-flex items-center gap-1.5 rounded-xl border border-indigo-200 bg-indigo-50 px-3 py-1.5 text-xs font-semibold text-indigo-700 hover:bg-indigo-100 disabled:opacity-50 cursor-pointer"
          >
            <i :class="testingStorageId === sp.id ? 'pi pi-spin pi-spinner' : 'pi pi-shield-check'"></i>
            Test Access
          </button>

          <div class="flex items-center gap-1.5">
            <button
              @click="openEditStorage(sp)"
              class="rounded-lg border border-slate-200 bg-slate-50 p-1.5 text-xs text-slate-600 hover:bg-slate-100 cursor-pointer"
            >
              <i class="pi pi-pencil"></i>
            </button>
            <button
              @click="deleteStorage(sp)"
              class="rounded-lg border border-rose-200 bg-rose-50 p-1.5 text-xs text-rose-700 hover:bg-rose-100 cursor-pointer"
            >
              <i class="pi pi-trash"></i>
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- TAB 3: Encryption Profiles -->
    <div v-if="activeTab === 'encryption'" class="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
      <div
        v-for="enc in store.encryptionProfiles"
        :key="enc.id"
        class="flex flex-col justify-between rounded-2xl border border-slate-200 bg-white p-5 shadow-2xs hover:border-slate-300 transition"
      >
        <div class="space-y-3">
          <div class="flex items-center justify-between">
            <span class="rounded-lg border border-amber-200 bg-amber-50 px-2.5 py-1 font-mono text-xs font-semibold uppercase text-amber-700">
              <i class="pi pi-lock text-[10px] mr-1"></i>
              {{ enc.type }}
            </span>
          </div>

          <div>
            <h3 class="font-mono text-base font-bold text-slate-900">{{ enc.name }}</h3>
            <p v-if="enc.recipients" class="mt-1 font-mono text-[11px] text-slate-500 truncate">
              Keys: {{ enc.recipients }}
            </p>
            <p v-else-if="enc.passphrase" class="mt-1 font-mono text-[11px] text-slate-500">
              Passphrase: {{ enc.passphrase.startsWith('env://') ? enc.passphrase : '••••••••' }}
            </p>
          </div>
        </div>

        <div class="mt-4 flex items-center justify-end gap-1.5 border-t border-slate-100 pt-3">
          <button
            @click="openEditEnc(enc)"
            class="rounded-lg border border-slate-200 bg-slate-50 p-1.5 text-xs text-slate-600 hover:bg-slate-100 cursor-pointer"
          >
            <i class="pi pi-pencil"></i>
          </button>
          <button
            @click="deleteEnc(enc)"
            class="rounded-lg border border-rose-200 bg-rose-50 p-1.5 text-xs text-rose-700 hover:bg-rose-100 cursor-pointer"
          >
            <i class="pi pi-trash"></i>
          </button>
        </div>
      </div>
    </div>

    <!-- TAB 4: Alert Channels & Notifications -->
    <div v-if="activeTab === 'notifications'" class="space-y-4">
      <div
        v-if="!Array.isArray(store.notifications) || store.notifications.length === 0"
        class="rounded-2xl border border-dashed border-slate-300 bg-white p-12 text-center"
      >
        <div class="mx-auto flex h-12 w-12 items-center justify-center rounded-2xl bg-slate-100 text-slate-400">
          <i class="pi pi-bell text-xl"></i>
        </div>
        <h3 class="mt-3 text-sm font-bold text-slate-800">No Alert Channels Configured</h3>
        <p class="mt-1 text-xs text-slate-500 max-w-sm mx-auto">
          Get real-time alerts on Telegram, Slack, Discord, or Email whenever a backup succeeds or fails.
        </p>
        <button
          @click="openCreateNotif"
          class="mt-4 inline-flex items-center gap-2 rounded-xl bg-emerald-600 px-4 py-2 text-xs font-semibold text-white hover:bg-emerald-500 cursor-pointer"
        >
          <i class="pi pi-plus"></i>
          Configure Alert Channel
        </button>
      </div>

      <div v-else class="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
        <div
          v-for="notif in (Array.isArray(store.notifications) ? store.notifications : [])"
          :key="notif.id"
          class="flex flex-col justify-between rounded-2xl border border-slate-200 bg-white p-5 shadow-2xs hover:border-slate-300 transition"
        >
          <div class="space-y-3">
            <div class="flex items-center justify-between">
              <span
                class="inline-flex items-center gap-1.5 rounded-lg border px-2.5 py-1 text-xs font-semibold uppercase font-mono"
                :class="
                  notif.channel === 'telegram'
                    ? 'border-sky-200 bg-sky-50 text-sky-700'
                    : notif.channel === 'slack'
                    ? 'border-amber-200 bg-amber-50 text-amber-700'
                    : notif.channel === 'discord'
                    ? 'border-indigo-200 bg-indigo-50 text-indigo-700'
                    : 'border-slate-200 bg-slate-50 text-slate-700'
                "
              >
                <i
                  :class="
                    notif.channel === 'telegram'
                      ? 'pi pi-send'
                      : notif.channel === 'slack' || notif.channel === 'discord'
                      ? 'pi pi-comments'
                      : notif.channel === 'email'
                      ? 'pi pi-envelope'
                      : 'pi pi-globe'
                  "
                ></i>
                {{ notif.channel }}
              </span>

              <span
                class="rounded-full px-2 py-0.5 text-[10px] font-semibold"
                :class="notif.isEnabled ? 'bg-emerald-100 text-emerald-800' : 'bg-slate-100 text-slate-500'"
              >
                {{ notif.isEnabled ? 'Active' : 'Disabled' }}
              </span>
            </div>

            <div>
              <h3 class="font-mono text-base font-bold text-slate-900">{{ notif.name }}</h3>
              <p v-if="notif.chatId" class="mt-0.5 text-xs text-slate-500">
                Chat ID: <code class="font-bold text-slate-700">{{ notif.chatId }}</code>
              </p>
              <p v-if="notif.webhookUrl" class="mt-0.5 text-xs text-slate-500 truncate">
                Webhook: <code class="text-slate-700">{{ notif.webhookUrl }}</code>
              </p>
              <p v-if="notif.emailTo" class="mt-0.5 text-xs text-slate-500">
                To: <code class="text-slate-700">{{ notif.emailTo }}</code>
              </p>
            </div>

            <div class="flex items-center gap-3 border-t border-slate-100 pt-2.5 text-xs">
              <span class="flex items-center gap-1 text-rose-700" v-if="notif.notifyOnFailure">
                <i class="pi pi-times-circle text-[11px]"></i> On Failure
              </span>
              <span class="flex items-center gap-1 text-emerald-700" v-if="notif.notifyOnSuccess">
                <i class="pi pi-check-circle text-[11px]"></i> On Success
              </span>
            </div>
          </div>

          <div class="mt-4 flex items-center justify-between border-t border-slate-100 pt-3">
            <button
              @click="testNotification(notif)"
              :disabled="testingNotifId === notif.id"
              class="inline-flex items-center gap-1.5 rounded-xl border border-sky-200 bg-sky-50 px-3 py-1.5 text-xs font-semibold text-sky-700 hover:bg-sky-100 disabled:opacity-50 cursor-pointer"
            >
              <i :class="testingNotifId === notif.id ? 'pi pi-spin pi-spinner' : 'pi pi-send'"></i>
              Send Test Alert
            </button>

            <div class="flex items-center gap-1.5">
              <button
                @click="openEditNotif(notif)"
                class="rounded-lg border border-slate-200 bg-slate-50 p-1.5 text-xs text-slate-600 hover:bg-slate-100 cursor-pointer"
              >
                <i class="pi pi-pencil"></i>
              </button>
              <button
                @click="deleteNotification(notif)"
                class="rounded-lg border border-rose-200 bg-rose-50 p-1.5 text-xs text-rose-700 hover:bg-rose-100 cursor-pointer"
              >
                <i class="pi pi-trash"></i>
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- MODAL: Database Catalog Explorer -->
    <div
      v-if="showDbExplorerModal"
      class="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4 backdrop-blur-xs"
      @click.self="showDbExplorerModal = false"
    >
      <div class="w-full max-w-lg rounded-2xl border border-slate-200 bg-white shadow-2xl">
        <div class="flex items-center justify-between border-b border-slate-200 px-6 py-4">
          <div class="flex items-center gap-2">
            <i class="pi pi-database text-emerald-600"></i>
            <h3 class="text-base font-bold text-slate-900">
              Databases on {{ exploredConnName }}
            </h3>
          </div>
          <button @click="showDbExplorerModal = false" class="text-slate-400 hover:text-slate-800 cursor-pointer">
            <i class="pi pi-times"></i>
          </button>
        </div>

        <div class="p-6 space-y-4">
          <div v-if="exploringLoading" class="py-8 text-center text-xs text-slate-500">
            <i class="pi pi-spin pi-spinner text-lg text-emerald-600 mb-2 block"></i>
            Querying database catalog...
          </div>
          <div
            v-else-if="exploredError"
            class="rounded-xl border border-rose-200 bg-rose-50 p-4 text-xs text-rose-900 space-y-1.5"
          >
            <div class="flex items-center gap-1.5 font-bold text-rose-950">
              <i class="pi pi-exclamation-circle text-rose-600"></i>
              <span>Database Discovery Failed:</span>
            </div>
            <p class="font-mono text-[11px] text-rose-800 break-words leading-relaxed">{{ exploredError }}</p>
          </div>
          <div v-else-if="exploredDatabases.length === 0" class="py-6 text-center text-xs text-slate-500">
            No database catalogs found on this server.
          </div>
          <div v-else class="space-y-2">
            <div class="flex items-center justify-between text-xs text-slate-600 font-semibold">
              <span>Discovered {{ exploredDatabases.length }} database(s):</span>
              <span v-if="exploredMessage" class="text-[11px] font-normal text-emerald-700">
                <i class="pi pi-check-circle text-emerald-600 mr-1"></i>{{ exploredMessage }}
              </span>
            </div>
            <div class="flex flex-wrap gap-2 max-h-60 overflow-y-auto p-2 rounded-xl border border-slate-100 bg-slate-50">
              <span
                v-for="db in exploredDatabases"
                :key="db"
                class="inline-flex items-center gap-1.5 rounded-lg border border-emerald-200 bg-white px-3 py-1 text-xs font-mono font-bold text-emerald-800 shadow-2xs"
              >
                <i class="pi pi-folder text-[10px] text-emerald-600"></i>
                {{ db }}
              </span>
            </div>
          </div>
        </div>

        <div class="flex justify-end border-t border-slate-200 px-6 py-3 bg-slate-50">
          <button
            @click="showDbExplorerModal = false"
            class="rounded-xl bg-slate-800 px-4 py-1.5 text-xs font-semibold text-white hover:bg-slate-700 cursor-pointer"
          >
            Close
          </button>
        </div>
      </div>
    </div>

    <!-- MODAL 1: Connection Modal -->
    <div
      v-if="showConnModal"
      class="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4 backdrop-blur-xs"
      @click.self="showConnModal = false"
    >
      <div class="w-full max-w-2xl rounded-2xl border border-slate-200 bg-white shadow-2xl">
        <div class="flex items-center justify-between border-b border-slate-200 px-6 py-4">
          <h3 class="text-base font-bold text-slate-900">
            {{ editingConnId ? 'Edit Connection Profile' : 'New Connection Profile' }}
          </h3>
          <button @click="showConnModal = false" class="text-slate-400 hover:text-slate-800 cursor-pointer">
            <i class="pi pi-times"></i>
          </button>
        </div>

        <div class="p-6 space-y-4 text-xs">
          <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div>
              <label class="mb-1 block font-semibold text-slate-700">Profile Slug Name *</label>
              <input
                v-model="connForm.name"
                type="text"
                placeholder="primary-pg"
                class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 font-mono text-slate-900"
              />
            </div>
            <div>
              <label class="mb-1 block font-semibold text-slate-700">Engine Type *</label>
              <select
                v-model="connForm.type"
                @change="onEngineChange"
                class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 text-slate-900"
              >
                <option v-for="(meta, engine) in ENGINE_METADATA" :key="engine" :value="engine">
                  {{ meta.label }}
                </option>
              </select>
            </div>
          </div>

          <div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <div class="sm:col-span-2">
              <label class="mb-1 block font-semibold text-slate-700">
                {{ connForm.type === 'sqlite' ? 'SQLite Database Path *' : 'Host / IP *' }}
              </label>
              <input
                v-model="connForm.host"
                type="text"
                :placeholder="connForm.type === 'sqlite' ? './data/database.sqlite' : 'localhost'"
                class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 font-mono text-slate-900"
              />
            </div>
            <div v-if="connForm.type !== 'sqlite'">
              <label class="mb-1 block font-semibold text-slate-700">Port</label>
              <input
                v-model.number="connForm.port"
                type="number"
                class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 font-mono text-slate-900"
              />
            </div>
          </div>

          <div v-if="connForm.type !== 'sqlite'" class="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div>
              <label class="mb-1 block font-semibold text-slate-700">Username</label>
              <input
                v-model="connForm.username"
                type="text"
                placeholder="postgres"
                class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 font-mono text-slate-900"
              />
            </div>
            <div>
              <label class="mb-1 block font-semibold text-slate-700">Password (Literal or <code>env://VAR</code>)</label>
              <input
                v-model="connForm.password"
                type="password"
                placeholder="env://PGPASSWORD"
                class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 font-mono text-slate-900"
              />
            </div>
          </div>

          <!-- TLS Toggle -->
          <div class="flex items-center gap-2 pt-1 text-slate-700">
            <input type="checkbox" v-model="connForm.tlsEnabled" id="connTls" class="rounded accent-emerald-600" />
            <label for="connTls" class="font-semibold cursor-pointer">Enable TLS / SSL Connection</label>
          </div>
        </div>

        <div class="flex justify-end gap-2.5 border-t border-slate-200 px-6 py-4">
          <button
            @click="showConnModal = false"
            class="rounded-xl border border-slate-200 bg-slate-50 px-4 py-2 text-xs font-semibold text-slate-700 cursor-pointer"
          >
            Cancel
          </button>
          <button
            @click="saveConnection"
            class="rounded-xl bg-emerald-600 px-5 py-2 text-xs font-semibold text-white hover:bg-emerald-500 cursor-pointer"
          >
            Save Connection
          </button>
        </div>
      </div>
    </div>

    <!-- MODAL 2: Storage Profile Modal -->
    <div
      v-if="showStorageModal"
      class="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4 backdrop-blur-xs"
      @click.self="showStorageModal = false"
    >
      <div class="w-full max-w-xl rounded-2xl border border-slate-200 bg-white shadow-2xl">
        <div class="flex items-center justify-between border-b border-slate-200 px-6 py-4">
          <h3 class="text-base font-bold text-slate-900">
            {{ editingStorageId ? 'Edit Storage Profile' : 'New Storage Profile' }}
          </h3>
          <button @click="showStorageModal = false" class="text-slate-400 hover:text-slate-800 cursor-pointer">
            <i class="pi pi-times"></i>
          </button>
        </div>

        <div class="p-6 space-y-4 text-xs">
          <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div>
              <label class="mb-1 block font-semibold text-slate-700">Profile Name *</label>
              <input
                v-model="storageForm.name"
                type="text"
                placeholder="local-backups"
                class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 font-mono text-slate-900"
              />
            </div>
            <div>
              <label class="mb-1 block font-semibold text-slate-700">Backend Provider *</label>
              <select
                v-model="storageForm.backend"
                class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 text-slate-900"
              >
                <option value="filesystem">Local Filesystem</option>
                <option value="s3">Amazon S3 / MinIO / Ceph</option>
                <option value="azure">Azure Blob Storage</option>
                <option value="gcs">Google Cloud Storage</option>
                <option value="webdav">WebDAV</option>
              </select>
            </div>
          </div>

          <div>
            <label class="mb-1 block font-semibold text-slate-700">Storage Path / Directory *</label>
            <input
              v-model="storageForm.path"
              type="text"
              placeholder="./data/backups"
              class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 font-mono text-slate-900"
            />
          </div>

          <div v-if="storageForm.backend === 's3'" class="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div>
              <label class="mb-1 block font-semibold text-slate-700">Bucket Name *</label>
              <input
                v-model="storageForm.bucket"
                type="text"
                placeholder="my-db-backups"
                class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 font-mono text-slate-900"
              />
            </div>
            <div>
              <label class="mb-1 block font-semibold text-slate-700">Endpoint URL (MinIO)</label>
              <input
                v-model="storageForm.endpoint"
                type="text"
                placeholder="https://s3.amazonaws.com"
                class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 font-mono text-slate-900"
              />
            </div>
          </div>

          <div class="flex items-center gap-2 pt-1 text-slate-700">
            <input type="checkbox" v-model="storageForm.isDefault" id="stDefault" class="rounded accent-emerald-600" />
            <label for="stDefault" class="font-semibold cursor-pointer">Set as System Default Storage Profile</label>
          </div>
        </div>

        <div class="flex justify-end gap-2.5 border-t border-slate-200 px-6 py-4">
          <button
            @click="showStorageModal = false"
            class="rounded-xl border border-slate-200 bg-slate-50 px-4 py-2 text-xs font-semibold text-slate-700 cursor-pointer"
          >
            Cancel
          </button>
          <button
            @click="saveStorage"
            class="rounded-xl bg-emerald-600 px-5 py-2 text-xs font-semibold text-white hover:bg-emerald-500 cursor-pointer"
          >
            Save Storage
          </button>
        </div>
      </div>
    </div>

    <!-- MODAL 3: Encryption Profile Modal -->
    <div
      v-if="showEncModal"
      class="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4 backdrop-blur-xs"
      @click.self="showEncModal = false"
    >
      <div class="w-full max-w-lg rounded-2xl border border-slate-200 bg-white shadow-2xl">
        <div class="flex items-center justify-between border-b border-slate-200 px-6 py-4">
          <h3 class="text-base font-bold text-slate-900">
            {{ editingEncId ? 'Edit Encryption Profile' : 'New Encryption Profile' }}
          </h3>
          <button @click="showEncModal = false" class="text-slate-400 hover:text-slate-800 cursor-pointer">
            <i class="pi pi-times"></i>
          </button>
        </div>

        <div class="p-6 space-y-4 text-xs">
          <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div>
              <label class="mb-1 block font-semibold text-slate-700">Profile Slug Name *</label>
              <input
                v-model="encForm.name"
                type="text"
                placeholder="age-vault-prod"
                class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 font-mono text-slate-900"
              />
            </div>
            <div>
              <label class="mb-1 block font-semibold text-slate-700">Encryption Provider *</label>
              <select
                v-model="encForm.type"
                class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 text-slate-900"
              >
                <option value="age">age (Modern X25519 / scrypt)</option>
                <option value="gpg">gpg (OpenPGP RFC 4880)</option>
                <option value="openssl">openssl (AES-256-CBC PBKDF2)</option>
              </select>
            </div>
          </div>

          <div>
            <label class="mb-1 block font-semibold text-slate-700">Recipients (Public Keys)</label>
            <input
              v-model="encForm.recipients"
              type="text"
              placeholder="age1ql3z7hjy54pw3hyww5ayyfg7zqgvc7w3j2elw8zmrj2kg5sfn9aqmcac8p"
              class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 font-mono text-slate-900"
            />
          </div>

          <div>
            <label class="mb-1 block font-semibold text-slate-700">Passphrase (or env://VAR)</label>
            <input
              v-model="encForm.passphrase"
              type="text"
              placeholder="env://BACKUP_PASSPHRASE"
              class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 font-mono text-slate-900"
            />
          </div>
        </div>

        <div class="flex justify-end gap-2.5 border-t border-slate-200 px-6 py-4">
          <button
            @click="showEncModal = false"
            class="rounded-xl border border-slate-200 bg-slate-50 px-4 py-2 text-xs font-semibold text-slate-700 cursor-pointer"
          >
            Cancel
          </button>
          <button
            @click="saveEncryption"
            class="rounded-xl bg-emerald-600 px-5 py-2 text-xs font-semibold text-white hover:bg-emerald-500 cursor-pointer"
          >
            Save Encryption
          </button>
        </div>
      </div>
    </div>

    <!-- MODAL 4: Notification Profile Modal -->
    <div
      v-if="showNotifModal"
      class="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4 backdrop-blur-xs"
      @click.self="showNotifModal = false"
    >
      <div class="w-full max-w-lg rounded-2xl border border-slate-200 bg-white shadow-2xl">
        <div class="flex items-center justify-between border-b border-slate-200 px-6 py-4">
          <h3 class="text-base font-bold text-slate-900">
            {{ editingNotifId ? 'Edit Alert Channel' : 'New Alert Channel' }}
          </h3>
          <button @click="showNotifModal = false" class="text-slate-400 hover:text-slate-800 cursor-pointer">
            <i class="pi pi-times"></i>
          </button>
        </div>

        <div class="p-6 space-y-4 text-xs">
          <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div>
              <label class="mb-1 block font-semibold text-slate-700">Channel Name *</label>
              <input
                v-model="notifForm.name"
                type="text"
                placeholder="devops-telegram-alerts"
                class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 text-slate-900"
              />
            </div>
            <div>
              <label class="mb-1 block font-semibold text-slate-700">Service Provider *</label>
              <select
                v-model="notifForm.channel"
                class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 text-slate-900"
              >
                <option value="telegram">Telegram Bot</option>
                <option value="slack">Slack Webhook</option>
                <option value="discord">Discord Webhook</option>
                <option value="webhook">Generic Webhook (POST JSON)</option>
                <option value="email">Email / SMTP</option>
              </select>
            </div>
          </div>

          <!-- Telegram Fields -->
          <div v-if="notifForm.channel === 'telegram'" class="space-y-3">
            <div>
              <label class="mb-1 block font-semibold text-slate-700">Bot Token *</label>
              <input
                v-model="notifForm.botToken"
                type="text"
                placeholder="123456789:ABCdefGhIJKlmNoPQRsTUVwxyZ"
                class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 font-mono text-slate-900"
              />
            </div>
            <div>
              <label class="mb-1 block font-semibold text-slate-700">Target Chat / Group ID *</label>
              <input
                v-model="notifForm.chatId"
                type="text"
                placeholder="-100123456789"
                class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 font-mono text-slate-900"
              />
            </div>
          </div>

          <!-- Webhook Fields -->
          <div v-if="notifForm.channel === 'slack' || notifForm.channel === 'discord' || notifForm.channel === 'webhook'">
            <label class="mb-1 block font-semibold text-slate-700">Webhook URL *</label>
            <input
              v-model="notifForm.webhookUrl"
              type="text"
              placeholder="https://hooks.slack.com/services/..."
              class="w-full rounded-xl border border-slate-300 bg-white px-3.5 py-2 font-mono text-slate-900"
            />
          </div>

          <!-- Email / SMTP Fields -->
          <div v-if="notifForm.channel === 'email'" class="space-y-3">
            <div class="grid grid-cols-2 gap-3">
              <div>
                <label class="mb-1 block font-semibold text-slate-700">SMTP Host</label>
                <input v-model="notifForm.smtpHost" type="text" placeholder="smtp.gmail.com" class="w-full rounded-xl border border-slate-300 px-3 py-2 text-slate-900" />
              </div>
              <div>
                <label class="mb-1 block font-semibold text-slate-700">SMTP Port</label>
                <input v-model.number="notifForm.smtpPort" type="number" placeholder="587" class="w-full rounded-xl border border-slate-300 px-3 py-2 text-slate-900" />
              </div>
            </div>
            <div class="grid grid-cols-2 gap-3">
              <div>
                <label class="mb-1 block font-semibold text-slate-700">Recipient Email (To)</label>
                <input v-model="notifForm.emailTo" type="email" placeholder="dba@company.com" class="w-full rounded-xl border border-slate-300 px-3 py-2 text-slate-900" />
              </div>
              <div>
                <label class="mb-1 block font-semibold text-slate-700">Sender Email (From)</label>
                <input v-model="notifForm.emailFrom" type="email" placeholder="backups@company.com" class="w-full rounded-xl border border-slate-300 px-3 py-2 text-slate-900" />
              </div>
            </div>
          </div>

          <!-- Triggers -->
          <div class="flex items-center gap-6 pt-2 border-t border-slate-200 text-slate-700">
            <label class="flex items-center gap-2 cursor-pointer">
              <input type="checkbox" v-model="notifForm.notifyOnFailure" class="rounded accent-rose-600" />
              <span class="font-semibold text-rose-800">Alert on Failure</span>
            </label>
            <label class="flex items-center gap-2 cursor-pointer">
              <input type="checkbox" v-model="notifForm.notifyOnSuccess" class="rounded accent-emerald-600" />
              <span class="font-semibold text-emerald-800">Alert on Success</span>
            </label>
          </div>
        </div>

        <div class="flex justify-end gap-2.5 border-t border-slate-200 px-6 py-4">
          <button
            @click="showNotifModal = false"
            class="rounded-xl border border-slate-200 bg-slate-50 px-4 py-2 text-xs font-semibold text-slate-700 cursor-pointer"
          >
            Cancel
          </button>
          <button
            @click="saveNotification"
            class="rounded-xl bg-emerald-600 px-5 py-2 text-xs font-semibold text-white hover:bg-emerald-500 cursor-pointer"
          >
            Save Alert Channel
          </button>
        </div>
      </div>
    </div>
  </div>
</template>
