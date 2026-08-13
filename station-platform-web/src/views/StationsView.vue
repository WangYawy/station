<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api, ApiError } from '../api/client'
import type { DeptItem, PagedResult, StationItem } from '../api/types'
import { LICENSE_STATUS, fmtTime } from '../utils/format'
import { downloadCsv } from '../utils/export'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const loading = ref(false)
const items = ref<StationItem[]>([])
const total = ref(0)
const depts = ref<DeptItem[]>([])
const deptSelections = ref<Record<number, number | undefined>>({})

function deptName(id: number | null): string {
  if (!id) return ''
  return depts.value.find((d) => d.id === id)?.name || String(id)
}

async function loadDepts() {
  try {
    depts.value = await api<DeptItem[]>('/depts')
  } catch {
    depts.value = []
  }
}

async function loadStations() {
  loading.value = true
  try {
    const data = await api<PagedResult<StationItem>>('/stations?page=1&size=100')
    items.value = data.items
    total.value = data.totalCount
    const sel: Record<number, number | undefined> = {}
    data.items.forEach((s) => {
      sel[s.stationId] = s.deptId ?? undefined
    })
    deptSelections.value = sel
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '查询失败')
  } finally {
    loading.value = false
  }
}

async function assignDept(row: StationItem) {
  try {
    const deptId = deptSelections.value[row.stationId] ?? null
    await api<boolean>(`/stations/${row.stationId}/dept`, {
      method: 'PUT',
      body: JSON.stringify({ deptId })
    })
    ElMessage.success('归属已更新')
    loadStations()
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '调整失败')
  }
}

async function doExport() {
  try {
    await downloadCsv('/exports/stations')
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '导出失败')
  }
}

onMounted(() => {
  if (auth.hasPermission('dept:view') || auth.hasPermission('station:manage')) {
    loadDepts()
  }
  loadStations()
})
</script>

<template>
  <div>
    <el-form inline>
      <el-form-item>
        <el-button type="primary" @click="loadStations()">刷新</el-button>
      </el-form-item>
      <el-form-item>
        <el-button @click="doExport()">导出</el-button>
      </el-form-item>
      <el-form-item>
        <span style="color: #64748b">共 {{ total }} 个采集站</span>
      </el-form-item>
    </el-form>

    <el-table v-loading="loading" :data="items" border stripe>
      <el-table-column prop="stationId" label="站ID" width="80" />
      <el-table-column prop="stationCode" label="站编号" width="130" />
      <el-table-column prop="osVersion" label="系统" width="100" />
      <el-table-column prop="cpuArch" label="架构" width="90" />
      <el-table-column prop="softwareVersion" label="版本" width="90" />
      <el-table-column label="授权状态" width="90">
        <template #default="{ row }">
          <el-tag :type="row.licenseStatus === 2 ? 'success' : row.licenseStatus === 3 ? 'danger' : 'warning'" size="small">
            {{ LICENSE_STATUS[row.licenseStatus] }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="到期时间" width="150">
        <template #default="{ row }">{{ fmtTime(row.licenseExpiresAt) }}</template>
      </el-table-column>
      <el-table-column prop="licenseDaysLeft" label="剩余天数" width="90" />
      <el-table-column label="部门" width="180">
        <template #default="{ row }">
          <el-select v-if="auth.hasPermission('station:manage')" v-model="deptSelections[row.stationId]" placeholder="选择部门" clearable size="small">
            <el-option v-for="d in depts" :key="d.id" :label="d.name" :value="d.id" />
          </el-select>
          <span v-else>{{ deptName(row.deptId) }}</span>
        </template>
      </el-table-column>
      <el-table-column v-if="auth.hasPermission('station:manage')" label="操作" width="90" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" @click="assignDept(row as StationItem)">保存</el-button>
        </template>
      </el-table-column>
    </el-table>
  </div>
</template>
