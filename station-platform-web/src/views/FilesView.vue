<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { api, ApiError } from '../api/client'
import type { DeptItem, FileItem, PagedResult } from '../api/types'
import { FILE_KINDS, fmtSize, fmtTime } from '../utils/format'
import { downloadCsv } from '../utils/export'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const loading = ref(false)
const items = ref<FileItem[]>([])
const total = ref(0)
const page = ref(1)
const size = 20
const depts = ref<DeptItem[]>([])
const query = reactive({ keyword: '', kind: '', deptId: '' })
const previewVisible = ref(false)
const previewSrc = ref('')
const previewName = ref('')

async function loadDepts() {
  if (!auth.hasPermission('dept:view')) return
  try {
    depts.value = await api<DeptItem[]>('/depts')
  } catch {
    depts.value = []
  }
}

async function loadFiles() {
  loading.value = true
  try {
    const params = new URLSearchParams({ page: String(page.value), size: String(size) })
    if (query.keyword) params.set('keyword', query.keyword)
    if (query.kind !== '') params.set('kind', query.kind)
    if (query.deptId !== '') params.set('deptId', query.deptId)
    const data = await api<PagedResult<FileItem>>(`/files?${params.toString()}`)
    items.value = data.items
    total.value = data.totalCount
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '查询失败')
  } finally {
    loading.value = false
  }
}

function openPreview(f: FileItem) {
  previewName.value = f.fileName
  previewSrc.value = `/api/v1/files/${f.fileNo}/preview`
  previewVisible.value = true
}

function onPageChange(p: number) {
  page.value = p
  loadFiles()
}

async function doExport() {
  const params = new URLSearchParams()
  if (query.keyword) params.set('keyword', query.keyword)
  if (query.kind !== '') params.set('kind', query.kind)
  if (query.deptId !== '') params.set('deptId', query.deptId)
  try {
    await downloadCsv(`/exports/files?${params.toString()}`)
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '导出失败')
  }
}

onMounted(() => {
  loadDepts()
  loadFiles()
})
</script>

<template>
  <div>
    <el-form inline>
      <el-form-item>
        <el-input v-model="query.keyword" placeholder="文件名/编号/记录仪关键字" clearable style="width: 240px" @keyup.enter="loadFiles()" />
      </el-form-item>
      <el-form-item>
        <el-select v-model="query.kind" placeholder="全部类型" clearable style="width: 130px">
          <el-option v-for="(k, i) in FILE_KINDS" :key="i" :label="k" :value="String(i)" />
        </el-select>
      </el-form-item>
      <el-form-item v-if="auth.hasPermission('dept:view')">
        <el-select v-model="query.deptId" placeholder="全部部门" clearable style="width: 150px">
          <el-option v-for="d in depts" :key="d.id" :label="d.name" :value="String(d.id)" />
        </el-select>
      </el-form-item>
      <el-form-item>
        <el-button type="primary" @click="page = 1; loadFiles()">查询</el-button>
      </el-form-item>
      <el-form-item>
        <el-button @click="doExport()">导出</el-button>
      </el-form-item>
    </el-form>

    <el-table v-loading="loading" :data="items" border stripe>
      <el-table-column prop="fileNo" label="编号" width="150" />
      <el-table-column prop="fileName" label="文件名" min-width="220" show-overflow-tooltip />
      <el-table-column label="类型" width="70">
        <template #default="{ row }">{{ FILE_KINDS[row.kind] }}</template>
      </el-table-column>
      <el-table-column label="大小" width="100">
        <template #default="{ row }">{{ fmtSize(row.size) }}</template>
      </el-table-column>
      <el-table-column label="采集时间" width="160">
        <template #default="{ row }">{{ fmtTime(row.collectedAt) }}</template>
      </el-table-column>
      <el-table-column prop="recorderSerial" label="记录仪" width="130" />
      <el-table-column prop="userNo" label="用户" width="90" />
      <el-table-column prop="deptCode" label="部门" width="90" />
      <el-table-column label="操作" width="90" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" @click="openPreview(row as FileItem)">预览</el-button>
        </template>
      </el-table-column>
    </el-table>

    <div class="pager">
      <el-pagination background layout="total, prev, pager, next" :total="total" :page-size="size" :current-page="page" @current-change="onPageChange" />
    </div>

    <el-dialog v-model="previewVisible" :title="previewName" width="720px" destroy-on-close>
      <video :src="previewSrc" controls style="width: 100%; max-height: 380px" />
      <p style="font-size: 12px; color: #64748b">H.264 在线播放；H.265 请下载后播放（转码为 P2 规划）</p>
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
