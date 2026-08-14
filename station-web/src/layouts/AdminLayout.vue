<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { Bell, FolderOpened, List, Monitor, Setting, User } from '@element-plus/icons-vue'
import { useAuthStore } from '../stores/auth'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

const activeMenu = computed(() => route.path)

const menus = [
  { path: '/files', title: '文件查询', icon: FolderOpened, perm: 'file:view' },
  { path: '/alerts', title: '报警中心', icon: Bell, perm: 'alert:view' },
  { path: '/depts', title: '部门管理', icon: Setting, perm: 'dept:view' },
  { path: '/users', title: '用户管理', icon: User, perm: 'user:view' },
  { path: '/roles', title: '角色管理', icon: Setting, perm: 'role:view' },
  { path: '/recorders', title: '记录仪管理', icon: Monitor, perm: 'recorder:view' },
  { path: '/audit', title: '审计日志', icon: List, perm: 'audit:view' },
  { path: '/settings', title: '系统设置', icon: Setting, perm: 'setting:view' }
]

const visibleMenus = computed(() => menus.filter((m) => !m.perm || auth.hasPermission(m.perm)))

async function onLogout() {
  try {
    await ElMessageBox.confirm('确定退出登录？', '提示', { type: 'warning' })
  } catch {
    return
  }
  await auth.logout()
  router.push('/login')
}
</script>

<template>
  <el-container class="layout">
    <el-aside width="200px" class="aside">
      <div class="logo">采集站管理系统</div>
      <el-menu :default-active="activeMenu" router background-color="#0a2f6c" text-color="#cfd8e6" active-text-color="#ffffff">
        <el-menu-item v-for="m in visibleMenus" :key="m.path" :index="m.path">
          <el-icon><component :is="m.icon" /></el-icon>
          <span>{{ m.title }}</span>
        </el-menu-item>
      </el-menu>
    </el-aside>
    <el-container>
      <el-header class="header">
        <span class="page-title">{{ route.meta.title }}</span>
        <div class="user-box">
          <el-icon><User /></el-icon>
          <span>{{ auth.userName }}（{{ auth.roleText }}）</span>
          <el-button link type="primary" @click="onLogout">退出</el-button>
        </div>
      </el-header>
      <el-main><router-view /></el-main>
    </el-container>
  </el-container>
</template>

<style scoped>
.layout { height: 100vh; }
.aside { background: #0a2f6c; }
.logo { color: #fff; font-size: 16px; font-weight: 600; padding: 18px 16px; }
.aside :deep(.el-menu) { border-right: none; }
.aside :deep(.el-menu-item.is-active) { background: #12408a; }
.header { display: flex; align-items: center; justify-content: space-between; border-bottom: 1px solid #e2e8f0; background: #fff; }
.page-title { font-size: 16px; font-weight: 600; color: #1e293b; }
.user-box { display: flex; align-items: center; gap: 6px; color: #475569; font-size: 13px; }
</style>
