<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { api, ApiError } from '../api/client'
import type { DeptItem } from '../api/types'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const items = ref<DeptItem[]>([])
const loading = ref(false)
const dialogVisible = ref(false)
const editingId = ref<number | null>(null)
const form = reactive({ code: '', name: '', parentId: null as number | null, sortOrder: 0 })

async function loadDepts() {
  loading.value = true
  try {
    items.value = await api<DeptItem[]>('/depts')
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '加载失败')
  } finally {
    loading.value = false
  }
}

function openCreate() {
  editingId.value = null
  form.code = ''
  form.name = ''
  form.parentId = null
  form.sortOrder = 0
  dialogVisible.value = true
}

function openEdit(row: DeptItem) {
  editingId.value = row.id
  form.code = row.code
  form.name = row.name
  form.parentId = row.parentId
  form.sortOrder = row.sortOrder
  dialogVisible.value = true
}

async function save() {
  try {
    const body = JSON.stringify({ code: form.code, name: form.name, parentId: form.parentId, sortOrder: form.sortOrder })
    if (editingId.value === null) {
      await api<number>('/depts', { method: 'POST', body })
    } else {
      await api<boolean>(`/depts/${editingId.value}`, { method: 'PUT', body })
    }
    ElMessage.success('已保存')
    dialogVisible.value = false
    loadDepts()
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '保存失败')
  }
}

async function remove(row: DeptItem) {
  try {
    await ElMessageBox.confirm(`确定删除部门「${row.name}」？`, '提示', { type: 'warning' })
  } catch {
    return
  }
  try {
    await api<boolean>(`/depts/${row.id}`, { method: 'DELETE' })
    ElMessage.success('已删除')
    loadDepts()
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '删除失败')
  }
}

onMounted(loadDepts)
</script>

<template>
  <div>
    <el-form inline>
      <el-form-item>
        <el-button type="primary" :loading="loading" @click="loadDepts()">刷新</el-button>
      </el-form-item>
      <el-form-item v-if="auth.hasPermission('dept:manage')">
        <el-button type="success" @click="openCreate()">新增部门</el-button>
      </el-form-item>
    </el-form>

    <el-table v-loading="loading" :data="items" border stripe>
      <el-table-column prop="code" label="编码" width="140" />
      <el-table-column prop="name" label="名称" width="160" />
      <el-table-column prop="parentId" label="上级ID" width="90" />
      <el-table-column prop="sortOrder" label="排序" width="70" />
      <el-table-column label="状态" width="80">
        <template #default="{ row }">
          <el-tag :type="(row as DeptItem).isActive ? 'success' : 'info'" size="small">{{ (row as DeptItem).isActive ? '启用' : '停用' }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column v-if="auth.hasPermission('dept:manage')" label="操作" width="140" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" @click="openEdit(row as DeptItem)">编辑</el-button>
          <el-button link type="danger" @click="remove(row as DeptItem)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="dialogVisible" :title="editingId === null ? '新增部门' : '编辑部门'" width="420px">
      <el-form label-width="80px">
        <el-form-item label="编码">
          <el-input v-model="form.code" :disabled="editingId !== null" />
        </el-form-item>
        <el-form-item label="名称">
          <el-input v-model="form.name" />
        </el-form-item>
        <el-form-item label="上级部门">
          <el-select v-model="form.parentId" clearable placeholder="顶级" style="width: 100%">
            <el-option v-for="d in items.filter(x => x.id !== null && x.id !== editingId)" :key="d.id" :label="d.name" :value="d.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="排序">
          <el-input-number v-model="form.sortOrder" :min="0" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="save()">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>
