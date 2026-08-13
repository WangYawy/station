<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { api, ApiError } from '../api/client'
import type { FileItem, PagedResult } from '../api/types'
import { FILE_STATUS, UPLOAD_STATUS, fmtSize, fmtTime } from '../utils/format'

const loading = ref(false)
const items = ref<FileItem[]>([])
const total = ref(0)
const page = ref(1)
const size = 20
const query = reactive({ keyword: '', extension: '', status: '', syncStatus: '' })
const previewVisible = ref(false)
const previewSrc = ref('')
const previewName = ref('')

function buildParams(exportMode = false) {
  const params = new URLSearchParams()
  if (query.keyword) params.set('keyword', query.keyword)
  if (query.extension) params.set('extension', query.extension)
  if (query.status !== '') params.set('status', query.status)
  if (query.syncStatus !== '') params.set('syncStatus', query.syncStatus)
  if (exportMode) {
    params.delete('page')
    params.delete('size')
  } else {
    params.set('page', String(page.value))
    params.set('size', String(size))
  }
  return params.toString()
}

async function loadFiles() {
  loading.value = true
  try {
    const data = await api<PagedResult<FileItem>>(`/files?${buildParams()}`)
    items.value = data.items
    total.value = data.totalCount
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '查询失败')
  } finally {
    loading.value = false
  }
}

async function doExport(format: string) {
  try {
    const resp = await fetch(`/api/v1/files/export?${buildParams(true)}&format=${format}`)
    if (!resp.ok) throw new ApiError(resp.status, '导出失败')
    const blob = await resp.blob()
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `文件台账.${format}`
    a.click()
    URL.revokeObjectURL(url)
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '导出失败')
  }
}

function openPreview(row: FileItem) {
  if (!row.fileNo) {
    ElMessage.warning('该文件尚无业务编号，无法预览')
    return
  }
  previewName.value = row.fileName
  previewSrc.value = `/api/v1/files/${row.fileNo}/stream`
  previewVisible.value = true
}

onMounted(loadFiles)
</script>

<template>
  <div>
    <el-form inline>
      <el-form-item>
        <el-input v-model="query.keyword" placeholder="文件名/编号" clearable style="width: 200px" @keyup.enter="loadFiles()" />
      </el-form-item>
      <el-form-item>
        <el-input v-model="query.extension" placeholder="类型(如 mp4)" clearable style="width: 120px" />
      </el-form-item>
      <el-form-item>
        <el-select v-model="query.status" placeholder="采集状态" clearable style="width: 120px">
          <el-option v-for="(s, i) in FILE_STATUS" :key="i" :label="s" :value="String(i)" />
        </el-select>
      </el-form-item>
      <el-form-item>
        <el-select v-model="query.syncStatus" placeholder="上传状态" clearable style="width: 120px">
          <el-option v-for="(s, i) in UPLOAD_STATUS" :key="i" :label="s" :value="String(i)" />
        </el-select>
      </el-form-item>
      <el-form-item>
        <el-button type="primary" @click="page = 1; loadFiles()">查询</el-button>
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
      <el-table-column prop="fileNo" label="编号" width="140" />
      <el-table-column prop="fileName" label="文件名" min-width="180" show-overflow-tooltip />
      <el-table-column prop="extension" label="类型" width="70" />
      <el-table-column label="大小" width="90">
        <template #default="{ row }">{{ fmtSize((row as FileItem).size) }}</template>
      </el-table-column>
      <el-table-column label="采集时间" width="150">
        <template #default="{ row }">{{ fmtTime((row as FileItem).collectedAt) }}</template>
      </el-table-column>
      <el-table-column label="原始时间" width="150">
        <template #default="{ row }">{{ fmtTime((row as FileItem).originalModifiedAt) }}</template>
      </el-table-column>
      <el-table-column label="采集状态" width="90">
        <template #default="{ row }">{{ FILE_STATUS[(row as FileItem).status] }}</template>
      </el-table-column>
      <el-table-column label="上传状态" width="90">
        <template #default="{ row }">{{ UPLOAD_STATUS[(row as FileItem).syncStatus] }}</template>
      </el-table-column>
      <el-table-column prop="recorder" label="记录仪" width="120" />
      <el-table-column prop="deptName" label="部门" width="90" />
      <el-table-column label="SM3" min-width="150">
        <template #default="{ row }">
          <span style="font-size: 12px">{{ (row as FileItem).sm3 || '-' }}</span>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="80" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" @click="openPreview(row as FileItem)">预览</el-button>
        </template>
      </el-table-column>
    </el-table>

    <div class="pager">
      <el-pagination background layout="total, prev, pager, next" :total="total" :page-size="size" :current-page="page" @current-change="(p) => { page = p; loadFiles() }" />
    </div>

    <el-dialog v-model="previewVisible" :title="previewName" width="720px" destroy-on-close>
      <video :src="previewSrc" controls style="width: 100%; max-height: 380px" />
      <p style="font-size: 12px; color: #64748b">H.264 在线播放；H.265 请下载后播放</p>
    </el-dialog>
  </div>
</template>

<style scoped>
.pager { margin-top: 12px; display: flex; justify-content: flex-end; }
</style>
