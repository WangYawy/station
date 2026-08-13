<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api, ApiError } from '../../api/client'
import type { AuditLogItem, PagedResult } from '../../api/types'
import { fmtTime } from '../../utils/format'
import { downloadCsv } from '../../utils/export'

const loading = ref(false)
const items = ref<AuditLogItem[]>([])
const total = ref(0)
const page = ref(1)
const size = 20
const keyword = ref('')
const range = ref<[string, string] | null>(null)

async function loadLogs() {
  loading.value = true
  try {
    const params = new URLSearchParams({ page: String(page.value), size: String(size) })
    if (keyword.value) params.set('keyword', keyword.value)
    if (range.value?.[0]) params.set('from', range.value[0] + ' 00:00:00')
    if (range.value?.[1]) params.set('to', range.value[1] + ' 23:59:59')
    const data = await api<PagedResult<AuditLogItem>>(`/audit-logs?${params.toString()}`)
    items.value = data.items
    total.value = data.totalCount
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '查询失败')
  } finally {
    loading.value = false
  }
}

function onPageChange(p: number) {
  page.value = p
  loadLogs()
}

async function doExport() {
  const params = new URLSearchParams()
  if (keyword.value) params.set('keyword', keyword.value)
  if (range.value?.[0]) params.set('from', range.value[0] + ' 00:00:00')
  if (range.value?.[1]) params.set('to', range.value[1] + ' 23:59:59')
  try {
    await downloadCsv(`/exports/audit-logs?${params.toString()}`)
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
        <el-input v-model="keyword" placeholder="操作人/类型/目标/详情关键字" clearable style="width: 220px" @keyup.enter="loadLogs()" />
      </el-form-item>
      <el-form-item>
        <el-date-picker v-model="range" type="daterange" range-separator="至" start-placeholder="开始日期" end-placeholder="结束日期" value-format="YYYY-MM-DD" />
      </el-form-item>
      <el-form-item>
        <el-button type="primary" @click="page = 1; loadLogs()">查询</el-button>
      </el-form-item>
      <el-form-item>
        <el-button @click="doExport()">导出</el-button>
      </el-form-item>
    </el-form>

    <el-table v-loading="loading" :data="items" border stripe>
      <el-table-column label="时间" width="160">
        <template #default="{ row }">{{ fmtTime((row as AuditLogItem).createdAt) }}</template>
      </el-table-column>
      <el-table-column prop="operatorName" label="操作人" width="100" />
      <el-table-column prop="operatorAccount" label="账号" width="110" />
      <el-table-column prop="deptId" label="部门ID" width="80" />
      <el-table-column prop="operationType" label="类型" width="150" />
      <el-table-column prop="target" label="目标" width="140" show-overflow-tooltip />
      <el-table-column prop="detail" label="详情" min-width="200" show-overflow-tooltip />
      <el-table-column prop="sourceIp" label="来源IP" width="120" />
      <el-table-column label="结果" width="70">
        <template #default="{ row }">
          <el-tag :type="(row as AuditLogItem).result === 1 ? 'success' : 'danger'" size="small">{{ (row as AuditLogItem).result === 1 ? '成功' : '失败' }}</el-tag>
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
