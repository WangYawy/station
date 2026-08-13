<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { api, ApiError } from '../api/client'
import type { AlertItem, PagedResult } from '../api/types'
import { ALERT_LEVELS, ALERT_STATUS, ALERT_TYPES, fmtTime, levelTagType } from '../utils/format'
import { downloadCsv } from '../utils/export'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const loading = ref(false)
const items = ref<AlertItem[]>([])
const total = ref(0)
const page = ref(1)
const size = 50
const query = reactive({ stationId: '', level: '' })

async function loadAlerts() {
  loading.value = true
  try {
    const params = new URLSearchParams({ page: String(page.value), size: String(size) })
    if (query.stationId) params.set('stationId', query.stationId)
    if (query.level !== '') params.set('level', query.level)
    const data = await api<PagedResult<AlertItem>>(`/alerts?${params.toString()}`)
    items.value = data.items
    total.value = data.totalCount
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '查询失败')
  } finally {
    loading.value = false
  }
}

async function setStatus(row: AlertItem, status: number) {
  try {
    await api<boolean>(`/alerts/${row.id}/status`, {
      method: 'POST',
      body: JSON.stringify({ status })
    })
    ElMessage.success('已更新')
    loadAlerts()
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '操作失败')
  }
}

function onPageChange(p: number) {
  page.value = p
  loadAlerts()
}

async function doExport() {
  const params = new URLSearchParams()
  if (query.stationId) params.set('stationId', query.stationId)
  if (query.level !== '') params.set('level', query.level)
  try {
    await downloadCsv(`/exports/alerts?${params.toString()}`)
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '导出失败')
  }
}

onMounted(loadAlerts)
</script>

<template>
  <div>
    <el-form inline>
      <el-form-item>
        <el-input v-model="query.stationId" placeholder="采集站ID" clearable style="width: 140px" @keyup.enter="loadAlerts()" />
      </el-form-item>
      <el-form-item>
        <el-select v-model="query.level" placeholder="全部级别" clearable style="width: 120px">
          <el-option v-for="(l, i) in ALERT_LEVELS" :key="i" :label="l" :value="String(i)" />
        </el-select>
      </el-form-item>
      <el-form-item>
        <el-button type="primary" @click="page = 1; loadAlerts()">查询</el-button>
      </el-form-item>
      <el-form-item>
        <el-button @click="doExport()">导出</el-button>
      </el-form-item>
    </el-form>

    <el-table v-loading="loading" :data="items" border stripe>
      <el-table-column prop="id" label="ID" width="70" />
      <el-table-column prop="stationId" label="站" width="70" />
      <el-table-column label="级别" width="70">
        <template #default="{ row }">
          <el-tag :type="levelTagType(row.level)" size="small">{{ ALERT_LEVELS[row.level] }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="类型" width="110">
        <template #default="{ row }">{{ ALERT_TYPES[row.type] }}</template>
      </el-table-column>
      <el-table-column prop="source" label="来源" width="110" />
      <el-table-column prop="message" label="内容" min-width="220" show-overflow-tooltip />
      <el-table-column label="状态" width="90">
        <template #default="{ row }">{{ ALERT_STATUS[row.status] }}</template>
      </el-table-column>
      <el-table-column label="时间" width="160">
        <template #default="{ row }">{{ fmtTime(row.occurredAt) }}</template>
      </el-table-column>
      <el-table-column v-if="auth.hasPermission('alert:handle')" label="操作" width="150" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" :disabled="row.status === 1" @click="setStatus(row as AlertItem, 1)">确认</el-button>
          <el-button link type="warning" :disabled="row.status === 2" @click="setStatus(row as AlertItem, 2)">处理</el-button>
          <el-button link type="info" :disabled="row.status === 3" @click="setStatus(row as AlertItem, 3)">关闭</el-button>
        </template>
      </el-table-column>
    </el-table>

    <div class="pager">
      <el-pagination background layout="total, prev, pager, next" :total="total" :page-size="size" :current-page="page" @current-change="onPageChange" />
    </div>
  </div>
</template>

<style scoped>
.pager {
  margin-top: 12px;
  display: flex;
  justify-content: flex-end;
}
</style>
