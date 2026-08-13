<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { api, ApiError, apiText } from '../api/client'
import type { DeptItem, ImportResult, PagedResult, StationItem } from '../api/types'
import { LICENSE_STATUS, fmtTime } from '../utils/format'
import { downloadCsv, downloadText } from '../utils/export'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const loading = ref(false)
const items = ref<StationItem[]>([])
const total = ref(0)
const depts = ref<DeptItem[]>([])
const deptSelections = ref<Record<number, number | undefined>>({})
const statusSelections = ref<Record<number, number>>({})
const importResult = ref<ImportResult | null>(null)
const showImportResult = computed({
  get: () => importResult.value !== null,
  set: (v: boolean) => {
    if (!v) importResult.value = null
  }
})

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
    const st: Record<number, number> = {}
    data.items.forEach((s) => {
      sel[s.stationId] = s.deptId ?? undefined
      st[s.stationId] = s.operationalStatus ?? 0
    })
    deptSelections.value = sel
    statusSelections.value = st
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

async function setStatus(row: StationItem) {
  try {
    await api<boolean>(`/stations/${row.stationId}/status`, {
      method: 'PUT',
      body: JSON.stringify({ status: statusSelections.value[row.stationId] ?? 0 })
    })
    ElMessage.success('状态已更新')
    loadStations()
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '更新失败')
  }
}

function downloadTemplate() {
  downloadText('采集站导入模板.csv', '站编号,系统,架构,版本,部门编码\nST1001,麒麟V10,x86_64,0.1.0,TEAM1\n')
}

async function handleFile(file: { raw?: File }) {
  if (!file.raw) return
  try {
    importResult.value = await apiText<ImportResult>('/imports/stations', await file.raw.text())
    ElMessage.success(`导入完成：成功 ${importResult.value.success}，失败 ${importResult.value.failed}`)
    loadStations()
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '导入失败')
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
      <el-form-item v-if="auth.hasPermission('station:manage')">
        <el-upload :auto-upload="false" :show-file-list="false" accept=".csv" :on-change="handleFile" style="display: inline-block">
          <el-button type="primary">导入</el-button>
        </el-upload>
        <el-button @click="downloadTemplate()">下载模板</el-button>
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
      <el-table-column label="运行状态" width="150">
        <template #default="{ row }">
          <el-select v-if="auth.hasPermission('station:manage')" v-model="statusSelections[(row as StationItem).stationId]" size="small" @change="setStatus(row as StationItem)">
            <el-option label="正常" :value="0" />
            <el-option label="维修" :value="1" />
            <el-option label="报废" :value="2" />
          </el-select>
          <el-tag v-else :type="(row as StationItem).operationalStatus === 1 ? 'warning' : (row as StationItem).operationalStatus === 2 ? 'danger' : 'success'" size="small">
            {{ ['正常', '维修', '报废'][(row as StationItem).operationalStatus] }}
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

    <el-dialog v-model="showImportResult" title="导入结果" width="520px">
      <p>共 {{ importResult?.total }} 行：成功 <b style="color:#16a34a">{{ importResult?.success }}</b>，失败 <b style="color:#dc2626">{{ importResult?.failed }}</b></p>
      <el-table v-if="importResult?.errors.length" :data="importResult.errors" border size="small">
        <el-table-column prop="line" label="行号" width="80" />
        <el-table-column prop="message" label="原因" min-width="280" />
      </el-table>
      <template #footer>
        <el-button type="primary" @click="importResult = null">关闭</el-button>
      </template>
    </el-dialog>
  </div>
</template>
