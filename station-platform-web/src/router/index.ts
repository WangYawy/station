import { createRouter, createWebHashHistory } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import AdminLayout from '../layouts/AdminLayout.vue'

const router = createRouter({
  history: createWebHashHistory(),
  routes: [
    { path: '/login', component: () => import('../views/LoginView.vue') },
    {
      path: '/',
      component: AdminLayout,
      redirect: '/stats',
      children: [
        { path: 'stats', component: () => import('../views/StatsView.vue'), meta: { title: '总览驾驶舱' } },
        { path: 'files', component: () => import('../views/FilesView.vue'), meta: { title: '文件检索' } },
        { path: 'alerts', component: () => import('../views/AlertsView.vue'), meta: { title: '报警中心' } },
        { path: 'stations', component: () => import('../views/StationsView.vue'), meta: { title: '采集站管理' } },
        { path: 'commands', component: () => import('../views/CommandsView.vue'), meta: { title: '远程指令' } },
        { path: 'system', component: () => import('../views/system/SystemView.vue'), meta: { title: '系统管理' } }
      ]
    }
  ]
})

router.beforeEach(async (to) => {
  if (to.path === '/login') return true
  const auth = useAuthStore()
  if (!auth.loggedIn) {
    await auth.restore()
  }
  return auth.loggedIn ? true : { path: '/login' }
})

export default router
