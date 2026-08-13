<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { api, ApiError } from '../../api/client'
import type { DeptItem, PagedResult, RoleItem, UserItem } from '../../api/types'
import { useAuthStore } from '../../stores/auth'

const auth = useAuthStore()
const loading = ref(false)
const items = ref<UserItem[]>([])
const total = ref(0)
const page = ref(1)
const size = 20
const depts = ref<DeptItem[]>([])
const roles = ref<RoleItem[]>([])
const query = reactive({ keyword: '', deptId: '' })
const dialogVisible = ref(false)
const editingId = ref<number | null>(null)
const form = reactive({
  userNo: '',
  name: '',
  deptId: undefined as number | undefined,
  password: '',
  roleIds: [] as number[],
  isActive: true
})

async function loadDepts() {
  if (!auth.hasPermission('dept:view')) return
  try {
    depts.value = await api<DeptItem[]>('/depts')
  } catch {
    depts.value = []
  }
}

async function loadRoles() {
  try {
    roles.value = await api<RoleItem[]>('/roles')
  } catch {
    roles.value = []
  }
}

async function loadUsers() {
  loading.value = true
  try {
    const params = new URLSearchParams({ page: String(page.value), size: String(size) })
    if (query.keyword) params.set('keyword', query.keyword)
    if (query.deptId) params.set('deptId', query.deptId)
    const data = await api<PagedResult<UserItem>>(`/users?${params.toString()}`)
    items.value = data.items
    total.value = data.totalCount
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '查询失败')
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
  form.isActive = true
  dialogVisible.value = true
}

function openEdit(row: UserItem) {
  editingId.value = row.id
  form.userNo = row.userNo
  form.name = row.name
  form.deptId = row.deptId
  form.password = ''
  form.isActive = row.isActive
  form.roleIds = roles.value
    .filter((r) => row.roles.includes(r.name))
    .map((r) => r.id)
  dialogVisible.value = true
}

async function save() {
  if (!form.userNo || !form.name || !form.deptId) {
    ElMessage.warning('请填写工号、姓名与部门')
    return
  }
  try {
    if (editingId.value === null) {
      await api<boolean>('/users', {
        method: 'POST',
        body: JSON.stringify({
          userNo: form.userNo,
          name: form.name,
          deptId: form.deptId,
          password: form.password || null,
          roleIds: form.roleIds
        })
      })
    } else {
      await api<boolean>(`/users/${editingId.value}`, {
        method: 'PUT',
        body: JSON.stringify({
          name: form.name,
          deptId: form.deptId,
          isActive: form.isActive,
          roleIds: form.roleIds
        })
      })
    }
    ElMessage.success('已保存')
    dialogVisible.value = false
    loadUsers()
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '保存失败')
  }
}

async function resetPassword(row: UserItem) {
  try {
    const { value } = await ElMessageBox.prompt(`重置用户「${row.name}」的密码（留空则使用默认 Station@123）`, '重置密码', {
      inputType: 'password',
      inputPlaceholder: '留空使用默认密码'
    })
    await api<boolean>(`/users/${row.id}/reset-password`, {
      method: 'POST',
      body: JSON.stringify({ password: value || null })
    })
    ElMessage.success('密码已重置')
  } catch (e) {
    if (e !== 'cancel' && e !== 'close') {
      ElMessage.error(e instanceof ApiError ? e.message : '重置失败')
    }
  }
}

function onPageChange(p: number) {
  page.value = p
  loadUsers()
}

onMounted(() => {
  loadDepts()
  loadRoles()
  loadUsers()
})
</script>

<template>
  <div>
    <el-form inline>
      <el-form-item>
        <el-input v-model="query.keyword" placeholder="工号/姓名关键字" clearable style="width: 180px" @keyup.enter="loadUsers()" />
      </el-form-item>
      <el-form-item v-if="auth.hasPermission('dept:view')">
        <el-select v-model="query.deptId" placeholder="全部部门" clearable style="width: 150px">
          <el-option v-for="d in depts" :key="d.id" :label="d.name" :value="String(d.id)" />
        </el-select>
      </el-form-item>
      <el-form-item>
        <el-button type="primary" @click="page = 1; loadUsers()">查询</el-button>
      </el-form-item>
      <el-form-item v-if="auth.hasPermission('user:manage')">
        <el-button type="success" @click="openCreate()">新增用户</el-button>
      </el-form-item>
    </el-form>

    <el-table v-loading="loading" :data="items" border stripe>
      <el-table-column prop="userNo" label="工号" width="110" />
      <el-table-column prop="name" label="姓名" width="120" />
      <el-table-column prop="accountName" label="账号" width="120" />
      <el-table-column prop="deptName" label="部门" width="130" />
      <el-table-column label="角色" min-width="160">
        <template #default="{ row }">
          <el-tag v-for="r in (row as UserItem).roles" :key="r" size="small" style="margin-right: 4px">{{ r }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="状态" width="80">
        <template #default="{ row }">
          <el-tag :type="(row as UserItem).isActive ? 'success' : 'info'" size="small">{{ (row as UserItem).isActive ? '启用' : '停用' }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column v-if="auth.hasPermission('user:manage')" label="操作" width="180" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" @click="openEdit(row as UserItem)">编辑</el-button>
          <el-button link type="warning" @click="resetPassword(row as UserItem)">重置密码</el-button>
        </template>
      </el-table-column>
    </el-table>

    <div class="pager">
      <el-pagination background layout="total, prev, pager, next" :total="total" :page-size="size" :current-page="page" @current-change="onPageChange" />
    </div>

    <el-dialog v-model="dialogVisible" :title="editingId === null ? '新增用户' : '编辑用户'" width="480px">
      <el-form label-width="90px">
        <el-form-item label="工号">
          <el-input v-model="form.userNo" :disabled="editingId !== null" placeholder="登录账号同工号" />
        </el-form-item>
        <el-form-item label="姓名">
          <el-input v-model="form.name" />
        </el-form-item>
        <el-form-item label="部门">
          <el-select v-model="form.deptId" placeholder="选择部门" style="width: 100%">
            <el-option v-for="d in depts" :key="d.id" :label="d.name" :value="d.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="角色">
          <el-select v-model="form.roleIds" multiple placeholder="选择角色" style="width: 100%">
            <el-option v-for="r in roles" :key="r.id" :label="r.name" :value="r.id" />
          </el-select>
        </el-form-item>
        <el-form-item v-if="editingId === null" label="初始密码">
          <el-input v-model="form.password" type="password" placeholder="留空使用默认 Station@123" show-password />
        </el-form-item>
        <el-form-item v-else label="启用">
          <el-switch v-model="form.isActive" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="save()">保存</el-button>
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
