<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api, ApiError } from '../api/client'
import type { AuditLogItem, PagedResult } from '../api/types'
import { fmtTime } from '../utils/format'

const items = ref<AuditLogItem[]>([])
const total = ref(0)
const page = ref(1)
const size = 20
const keyword = ref('')
const loading = ref(false)

async function loadLogs() {
  loading.value = true
  try {
    const params = new URLSearchParams({ page: String(page.value), size: String(size) })
    if (keyword.value) params.set('keyword', keyword.value)
    const data = await api<PagedResult<AuditLogItem>>(`/audit-logs?${params.toString()}`)
    items.value = data.items
    total.value = data.totalCount
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '加载失败')
  } finally {
    loading.value = false
  }
}

async function doExport(format: string) {
  try {
    const params = new URLSearchParams()
    if (keyword.value) params.set('keyword', keyword.value)
    params.set('format', format)
    const resp = await fetch(`/api/v1/audit-logs/export?${params.toString()}`)
    if (!resp.ok) throw new ApiError(resp.status, '导出失败')
    const blob = await resp.blob()
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `审计日志.${format}`
    a.click()
    URL.revokeObjectURL(url)
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '导出失败')
  }
}

onMounted(loadLogs)
</script>

<template>
  <div>
    <el-form inline>
      <el-form-item>
        <el-input v-model="keyword" placeholder="操作人/类型/目标/详情" clearable style="width: 220px" @keyup.enter="loadLogs()" />
      </el-form-item>
      <el-form-item>
        <el-button type="primary" @click="page = 1; loadLogs()">查询</el-button>
      </el-form-item>
      <el-form-item>
        <el-dropdown @command="doExport">
          <el-button>导出</el-button>
          <template #dropdown>
            <el-dropdown-menu>
              <el-dropdown-item command="csv">CSV</el-dropdown-item>
              <el-dropdown-item command="xlsx">Excel(xlsx)</el-dropdown-item>
              <el-dropdown-item command="pdf">PDF</el-dropdown-item>
            </el-dropdown-menu>
          </template>
        </el-dropdown>
      </el-form-item>
    </el-form>

    <el-table v-loading="loading" :data="items" border stripe>
      <el-table-column label="时间" width="150">
        <template #default="{ row }">{{ fmtTime((row as AuditLogItem).createdAt) }}</template>
      </el-table-column>
      <el-table-column prop="operatorName" label="操作人" width="100" />
      <el-table-column prop="operatorAccount" label="账号" width="110" />
      <el-table-column prop="operationType" label="类型" width="150" />
      <el-table-column prop="target" label="目标" width="130" show-overflow-tooltip />
      <el-table-column prop="detail" label="详情" min-width="200" show-overflow-tooltip />
      <el-table-column prop="sourceIp" label="来源IP" width="120" />
      <el-table-column label="结果" width="70">
        <template #default="{ row }">
          <el-tag :type="(row as AuditLogItem).result === 1 ? 'success' : 'danger'" size="small">{{ (row as AuditLogItem).result === 1 ? '成功' : '失败' }}</el-tag>
        </template>
      </el-table-column>
    </el-table>

    <div class="pager">
      <el-pagination background layout="total, prev, pager, next" :total="total" :page-size="size" :current-page="page" @current-change="(p) => { page = p; loadLogs() }" />
    </div>
  </div>
</template>

<style scoped>
.pager { margin-top: 12px; display: flex; justify-content: flex-end; }
</style>
