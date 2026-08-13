<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { api, ApiError, apiText } from '../api/client'
import type { DeptItem, ImportResult, RoleItem, UserItem } from '../api/types'
import { downloadText } from '../utils/export'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const items = ref<UserItem[]>([])
const depts = ref<DeptItem[]>([])
const roles = ref<RoleItem[]>([])
const loading = ref(false)
const dialogVisible = ref(false)
const editingId = ref<number | null>(null)
const form = reactive({ userNo: '', name: '', deptId: undefined as number | undefined, password: '', roleIds: [] as number[] })
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
    items.value = await api<UserItem[]>('/users')
    if (auth.hasPermission('dept:view')) depts.value = await api<DeptItem[]>('/depts')
    if (auth.hasPermission('role:view')) roles.value = await api<RoleItem[]>('/roles')
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '加载失败')
  } finally {
    loading.value = false
  }
}

function openCreate() {
  editingId.value = null
  form.userNo = ''
  form.name = ''
  form.deptId = undefined
  form.password = ''
  form.roleIds = []
  dialogVisible.value = true
}

function openEdit(row: UserItem) {
  editingId.value = row.id
  form.userNo = row.userNo
  form.name = row.name
  form.deptId = row.deptId
  form.password = ''
  form.roleIds = []
  dialogVisible.value = true
}

async function save() {
  try {
    if (editingId.value === null) {
      await api<boolean>('/users', {
        method: 'POST',
        body: JSON.stringify({ userNo: form.userNo, name: form.name, deptId: form.deptId, password: form.password || null })
      })
    } else {
      await api<boolean>(`/users/${editingId.value}`, {
        method: 'PUT',
        body: JSON.stringify({ userNo: form.userNo, name: form.name, deptId: form.deptId })
      })
    }
    ElMessage.success('已保存')
    dialogVisible.value = false
    loadAll()
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '保存失败')
  }
}

async function resetPassword(row: UserItem) {
  try {
    const { value } = await ElMessageBox.prompt(`重置「${row.name}」密码`, '重置密码', { inputType: 'password' })
    await api<boolean>(`/users/${row.id}/reset-password`, {
      method: 'POST',
      body: JSON.stringify({ password: value })
    })
    ElMessage.success('密码已重置')
  } catch (e) {
    if (e !== 'cancel' && e !== 'close') ElMessage.error(e instanceof ApiError ? e.message : '重置失败')
  }
}

function downloadTemplate() {
  downloadText('用户导入模板.csv', '工号,姓名,部门编码,角色编码,初始密码\nuser01,用户一,TEAM9,operator,\nuser02,用户二,GRP9,,\n')
}

async function handleFile(file: { raw?: File }) {
  if (!file.raw) return
  try {
    importResult.value = await apiText<ImportResult>('/imports/users', await file.raw.text())
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
      <el-form-item v-if="auth.hasPermission('user:manage')">
        <el-button type="success" @click="openCreate()">新增用户</el-button>
        <el-upload :auto-upload="false" :show-file-list="false" accept=".csv" :on-change="handleFile" style="display: inline-block; margin-left: 8px">
          <el-button type="primary">导入</el-button>
        </el-upload>
        <el-button @click="downloadTemplate()">下载模板</el-button>
      </el-form-item>
    </el-form>

    <el-table v-loading="loading" :data="items" border stripe>
      <el-table-column prop="userNo" label="工号" width="130" />
      <el-table-column prop="name" label="姓名" width="130" />
      <el-table-column label="部门" width="140">
        <template #default="{ row }">{{ depts.find((d) => d.id === (row as UserItem).deptId)?.name || (row as UserItem).deptId }}</template>
      </el-table-column>
      <el-table-column label="状态" width="80">
        <template #default="{ row }">
          <el-tag :type="(row as UserItem).isActive ? 'success' : 'info'" size="small">{{ (row as UserItem).isActive ? '启用' : '停用' }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column v-if="auth.hasPermission('user:manage')" label="操作" width="160" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" @click="openEdit(row as UserItem)">编辑</el-button>
          <el-button link type="warning" @click="resetPassword(row as UserItem)">重置密码</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="dialogVisible" :title="editingId === null ? '新增用户' : '编辑用户'" width="440px">
      <el-form label-width="80px">
        <el-form-item label="工号">
          <el-input v-model="form.userNo" :disabled="editingId !== null" />
        </el-form-item>
        <el-form-item label="姓名">
          <el-input v-model="form.name" />
        </el-form-item>
        <el-form-item label="部门">
          <el-select v-model="form.deptId" style="width: 100%">
            <el-option v-for="d in depts.filter(x => x.id !== null)" :key="d.id" :label="d.name" :value="d.id" />
          </el-select>
        </el-form-item>
        <el-form-item v-if="editingId === null" label="密码">
          <el-input v-model="form.password" type="password" placeholder="留空使用默认" show-password />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="save()">保存</el-button>
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
