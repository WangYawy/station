<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { api, ApiError } from '../api/client'
import type { PermissionItem, RoleItem } from '../api/types'
import { DATA_SCOPES } from '../utils/format'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const items = ref<RoleItem[]>([])
const permissions = ref<PermissionItem[]>([])
const loading = ref(false)
const dialogVisible = ref(false)
const editingId = ref<number | null>(null)
const form = reactive({ code: '', name: '', dataScope: 3, permissionIds: [] as number[] })

const permissionGroups = computed(() => {
  const map = new Map<string, PermissionItem[]>()
  permissions.value.forEach((p) => {
    const list = map.get(p.module) ?? []
    list.push(p)
    map.set(p.module, list)
  })
  return [...map.entries()]
})

async function loadAll() {
  loading.value = true
  try {
    items.value = await api<RoleItem[]>('/roles')
    permissions.value = await api<PermissionItem[]>('/permissions')
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
  form.dataScope = 3
  form.permissionIds = []
  dialogVisible.value = true
}

function openEdit(row: RoleItem) {
  editingId.value = row.id
  form.code = row.code
  form.name = row.name
  form.dataScope = row.dataScope
  form.permissionIds = []
  dialogVisible.value = true
}

async function save() {
  try {
    if (editingId.value === null) {
      const id = await api<number>('/roles', {
        method: 'POST',
        body: JSON.stringify({ code: form.code || null, name: form.name, dataScope: form.dataScope })
      })
      await api<boolean>(`/roles/${id}/permissions`, {
        method: 'PUT',
        body: JSON.stringify({ permissionIds: form.permissionIds })
      })
    } else {
      await api<boolean>(`/roles/${editingId.value}`, {
        method: 'PUT',
        body: JSON.stringify({ code: form.code, name: form.name, dataScope: form.dataScope })
      })
      await api<boolean>(`/roles/${editingId.value}/permissions`, {
        method: 'PUT',
        body: JSON.stringify({ permissionIds: form.permissionIds })
      })
    }
    ElMessage.success('已保存')
    dialogVisible.value = false
    loadAll()
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '保存失败')
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
      <el-form-item v-if="auth.hasPermission('role:manage')">
        <el-button type="success" @click="openCreate()">新增角色</el-button>
      </el-form-item>
    </el-form>

    <el-table v-loading="loading" :data="items" border stripe>
      <el-table-column prop="code" label="编码" width="160" />
      <el-table-column prop="name" label="名称" width="150" />
      <el-table-column label="数据范围" width="130">
        <template #default="{ row }">{{ DATA_SCOPES[(row as RoleItem).dataScope] }}</template>
      </el-table-column>
      <el-table-column label="类型" width="90">
        <template #default="{ row }">
          <el-tag :type="(row as RoleItem).isSystem ? 'info' : 'success'" size="small">{{ (row as RoleItem).isSystem ? '预置' : '自定义' }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column v-if="auth.hasPermission('role:manage')" label="操作" width="90" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" :disabled="(row as RoleItem).isSystem" @click="openEdit(row as RoleItem)">编辑</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="dialogVisible" :title="editingId === null ? '新增角色' : '编辑角色'" width="520px">
      <el-form label-width="80px">
        <el-form-item label="编码">
          <el-input v-model="form.code" :disabled="editingId !== null" placeholder="自定义角色留空自动生成" />
        </el-form-item>
        <el-form-item label="名称">
          <el-input v-model="form.name" />
        </el-form-item>
        <el-form-item label="数据范围">
          <el-select v-model="form.dataScope" style="width: 200px">
            <el-option v-for="(s, i) in DATA_SCOPES" :key="i" :label="s" :value="i" />
          </el-select>
        </el-form-item>
        <el-form-item label="权限">
          <div style="width: 100%; max-height: 280px; overflow: auto; border: 1px solid #e2e8f0; padding: 8px 12px">
            <div v-for="[module, list] in permissionGroups" :key="module" style="margin-bottom: 8px">
              <div style="font-weight: 600">{{ module }}</div>
              <el-checkbox-group v-model="form.permissionIds">
                <el-checkbox v-for="p in list" :key="p.id" :value="p.id">{{ p.name }}</el-checkbox>
              </el-checkbox-group>
            </div>
          </div>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="save()">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>
