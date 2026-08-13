<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { api, ApiError, apiText } from '../api/client'
import type { DeptItem, ImportResult, RecorderItem, UserItem } from '../api/types'
import { downloadText } from '../utils/export'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const items = ref<RecorderItem[]>([])
const depts = ref<DeptItem[]>([])
const users = ref<UserItem[]>([])
const loading = ref(false)
const bindVisible = ref(false)
const bindRow = ref<RecorderItem | null>(null)
const bindForm = reactive({ userId: undefined as number | undefined, deptId: undefined as number | undefined })
const importResult = ref<ImportResult | null>(null)
const showImportResult = computed({
  get: () => importResult.value !== null,
  set: (v: boolean) => {
    if (!v) importResult.value = null
  }
})

async function loadAll() {
  loading.value = true
  try {
    items.value = await api<RecorderItem[]>('/recorders')
    if (auth.hasPermission('dept:view')) depts.value = await api<DeptItem[]>('/depts')
    if (auth.hasPermission('user:view')) users.value = await api<UserItem[]>('/users')
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '加载失败')
  } finally {
    loading.value = false
  }
}

function openBind(row: RecorderItem) {
  bindRow.value = row
  bindForm.userId = row.boundUserId ?? undefined
  bindForm.deptId = row.deptId ?? undefined
  bindVisible.value = true
}

async function saveBind() {
  if (!bindRow.value || bindForm.userId === undefined) {
    ElMessage.warning('请选择绑定用户')
    return
  }
  try {
    await api<boolean>(`/recorders/${bindRow.value.id}/bind`, {
      method: 'PUT',
      body: JSON.stringify({ userId: bindForm.userId, deptId: bindForm.deptId ?? null })
    })
    ElMessage.success('绑定已写入记录仪')
    bindVisible.value = false
    loadAll()
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '绑定失败')
  }
}

function downloadTemplate() {
  downloadText('记录仪绑定导入模板.csv', '序列号,用户工号,部门编码\nR-001,zhangsan,')
}

async function handleFile(file: { raw?: File }) {
  if (!file.raw) return
  try {
    importResult.value = await apiText<ImportResult>('/imports/recorder-bindings', await file.raw.text())
    ElMessage.success(`导入完成：成功 ${importResult.value.success}，失败 ${importResult.value.failed}`)
    loadAll()
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '导入失败')
  }
}

onMounted(loadAll)
</script>

<template>
  <div>
    <el-form inline>
      <el-form-item>
        <el-button type="primary" :loading="loading" @click="loadAll()">刷新</el-button>
      </el-form-item>
      <el-form-item v-if="auth.hasPermission('recorder:manage')">
        <el-upload :auto-upload="false" :show-file-list="false" accept=".csv" :on-change="handleFile" style="display: inline-block">
          <el-button type="primary">导入绑定</el-button>
        </el-upload>
        <el-button @click="downloadTemplate()">下载模板</el-button>
      </el-form-item>
    </el-form>

    <el-table v-loading="loading" :data="items" border stripe>
      <el-table-column prop="serialNumber" label="序列号" width="170" />
      <el-table-column prop="model" label="型号" width="120" />
      <el-table-column label="协议" width="110">
        <template #default="{ row }">{{ ['UMS', 'MTP', '私有SDK'][(row as RecorderItem).protocol] }}</template>
      </el-table-column>
      <el-table-column label="绑定用户" width="120">
        <template #default="{ row }">{{ users.find((u) => u.id === (row as RecorderItem).boundUserId)?.name || '-' }}</template>
      </el-table-column>
      <el-table-column label="绑定部门" width="120">
        <template #default="{ row }">{{ depts.find((d) => d.id === (row as RecorderItem).deptId)?.name || '-' }}</template>
      </el-table-column>
      <el-table-column label="白名单" width="90">
        <template #default="{ row }">
          <el-tag :type="(row as RecorderItem).isAuthorized ? 'success' : 'danger'" size="small">{{ (row as RecorderItem).isAuthorized ? '是' : '否' }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column v-if="auth.hasPermission('recorder:manage')" label="操作" width="90" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" @click="openBind(row as RecorderItem)">写入绑定</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="bindVisible" :title="`写入绑定：${bindRow?.serialNumber ?? ''}`" width="420px">
      <el-form label-width="80px">
        <el-form-item label="绑定用户">
          <el-select v-model="bindForm.userId" style="width: 100%">
            <el-option v-for="u in users.filter(x => x.id !== null)" :key="u.id" :label="`${u.name}（${u.userNo}）`" :value="u.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="部门">
          <el-select v-model="bindForm.deptId" clearable placeholder="默认取用户部门" style="width: 100%">
            <el-option v-for="d in depts.filter(x => x.id !== null)" :key="d.id" :label="d.name" :value="d.id" />
          </el-select>
        </el-form-item>
        <el-alert type="info" :closable="false" title="请确认当前插入的即为该记录仪，绑定将写入其根目录 station_bind.ini" />
      </el-form>
      <template #footer>
        <el-button @click="bindVisible = false">取消</el-button>
        <el-button type="primary" @click="saveBind()">确认写入</el-button>
      </template>
    </el-dialog>

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
