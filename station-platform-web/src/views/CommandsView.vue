<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api, ApiError } from '../api/client'
import type { CommandItem } from '../api/types'
import { CMD_STATUS, CMD_TYPES, fmtTime } from '../utils/format'

const loading = ref(false)
const stationId = ref('')
const cmdType = ref('0')
const commands = ref<CommandItem[]>([])
const dispatching = ref(false)

async function loadCommands() {
  if (!stationId.value) {
    commands.value = []
    return
  }
  loading.value = true
  try {
    commands.value = await api<CommandItem[]>(`/stations/${stationId.value}/commands`)
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '查询失败')
  } finally {
    loading.value = false
  }
}

async function dispatch() {
  if (!stationId.value) {
    ElMessage.warning('请输入采集站ID')
    return
  }
  dispatching.value = true
  try {
    await api<{ commandId: number }>(`/stations/${stationId.value}/commands`, {
      method: 'POST',
      body: JSON.stringify({ type: Number(cmdType.value) })
    })
    ElMessage.success('指令已下发')
    loadCommands()
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '下发失败')
  } finally {
    dispatching.value = false
  }
}

onMounted(loadCommands)
</script>

<template>
  <div>
    <el-form inline>
      <el-form-item>
        <el-input v-model="stationId" placeholder="采集站ID" clearable style="width: 160px" @blur="loadCommands()" />
      </el-form-item>
      <el-form-item>
        <el-select v-model="cmdType" style="width: 140px">
          <el-option v-for="(t, i) in CMD_TYPES" :key="i" :label="t" :value="String(i)" />
        </el-select>
      </el-form-item>
      <el-form-item>
        <el-button type="primary" :loading="dispatching" @click="dispatch">下发指令</el-button>
      </el-form-item>
    </el-form>

    <el-table v-loading="loading" :data="commands" border stripe>
      <el-table-column prop="commandId" label="指令ID" width="100" />
      <el-table-column label="类型" width="120">
        <template #default="{ row }">{{ CMD_TYPES[row.type] }}</template>
      </el-table-column>
      <el-table-column label="状态" width="100">
        <template #default="{ row }">
          <el-tag :type="row.status === 3 ? 'success' : row.status === 4 || row.status === 5 ? 'danger' : 'info'" size="small">
            {{ CMD_STATUS[row.status] }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="message" label="结果" min-width="220" show-overflow-tooltip />
      <el-table-column label="下发时间" width="160">
        <template #default="{ row }">{{ fmtTime(row.issuedAt) }}</template>
      </el-table-column>
    </el-table>
  </div>
</template>
