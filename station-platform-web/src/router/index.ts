import { createRouter, createWebHashHistory } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import AppLayout from '../App.vue'
import LoginView from '../views/LoginView.vue'
import StatsView from '../views/StatsView.vue'
import FilesView from '../views/FilesView.vue'
import AlertsView from '../views/AlertsView.vue'
import StationsView from '../views/StationsView.vue'
import CommandsView from '../views/CommandsView.vue'

const router = createRouter({
  history: createWebHashHistory(),
  routes: [
    { path: '/login', component: LoginView },
    {
      path: '/',
      component: AppLayout,
      redirect: '/stats',
      children: [
        { path: 'stats', component: StatsView, meta: { title: '总览驾驶舱' } },
        { path: 'files', component: FilesView, meta: { title: '文件检索' } },
        { path: 'alerts', component: AlertsView, meta: { title: '报警中心' } },
        { path: 'stations', component: StationsView, meta: { title: '采集站管理' } },
        { path: 'commands', component: CommandsView, meta: { title: '远程指令' } }
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
