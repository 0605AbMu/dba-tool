import { createRouter, createWebHistory } from 'vue-router'
import DashboardView from '@/views/DashboardView.vue'
import JobsView from '@/views/JobsView.vue'
import BackupsView from '@/views/BackupsView.vue'
import ProfilesView from '@/views/ProfilesView.vue'
import OperationsView from '@/views/OperationsView.vue'
import ExecutionsView from '@/views/ExecutionsView.vue'
import SchedulerView from '@/views/SchedulerView.vue'
import ConfigStudioView from '@/views/ConfigStudioView.vue'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      name: 'dashboard',
      component: DashboardView,
      meta: { title: 'Boshqaruv Paneli' },
    },
    {
      path: '/jobs',
      name: 'jobs',
      component: JobsView,
      meta: { title: 'Zaxiralash va Profilaktika Rejalari' },
    },
    {
      path: '/backups',
      name: 'backups',
      component: BackupsView,
      meta: { title: 'Zaxira Nusxalari & Tiklash' },
    },
    {
      path: '/operations',
      name: 'operations',
      component: OperationsView,
      meta: { title: 'Ma\'lumotlar Bazasi Amallari & Diagnostika' },
    },
    {
      path: '/executions',
      name: 'executions',
      component: ExecutionsView,
      meta: { title: 'Bajarilgan Amallar Tarixi' },
    },
    {
      path: '/profiles/:tab?',
      name: 'profiles',
      component: ProfilesView,
      meta: { title: 'Ulanishlar va Sozlamalar' },
    },
    {
      path: '/cli',
      redirect: '/operations',
    },
    {
      path: '/scheduler',
      name: 'scheduler',
      component: SchedulerView,
      meta: { title: 'Avtomatlashtirilgan Rejalar' },
    },
    {
      path: '/config',
      name: 'config',
      component: ConfigStudioView,
      meta: { title: 'Konfiguratsiya Muharriri' },
    },
  ],
})

export default router
