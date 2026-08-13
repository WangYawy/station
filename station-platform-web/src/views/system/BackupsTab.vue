<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api, ApiError } from '../../api/client'
import type { BackupFileItem } from '../../api/types'
import { fmtSize, fmtTime } from '../../utils/format'

const items = ref<BackupFileItem[]>([])
const loading = ref(false)
const creating = ref(false)

async function loadBackups() {
  loading.value = true
  try {
    items.value = await api<BackupFileItem[]>('/backups')
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '加载失败')
  } finally {
    loading.value = false
  }
}

async function createBackup() {
  creating.value = true
  try {
    const result = await api<{ file: string; retention: number }>('/backups', { method: 'POST' })
    ElMessage.success(`备份完成：${result.file}`)
    loadBackups()
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '备份失败')
  } finally {
    creating.value = false
  }
}

onMounted(loadBackups)
</script>

<template>
  <div>
    <el-form inline>
      <el-form-item>
        <el-button type="primary" :loading="loading" @click="loadBackups()">刷新</el-button>
      </el-form-item>
      <el-form-item>
        <el-button type="success" :loading="creating" @click="createBackup()">手动备份</el-button>
      </el-form-item>
      <el-form-item>
        <span style="color: #64748b">每日 03:00 自动备份；SQLite 为一致性快照，MySQL/PostgreSQL/Kingbase 为逻辑 SQL（幂等还原）</span>
      </el-form-item>
    </el-form>

    <el-table v-loading="loading" :data="items" border stripe>
      <el-table-column prop="fileName" label="文件名" min-width="260" />
      <el-table-column label="大小" width="110">
        <template #default="{ row }">{{ fmtSize((row as BackupFileItem).size) }}</template>
      </el-table-column>
      <el-table-column label="时间" width="160">
        <template #default="{ row }">{{ fmtTime((row as BackupFileItem).createdAt) }}</template>
      </el-table-column>
    </el-table>
  </div>
</template>
