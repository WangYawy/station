<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api, ApiError } from '../api/client'
import type { AlertItem } from '../api/types'
import { ALERT_LEVELS, ALERT_STATUS, ALERT_TYPES, fmtTime } from '../utils/format'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const items = ref<AlertItem[]>([])
const loading = ref(false)

async function loadAlerts() {
  loading.value = true
  try {
    items.value = await api<AlertItem[]>('/alerts?count=200')
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '加载失败')
  } finally {
    loading.value = false
  }
}

async function setStatus(row: AlertItem, status: number) {
  try {
    await api<boolean>(`/alerts/${row.id}/status`, { method: 'POST', body: JSON.stringify({ status }) })
    ElMessage.success('已更新')
    loadAlerts()
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '操作失败')
  }
}

onMounted(loadAlerts)
</script>

<template>
  <div>
    <el-form inline>
      <el-form-item>
        <el-button type="primary" :loading="loading" @click="loadAlerts()">刷新</el-button>
      </el-form-item>
    </el-form>

    <el-table v-loading="loading" :data="items" border stripe>
      <el-table-column label="级别" width="80">
        <template #default="{ row }">
          <el-tag :type="(row as AlertItem).level === 2 ? 'danger' : (row as AlertItem).level === 1 ? 'warning' : 'info'" size="small">
            {{ ALERT_LEVELS[(row as AlertItem).level] }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="类型" width="110">
        <template #default="{ row }">{{ ALERT_TYPES[(row as AlertItem).type] }}</template>
      </el-table-column>
      <el-table-column prop="title" label="标题" min-width="160" />
      <el-table-column prop="detail" label="详情" min-width="200" show-overflow-tooltip />
      <el-table-column prop="source" label="来源" width="120" />
      <el-table-column label="状态" width="90">
        <template #default="{ row }">{{ ALERT_STATUS[(row as AlertItem).status] }}</template>
      </el-table-column>
      <el-table-column label="时间" width="150">
        <template #default="{ row }">{{ fmtTime((row as AlertItem).createdAt) }}</template>
      </el-table-column>
      <el-table-column v-if="auth.hasPermission('alert:handle')" label="操作" width="160" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" @click="setStatus(row as AlertItem, 1)">确认</el-button>
          <el-button link type="warning" @click="setStatus(row as AlertItem, 2)">处理</el-button>
          <el-button link type="info" @click="setStatus(row as AlertItem, 3)">关闭</el-button>
        </template>
      </el-table-column>
    </el-table>
  </div>
</template>
