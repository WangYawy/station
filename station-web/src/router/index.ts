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
      redirect: '/files',
      children: [
        { path: 'files', component: () => import('../views/FilesView.vue'), meta: { title: '文件查询' } },
        { path: 'depts', component: () => import('../views/DeptsView.vue'), meta: { title: '部门管理' } },
        { path: 'users', component: () => import('../views/UsersView.vue'), meta: { title: '用户管理' } },
        { path: 'roles', component: () => import('../views/RolesView.vue'), meta: { title: '角色管理' } },
        { path: 'recorders', component: () => import('../views/RecordersView.vue'), meta: { title: '记录仪管理' } },
        { path: 'alerts', component: () => import('../views/AlertsView.vue'), meta: { title: '报警中心' } },
        { path: 'audit', component: () => import('../views/AuditView.vue'), meta: { title: '审计日志' } },
        { path: 'settings', component: () => import('../views/SettingsView.vue'), meta: { title: '系统设置' } }
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
