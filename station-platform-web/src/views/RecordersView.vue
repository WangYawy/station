<script setup lang="ts">
import { onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import * as echarts from 'echarts/core'
import { BarChart } from 'echarts/charts'
import { GridComponent, TooltipComponent } from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'
import { api, ApiError } from '../api/client'
import type { DeptItem, PagedResult, RecorderItem, RecorderTrail } from '../api/types'
import { fmtSize, fmtTime } from '../utils/format'
import { downloadCsv } from '../utils/export'
import { useAuthStore } from '../stores/auth'

echarts.use([BarChart, GridComponent, TooltipComponent, CanvasRenderer])

const auth = useAuthStore()
const loading = ref(false)
const items = ref<RecorderItem[]>([])
const total = ref(0)
const page = ref(1)
const size = 20
const depts = ref<DeptItem[]>([])
const query = reactive({ keyword: '', whitelisted: '', bound: '', warning: '' })
const bindVisible = ref(false)
const bindRow = ref<RecorderItem | null>(null)
const bindForm = reactive({ userNo: '', deptId: undefined as number | undefined })
const bindResult = ref('')
const trailVisible = ref(false)
const trail = ref<RecorderTrail | null>(null)
const trailChartEl = ref<HTMLDivElement | null>(null)
let trailChart: echarts.ECharts | null = null

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
    if (query.warning === '1') params.set('warning', 'true')
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
  if (query.warning === '1') params.set('warning', 'true')
  try {
    await downloadCsv(`/exports/recorders?${params.toString()}`)
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '导出失败')
  }
}

const WARNING_LABELS: Record<string, { text: string; type: 'warning' | 'danger' }> = {
  no_binding: { text: '未绑定', type: 'warning' },
  not_whitelisted: { text: '非白名单', type: 'warning' },
  idle: { text: '长期未使用', type: 'danger' }
}

async function openTrail(row: RecorderItem) {
  try {
    trail.value = await api<RecorderTrail>(`/recorders/${row.id}/trail?days=30`)
    trailVisible.value = true
    requestAnimationFrame(renderTrail)
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '加载失败')
  }
}

function renderTrail() {
  if (!trailChartEl.value || !trail.value) return
  if (!trailChart) {
    trailChart = echarts.init(trailChartEl.value)
  }
  trailChart.setOption({
    tooltip: { trigger: 'axis' },
    grid: { left: 48, right: 16, top: 24, bottom: 28 },
    xAxis: { type: 'category', data: trail.value.byDay.map((p) => p.date.slice(5, 10)) },
    yAxis: { type: 'value', minInterval: 1 },
    series: [
      {
        name: '文件数',
        type: 'bar',
        barMaxWidth: 22,
        data: trail.value.byDay.map((p) => p.fileCount),
        itemStyle: { color: '#0a2f6c' }
      }
    ]
  })
}

function onTrailClosed() {
  trailChart?.dispose()
  trailChart = null
}

onBeforeUnmount(() => {
  trailChart?.dispose()
  trailChart = null
})

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
        <el-select v-model="query.warning" placeholder="生命周期预警" clearable style="width: 130px">
          <el-option label="有预警" value="1" />
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
      <el-table-column label="生命周期预警" width="200">
        <template #default="{ row }">
          <template v-for="w in (row as RecorderItem).lifecycleWarnings" :key="w">
            <el-tag :type="WARNING_LABELS[w]?.type ?? 'info'" size="small" style="margin-right: 4px">{{ WARNING_LABELS[w]?.text ?? w }}</el-tag>
          </template>
        </template>
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
          <el-button link type="primary" @click="openTrail(row as RecorderItem)">轨迹</el-button>
          <el-button link type="primary" @click="openBind(row as RecorderItem)">绑定</el-button>
        </template>
      </el-table-column>
      <el-table-column v-else label="操作" width="90" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" @click="openTrail(row as RecorderItem)">轨迹</el-button>
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

    <el-dialog v-model="trailVisible" :title="`使用轨迹：${trail?.recorderSerial ?? ''}`" width="760px" destroy-on-close @closed="onTrailClosed">
      <div ref="trailChartEl" style="height: 260px" />
      <h4>按采集站聚合</h4>
      <el-table :data="trail?.byStation ?? []" border stripe size="small">
        <el-table-column prop="stationCode" label="采集站" width="130" />
        <el-table-column prop="fileCount" label="文件数" width="90" />
        <el-table-column label="容量" width="110">
          <template #default="{ row }">{{ fmtSize(row.totalSize) }}</template>
        </el-table-column>
        <el-table-column label="首次使用" min-width="150">
          <template #default="{ row }">{{ fmtTime(row.firstSeenAt) }}</template>
        </el-table-column>
        <el-table-column label="最近使用" min-width="150">
          <template #default="{ row }">{{ fmtTime(row.lastSeenAt) }}</template>
        </el-table-column>
      </el-table>
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
