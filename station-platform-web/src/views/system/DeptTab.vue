<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { api, ApiError, apiText } from '../../api/client'
import type { DeptItem, ImportResult } from '../../api/types'
import { downloadText } from '../../utils/export'
import { useAuthStore } from '../../stores/auth'

interface DeptNode extends DeptItem {
  children: DeptNode[]
}

const auth = useAuthStore()
const loading = ref(false)
const tree = ref<DeptNode[]>([])
const dialogVisible = ref(false)
const editingId = ref<number | null>(null)
const form = reactive({ code: '', name: '', parentId: undefined as number | undefined, sortOrder: 0 })
const importResult = ref<ImportResult | null>(null)
const showImportResult = computed({
  get: () => importResult.value !== null,
  set: (v: boolean) => {
    if (!v) importResult.value = null
  }
})

function buildTree(list: DeptItem[]): DeptNode[] {
  const map = new Map<number, DeptNode>()
  list.forEach((d) => map.set(d.id, { ...d, children: [] }))
  const roots: DeptNode[] = []
  map.forEach((node) => {
    if (node.parentId && map.has(node.parentId)) {
      map.get(node.parentId)!.children.push(node)
    } else {
      roots.push(node)
    }
  })
  return roots
}

async function loadDepts() {
  loading.value = true
  try {
    tree.value = buildTree(await api<DeptItem[]>('/depts'))
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '加载失败')
  } finally {
    loading.value = false
  }
}

function openCreate(parentId?: number) {
  editingId.value = null
  form.code = ''
  form.name = ''
  form.parentId = parentId
  form.sortOrder = 0
  dialogVisible.value = true
}

function openEdit(node: DeptNode) {
  editingId.value = node.id
  form.code = node.code
  form.name = node.name
  form.parentId = node.parentId ?? undefined
  form.sortOrder = node.sortOrder
  dialogVisible.value = true
}

async function save() {
  try {
    if (editingId.value === null) {
      await api<boolean>('/depts', {
        method: 'POST',
        body: JSON.stringify({ code: form.code, name: form.name, parentId: form.parentId ?? null, sortOrder: form.sortOrder })
      })
    } else {
      await api<boolean>(`/depts/${editingId.value}`, {
        method: 'PUT',
        body: JSON.stringify({ name: form.name, parentId: form.parentId ?? null, sortOrder: form.sortOrder, isActive: true })
      })
    }
    ElMessage.success('已保存')
    dialogVisible.value = false
    loadDepts()
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '保存失败')
  }
}

async function remove(node: DeptNode) {
  try {
    await ElMessageBox.confirm(`确定停用部门「${node.name}」？`, '提示', { type: 'warning' })
  } catch {
    return
  }
  try {
    await api<boolean>(`/depts/${node.id}`, { method: 'DELETE' })
    ElMessage.success('已停用')
    loadDepts()
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '删除失败')
  }
}

function downloadDeptTemplate() {
  downloadText('部门导入模板.csv', '编码,名称,上级编码,排序\nTEAM9,九队,,\nGRP9,九组,TEAM9,1')
}

async function handleDeptFile(file: { raw?: File }) {
  if (!file.raw) return
  const text = await file.raw.text()
  try {
    importResult.value = await apiText<ImportResult>('/imports/depts', text)
    ElMessage.success(`导入完成：成功 ${importResult.value.success}，失败 ${importResult.value.failed}`)
    loadDepts()
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '导入失败')
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
        <el-button type="success" @click="openCreate()">新增根部门</el-button>
        <el-upload :auto-upload="false" :show-file-list="false" accept=".csv" :on-change="handleDeptFile" style="display: inline-block; margin-left: 8px">
          <el-button type="primary">导入</el-button>
        </el-upload>
        <el-button @click="downloadDeptTemplate()">下载模板</el-button>
      </el-form-item>
    </el-form>

    <el-tree v-loading="loading" :data="tree" node-key="id" default-expand-all :expand-on-click-node="false">
      <template #default="{ data }">
        <span class="node-row">
          <span>{{ (data as DeptNode).name }}（{{ (data as DeptNode).code }}）</span>
          <span v-if="auth.hasPermission('dept:manage')" class="node-actions">
            <el-button link type="primary" size="small" @click="openCreate((data as DeptNode).id)">新增子部门</el-button>
            <el-button link type="primary" size="small" @click="openEdit(data as DeptNode)">编辑</el-button>
            <el-button link type="danger" size="small" @click="remove(data as DeptNode)">停用</el-button>
          </span>
        </span>
      </template>
    </el-tree>

    <el-dialog v-model="dialogVisible" :title="editingId === null ? '新增部门' : '编辑部门'" width="440px">
      <el-form label-width="90px">
        <el-form-item label="编码">
          <el-input v-model="form.code" :disabled="editingId !== null" placeholder="唯一编码" />
        </el-form-item>
        <el-form-item label="名称">
          <el-input v-model="form.name" placeholder="部门名称" />
        </el-form-item>
        <el-form-item label="上级部门">
          <el-input :model-value="form.parentId ? String(form.parentId) : '（顶级）'" disabled />
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

    <el-dialog v-model="showImportResult" title="导入结果" width="560px">
      <p>
        共 {{ importResult?.total }} 行：成功 <b style="color:#16a34a">{{ importResult?.success }}</b>，
        失败 <b style="color:#dc2626">{{ importResult?.failed }}</b>
      </p>
      <el-table v-if="importResult?.errors.length" :data="importResult.errors" border size="small">
        <el-table-column prop="line" label="行号" width="80" />
        <el-table-column prop="message" label="原因" min-width="300" />
      </el-table>
      <template #footer>
        <el-button type="primary" @click="importResult = null">关闭</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.node-row {
  display: inline-flex;
  align-items: center;
  gap: 12px;
}
.node-actions {
  visibility: hidden;
}
.node-row:hover .node-actions {
  visibility: visible;
}
</style>
