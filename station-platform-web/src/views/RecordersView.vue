<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { api, ApiError } from '../api/client'
import type { DeptItem, PagedResult, RecorderItem } from '../api/types'
import { fmtSize, fmtTime } from '../utils/format'
import { downloadCsv } from '../utils/export'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const loading = ref(false)
const items = ref<RecorderItem[]>([])
const total = ref(0)
const page = ref(1)
const size = 20
const depts = ref<DeptItem[]>([])
const query = reactive({ keyword: '', whitelisted: '', bound: '' })
const bindVisible = ref(false)
const bindRow = ref<RecorderItem | null>(null)
const bindForm = reactive({ userNo: '', deptId: undefined as number | undefined })
const bindResult = ref('')

async function loadDepts() {
  if (!auth.hasPermission('dept:view')) return
  try {
    depts.value = await api<DeptItem[]>('/depts')
  } catch {
    depts.value = []
  }
}

async function loadRecorders() {
  loading.value = true
  try {
    const params = new URLSearchParams({ page: String(page.value), size: String(size) })
    if (query.keyword) params.set('keyword', query.keyword)
    if (query.whitelisted !== '') params.set('whitelisted', query.whitelisted)
    if (query.bound === '1') params.set('bound', 'true')
    if (query.bound === '0') params.set('bound', 'false')
    const data = await api<PagedResult<RecorderItem>>(`/recorders?${params.toString()}`)
    items.value = data.items
    total.value = data.totalCount
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '查询失败')
  } finally {
    loading.value = false
  }
}

async function toggleWhitelist(row: RecorderItem) {
  try {
    await api<boolean>(`/recorders/${row.id}/whitelist`, {
      method: 'PUT',
      body: JSON.stringify({ isWhitelisted: row.isWhitelisted })
    })
    ElMessage.success(row.isWhitelisted ? '已加入白名单' : '已移出白名单')
  } catch (e) {
    row.isWhitelisted = !row.isWhitelisted
    ElMessage.error(e instanceof ApiError ? e.message : '操作失败')
  }
}

function openBind(row: RecorderItem) {
  bindRow.value = row
  bindForm.userNo = ''
  bindForm.deptId = undefined
  bindResult.value = ''
  bindVisible.value = true
}

async function saveBind() {
  if (!bindRow.value || !bindForm.userNo) {
    ElMessage.warning('请输入用户工号')
    return
  }
  try {
    const result = await api<{ dispatched: boolean; commandId: number | null }>(`/recorders/${bindRow.value.id}/bind`, {
      method: 'PUT',
      body: JSON.stringify({ userNo: bindForm.userNo, deptId: bindForm.deptId ?? null })
    })
    bindResult.value = result.dispatched
      ? `绑定已更新并下发写入指令（指令ID ${result.commandId}）`
      : '绑定已更新（记录仪暂无关联采集站，未下发指令）'
    ElMessage.success('绑定已更新')
    loadRecorders()
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '绑定失败')
  }
}

function onPageChange(p: number) {
  page.value = p
  loadRecorders()
}

async function doExport() {
  const params = new URLSearchParams()
  if (query.keyword) params.set('keyword', query.keyword)
  if (query.whitelisted !== '') params.set('whitelisted', query.whitelisted)
  if (query.bound === '1') params.set('bound', 'true')
  if (query.bound === '0') params.set('bound', 'false')
  try {
    await downloadCsv(`/exports/recorders?${params.toString()}`)
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '导出失败')
  }
}

onMounted(() => {
  loadDepts()
  loadRecorders()
})
</script>

<template>
  <div>
    <el-form inline>
      <el-form-item>
        <el-input v-model="query.keyword" placeholder="记录仪编号/绑定用户/部门" clearable style="width: 200px" @keyup.enter="loadRecorders()" />
      </el-form-item>
      <el-form-item>
        <el-select v-model="query.whitelisted" placeholder="白名单" clearable style="width: 110px">
          <el-option label="白名单" value="true" />
          <el-option label="非白名单" value="false" />
        </el-select>
      </el-form-item>
      <el-form-item>
        <el-select v-model="query.bound" placeholder="绑定状态" clearable style="width: 110px">
          <el-option label="已绑定" value="1" />
          <el-option label="未绑定" value="0" />
        </el-select>
      </el-form-item>
      <el-form-item>
        <el-button type="primary" @click="page = 1; loadRecorders()">查询</el-button>
      </el-form-item>
      <el-form-item>
        <el-button @click="doExport()">导出</el-button>
      </el-form-item>
    </el-form>

    <el-table v-loading="loading" :data="items" border stripe>
      <el-table-column prop="recorderSerial" label="记录仪编号" width="170" />
      <el-table-column prop="lastStationId" label="最近采集站" width="100" />
      <el-table-column prop="fileCount" label="文件数" width="80" />
      <el-table-column label="容量" width="100">
        <template #default="{ row }">{{ fmtSize((row as RecorderItem).totalSize) }}</template>
      </el-table-column>
      <el-table-column label="最近采集" width="150">
        <template #default="{ row }">{{ fmtTime((row as RecorderItem).lastFileAt) }}</template>
      </el-table-column>
      <el-table-column label="首次上报" width="150">
        <template #default="{ row }">{{ fmtTime((row as RecorderItem).firstSeenAt) }}</template>
      </el-table-column>
      <el-table-column label="绑定用户" width="120">
        <template #default="{ row }">{{ (row as RecorderItem).boundUserName || (row as RecorderItem).boundUserNo || '' }}</template>
      </el-table-column>
      <el-table-column label="绑定部门" width="110">
        <template #default="{ row }">{{ (row as RecorderItem).boundDeptName || (row as RecorderItem).boundDeptCode || '' }}</template>
      </el-table-column>
      <el-table-column label="白名单" width="100">
        <template #default="{ row }">
          <el-switch
            v-if="auth.hasPermission('recorder:manage')"
            v-model="(row as RecorderItem).isWhitelisted"
            @change="toggleWhitelist(row as RecorderItem)"
          />
          <el-tag v-else :type="(row as RecorderItem).isWhitelisted ? 'success' : 'info'" size="small">
            {{ (row as RecorderItem).isWhitelisted ? '白名单' : '非白名单' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column v-if="auth.hasPermission('recorder:manage')" label="操作" width="90" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" @click="openBind(row as RecorderItem)">绑定</el-button>
        </template>
      </el-table-column>
    </el-table>

    <div class="pager">
      <el-pagination background layout="total, prev, pager, next" :total="total" :page-size="size" :current-page="page" @current-change="onPageChange" />
    </div>

    <el-dialog v-model="bindVisible" :title="`重新绑定 ${bindRow?.recorderSerial ?? ''}`" width="440px">
      <el-form label-width="90px">
        <el-form-item label="用户工号">
          <el-input v-model="bindForm.userNo" placeholder="平台用户工号（如 zhangsan）" />
        </el-form-item>
        <el-form-item label="部门">
          <el-select v-model="bindForm.deptId" placeholder="默认取用户所属部门" clearable style="width: 100%">
            <el-option v-for="d in depts" :key="d.id" :label="d.name" :value="d.id" />
          </el-select>
        </el-form-item>
        <el-form-item v-if="bindResult">
          <el-text type="success">{{ bindResult }}</el-text>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="bindVisible = false">关闭</el-button>
        <el-button type="primary" @click="saveBind()">保存并下发</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.pager {
  margin-top: 12px;
  display: flex;
  justify-content: flex-end;
}
</style>
