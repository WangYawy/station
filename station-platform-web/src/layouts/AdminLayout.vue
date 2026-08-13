<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { Bell, Cpu, FolderOpened, Monitor, Odometer, Promotion, Setting, User } from '@element-plus/icons-vue'
import { useAuthStore } from '../stores/auth'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

const activeMenu = computed(() => route.path)

const menus = [
  { path: '/stats', title: '总览驾驶舱', icon: Odometer },
  { path: '/files', title: '文件检索', icon: FolderOpened },
  { path: '/alerts', title: '报警中心', icon: Bell },
  { path: '/stations', title: '采集站管理', icon: Monitor },
  { path: '/recorders', title: '记录仪管理', icon: Cpu, perm: 'recorder:view' },
  { path: '/commands', title: '远程指令', icon: Promotion },
  { path: '/system', title: '系统管理', icon: Setting }
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
    <el-aside width="212px" class="aside">
      <div class="logo">监控管理平台</div>
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
.layout {
  height: 100vh;
}
.aside {
  background: #0a2f6c;
}
.logo {
  color: #fff;
  font-size: 17px;
  font-weight: 600;
  padding: 18px 16px;
  letter-spacing: 1px;
}
.aside :deep(.el-menu) {
  border-right: none;
}
.aside :deep(.el-menu-item.is-active) {
  background: #12408a;
}
.header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-bottom: 1px solid #e2e8f0;
  background: #fff;
}
.page-title {
  font-size: 16px;
  font-weight: 600;
  color: #1e293b;
}
.user-box {
  display: flex;
  align-items: center;
  gap: 6px;
  color: #475569;
  font-size: 13px;
}
</style>
