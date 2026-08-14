<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import * as echarts from 'echarts/core'
import { BarChart } from 'echarts/charts'
import { GridComponent, TooltipComponent } from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'
import { api, ApiError } from '../api/client'
import type {
  AlertItem,
  CommandItem,
  ConfigChangeItem,
  DeptItem,
  FileItem,
  PagedResult,
  RecorderItem,
  StationDetail,
  TrendPoint
} from '../api/types'
import { ALERT_LEVELS, ALERT_STATUS, CMD_STATUS, CMD_TYPES, LICENSE_STATUS, fmtSize, fmtTime, levelTagType } from '../utils/format'
import { onRealtimeEvent } from '../utils/realtime'
import { useAuthStore } from '../stores/auth'

echarts.use([BarChart, GridComponent, TooltipComponent, CanvasRenderer])

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const stationId = Number(route.params.id)

const loading = ref(false)
const detail = ref<StationDetail | null>(null)
const alerts = ref<AlertItem[]>([])
const files = ref<FileItem[]>([])
const recorders = ref<RecorderItem[]>([])
const commands = ref<CommandItem[]>([])
const configs = ref<ConfigChangeItem[]>([])
const depts = ref<DeptItem[]>([])
const activeTab = ref('info')

const chartEl = ref<HTMLDivElement | null>(null)
let chart: echarts.ECharts | null = null

const canManageStation = computed(() => auth.hasPermission('station:manage'))
const canManageRecorder = computed(() => auth.hasPermission('recorder:manage'))
const canManageUser = computed(() => auth.hasPermission('user:manage'))

// ---------- 采集/存储策略表单（下发用） ----------
const collectPolicy = ref({ autoCollectOnConnect: true, eraseAfterComplete: false, skipCollected: true })
const storagePolicy = ref({ circuitBreakerThreshold: 5, retryCount: 3 })
const publishing = ref(false)

// ---------- 归属部门 ----------
const deptId = ref<number | null>(null)
const deptAssigning = ref(false)

// ---------- 指令 ----------
const dispatching = ref<string | null>(null)

const commandButtons = [
  { type: 0, label: '重启服务', danger: true },
  { type: 1, label: '清理缓存' },
  { type: 2, label: '同步配置' },
  { type: 3, label: '一键自检' },
  { type: 4, label: '停止采集', warning: true },
  { type: 5, label: '开始采集', primary: true }
]

function statusText(status: number): { text: string; color: string } {
  if (status === 0) return { text: '正常', color: '#22c55e' }
  if (status === 1) return { text: '维修', color: '#f59e0b' }
  return { text: '报废', color: '#94a3b8' }
}

async function loadAll() {
  loading.value = true
  try {
    const [d, alertData, fileData, recorderData, commandData, configData, trend, deptData] = await Promise.all([
      api<StationDetail>(`/stations/${stationId}`),
      api<PagedResult<AlertItem>>(`/alerts?stationId=${stationId}&size=5`),
      api<PagedResult<FileItem>>(`/files?stationId=${stationId}&page=1&size=5`),
      api<PagedResult<RecorderItem>>(`/recorders?stationId=${stationId}&page=1&size=100`),
      api<CommandItem[]>(`/stations/${stationId}/commands`),
      api<ConfigChangeItem[]>(`/stations/${stationId}/configs`).catch(() => [] as ConfigChangeItem[]),
      api<TrendPoint[]>(`/stats/collection-trend?stationId=${stationId}&days=14`),
      auth.hasPermission('dept:view') ? api<DeptItem[]>('/depts').catch(() => [] as DeptItem[]) : Promise.resolve([] as DeptItem[])
    ])
    detail.value = d
    alerts.value = alertData.items
    files.value = fileData.items
    recorders.value = recorderData.items
    commands.value = commandData
    configs.value = configData
    depts.value = deptData
    deptId.value = d.deptId
    renderTrend(trend)

    // 用最近一次下发配置预填表单（存在时）
    const collect = [...configData].reverse().find((c) => c.entityType === 'CollectPolicy')
    if (collect) {
      try {
        const payload = JSON.parse(collect.payloadJson)
        if (typeof payload.autoCollectOnConnect === 'boolean') collectPolicy.value.autoCollectOnConnect = payload.autoCollectOnConnect
        if (typeof payload.eraseAfterComplete === 'boolean') collectPolicy.value.eraseAfterComplete = payload.eraseAfterComplete
        if (typeof payload.skipCollected === 'boolean') collectPolicy.value.skipCollected = payload.skipCollected
      } catch {
        // 忽略非法载荷
      }
    }
    const storage = [...configData].reverse().find((c) => c.entityType === 'StoragePolicy')
    if (storage) {
      try {
        const payload = JSON.parse(storage.payloadJson)
        if (typeof payload.circuitBreakerThreshold === 'number') storagePolicy.value.circuitBreakerThreshold = payload.circuitBreakerThreshold
        if (typeof payload.retryCount === 'number') storagePolicy.value.retryCount = payload.retryCount
      } catch {
        // 忽略非法载荷
      }
    }
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '加载失败')
  } finally {
    loading.value = false
  }
}

function renderTrend(points: TrendPoint[]) {
  if (!chart) return
  chart.setOption({
    tooltip: { trigger: 'axis' },
    grid: { left: 48, right: 16, top: 28, bottom: 28 },
    xAxis: { type: 'category', data: points.map((p) => p.date.slice(5, 10)) },
    yAxis: { type: 'value', minInterval: 1 },
    series: [
      {
        name: '采集文件数',
        type: 'bar',
        barMaxWidth: 26,
        data: points.map((p) => p.fileCount),
        itemStyle: { color: '#2563eb' }
      }
    ]
  })
}

function onResize() {
  chart?.resize()
}

// ---------- 指令 ----------
async function dispatchCommand(type: number) {
  dispatching.value = String(type)
  try {
    await api<{ commandId: number }>(`/stations/${stationId}/commands`, {
      method: 'POST',
      body: JSON.stringify({ type })
    })
    ElMessage.success('指令已下发，等待采集站执行')
    await loadCommands()
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '下发失败')
  } finally {
    dispatching.value = null
  }
}

async function loadCommands() {
  try {
    commands.value = await api<CommandItem[]>(`/stations/${stationId}/commands`)
  } catch {
    // 指令查询失败不阻塞页面
  }
}

// ---------- 策略下发 ----------
async function publishCollectPolicy() {
  publishing.value = true
  try {
    await api<number>(`/stations/${stationId}/configs`, {
      method: 'POST',
      body: JSON.stringify({ entityType: 'CollectPolicy', operation: 0, payloadJson: JSON.stringify(collectPolicy.value) })
    })
    ElMessage.success('采集策略已下发，采集站下次同步生效')
    configs.value = await api<ConfigChangeItem[]>(`/stations/${stationId}/configs`)
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '下发失败')
  } finally {
    publishing.value = false
  }
}

async function publishStoragePolicy() {
  publishing.value = true
  try {
    await api<number>(`/stations/${stationId}/configs`, {
      method: 'POST',
      body: JSON.stringify({ entityType: 'StoragePolicy', operation: 0, payloadJson: JSON.stringify(storagePolicy.value) })
    })
    ElMessage.success('存储配置已下发，采集站下次同步生效')
    configs.value = await api<ConfigChangeItem[]>(`/stations/${stationId}/configs`)
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '下发失败')
  } finally {
    publishing.value = false
  }
}

// ---------- 归属部门 ----------
async function assignDept() {
  deptAssigning.value = true
  try {
    await api<boolean>(`/stations/${stationId}/dept`, {
      method: 'PUT',
      body: JSON.stringify({ deptId: deptId.value })
    })
    ElMessage.success('归属部门已更新')
    await loadAll()
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '调整失败')
  } finally {
    deptAssigning.value = false
  }
}

// ---------- 用户域/白名单同步 ----------
async function syncDomain() {
  try {
    const published = await api<Array<{ entityType: string; version: number; rows: number }>>(
      `/stations/${stationId}/configs/sync-domain`,
      { method: 'POST' }
    )
    ElMessage.success(`用户域快照已下发：${published.length} 类配置`)
    configs.value = await api<ConfigChangeItem[]>(`/stations/${stationId}/configs`)
    await loadAll()
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '下发失败')
  }
}

// ---------- 白名单 ----------
async function toggleWhitelist(row: RecorderItem) {
  try {
    await api<boolean>(`/recorders/${row.id}/whitelist`, {
      method: 'PUT',
      body: JSON.stringify({ isWhitelisted: !row.isWhitelisted })
    })
    ElMessage.success(row.isWhitelisted ? '已移出白名单' : '已加入白名单')
    await loadAll()
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '操作失败')
  }
}

function goBack() {
  router.push('/stations')
}

const pendingAlerts = computed(() => detail.value?.pendingAlertCount ?? 0)

let offRealtime: (() => void) | null = null
onMounted(() => {
  chart = echarts.init(chartEl.value!)
  window.addEventListener('resize', onResize)
  loadAll()
  const offs = ['station.registered', 'station.status', 'station.license', 'file.reported', 'alert.created', 'command.result'].map((t) =>
    onRealtimeEvent(t, loadAll))
  offRealtime = () => offs.forEach((off) => off())
})

onBeforeUnmount(() => {
  window.removeEventListener('resize', onResize)
  chart?.dispose()
  chart = null
  offRealtime?.()
})
</script>

<template>
  <div v-loading="loading">
    <!-- 顶部：返回 + 站信息 + 操作 -->
    <div class="detail-header">
      <div class="header-left">
        <el-button text @click="goBack">← 返回采集站列表</el-button>
        <div class="station-title">
          <h2>{{ detail?.stationCode ?? '-' }}</h2>
          <el-tag :type="detail?.isOnline ? 'success' : 'info'" size="small">
            {{ detail?.isOnline ? '● 在线' : '○ 离线' }}
          </el-tag>
          <el-tag :type="detail?.operationalStatus === 0 ? 'success' : detail?.operationalStatus === 1 ? 'warning' : 'info'" size="small">
            {{ statusText(detail?.operationalStatus ?? 0).text }}
          </el-tag>
        </div>
        <div class="station-meta">
          <span>{{ detail?.deptName ?? '未归属部门' }}</span>
          <span>版本 {{ detail?.softwareVersion || '-' }}</span>
          <span>最后心跳 {{ fmtTime(detail?.lastHeartbeatAt) || '—' }}</span>
          <span>授权 {{ LICENSE_STATUS[detail?.licenseStatus ?? 0] }} · 剩余 {{ detail?.licenseDaysLeft ?? 0 }} 天</span>
        </div>
      </div>
      <div class="header-actions">
        <el-button @click="loadAll()">刷新</el-button>
        <el-button v-if="canManageStation" type="danger" :loading="dispatching === '0'" @click="dispatchCommand(0)">重启服务</el-button>
      </div>
    </div>

    <!-- 统计概览 -->
    <el-row :gutter="12" class="stat-grid">
      <el-col :span="6">
        <el-card shadow="never">
          <div class="stat-label">文件总量</div>
          <div class="stat-num">{{ detail?.fileCount ?? 0 }}</div>
          <div class="stat-sub">{{ fmtSize(detail?.totalSize ?? 0) }}</div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card shadow="never">
          <div class="stat-label">今日采集</div>
          <div class="stat-num" style="color:#2563eb">{{ detail?.todayFileCount ?? 0 }}</div>
          <div class="stat-sub">{{ fmtSize(detail?.todaySize ?? 0) }}</div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card shadow="never">
          <div class="stat-label">待处理报警</div>
          <div class="stat-num" style="color:#f59e0b">{{ pendingAlerts }}</div>
          <div class="stat-sub">
            严重 {{ detail?.alertLevels.find((x) => x.key === '2')?.count ?? 0 }} · 警告 {{ detail?.alertLevels.find((x) => x.key === '1')?.count ?? 0 }}
          </div>
        </el-card>
      </el-col>
      <el-col :span="6">
        <el-card shadow="never">
          <div class="stat-label">关联记录仪</div>
          <div class="stat-num" style="color:#06b6d4">{{ detail?.recorderCount ?? 0 }}</div>
          <div class="stat-sub">白名单 {{ detail?.whitelistedRecorderCount ?? 0 }}</div>
        </el-card>
      </el-col>
    </el-row>

    <!-- Tabs -->
    <el-tabs v-model="activeTab" class="detail-tabs">
      <!-- ============ 设备信息 ============ -->
      <el-tab-pane label="设备信息" name="info">
        <el-row :gutter="12">
          <el-col :span="14">
            <el-card shadow="never">
              <template #header>📊 近 14 天采集趋势</template>
              <div ref="chartEl" style="height: 260px" />
            </el-card>
            <el-card shadow="never" style="margin-top:12px">
              <template #header>📱 关联记录仪（{{ recorders.length }}）</template>
              <div v-if="recorders.length" class="chips">
                <el-tag v-for="r in recorders.slice(0, 12)" :key="r.id" :type="r.isWhitelisted ? 'success' : 'info'" class="chip" size="large">
                  {{ r.recorderSerial }}{{ r.boundUserName ? ' · ' + r.boundUserName : '' }}
                </el-tag>
              </div>
              <el-empty v-else description="暂无关联记录仪" :image-size="60" />
              <el-button v-if="recorders.length" text type="primary" @click="activeTab = 'whitelist'">查看全部 →</el-button>
            </el-card>
          </el-col>
          <el-col :span="10">
            <el-card shadow="never">
              <template #header>📋 设备信息</template>
              <el-descriptions :column="1" border size="small">
                <el-descriptions-item label="设备编号">{{ detail?.stationCode }}</el-descriptions-item>
                <el-descriptions-item label="所属部门">{{ detail?.deptName ?? '—' }}</el-descriptions-item>
                <el-descriptions-item label="操作系统">{{ detail?.osVersion || '—' }}（{{ detail?.cpuArch || '—' }}）</el-descriptions-item>
                <el-descriptions-item label="软件版本">{{ detail?.softwareVersion || '—' }}（配置 v{{ detail?.configVersion ?? 0 }}）</el-descriptions-item>
                <el-descriptions-item label="USB 接口数">{{ detail?.usbPortCount ?? 0 }} 口</el-descriptions-item>
                <el-descriptions-item label="授权状态">
                  <span :style="{ color: (detail?.licenseStatus ?? 0) === 2 ? '#22c55e' : '#f59e0b' }">
                    {{ LICENSE_STATUS[detail?.licenseStatus ?? 0] }} · 剩余 {{ detail?.licenseDaysLeft ?? 0 }} 天
                  </span>
                </el-descriptions-item>
                <el-descriptions-item label="授权到期">{{ fmtTime(detail?.licenseExpiresAt) || '—' }}</el-descriptions-item>
                <el-descriptions-item label="注册时间">{{ fmtTime(detail?.registeredAt) }}</el-descriptions-item>
                <el-descriptions-item label="最后心跳">{{ fmtTime(detail?.lastHeartbeatAt) || '—' }}</el-descriptions-item>
                <el-descriptions-item label="站内 Web 地址">{{ detail?.stationBaseUrl || '—' }}</el-descriptions-item>
                <el-descriptions-item label="CPU 序列号">{{ detail?.cpuSerial || '—' }}</el-descriptions-item>
                <el-descriptions-item label="主板序列号">{{ detail?.motherboardSerial || '—' }}</el-descriptions-item>
                <el-descriptions-item label="磁盘序列号">{{ detail?.diskSerial || '—' }}</el-descriptions-item>
                <el-descriptions-item label="MAC 地址">{{ detail?.macAddress || '—' }}</el-descriptions-item>
              </el-descriptions>
            </el-card>
            <el-card shadow="never" style="margin-top:12px">
              <template #header>📡 远程指令下发</template>
              <div class="cmd-grid">
                <el-button v-for="b in commandButtons" :key="b.type" :type="b.danger ? 'danger' : b.warning ? 'warning' : b.primary ? 'primary' : 'default'"
                           :plain="!b.danger && !b.warning && !b.primary"
                           :loading="dispatching === String(b.type)"
                           :disabled="!canManageStation"
                           @click="dispatchCommand(b.type)">
                  {{ b.label }}
                </el-button>
              </div>
              <div class="hint">指令需采集站在线时生效，带 SM2 数字签名验证；执行结果回执实时显示</div>
              <el-table v-if="commands.length" :data="commands.slice(0, 5)" size="small" style="margin-top:8px">
                <el-table-column label="类型" width="100">
                  <template #default="{ row }">{{ CMD_TYPES[row.type] }}</template>
                </el-table-column>
                <el-table-column label="状态" width="80">
                  <template #default="{ row }">
                    <el-tag :type="row.status === 3 ? 'success' : row.status === 4 || row.status === 5 ? 'danger' : 'info'" size="small">
                      {{ CMD_STATUS[row.status] }}
                    </el-tag>
                  </template>
                </el-table-column>
                <el-table-column prop="message" label="回执" min-width="150" show-overflow-tooltip />
              </el-table>
            </el-card>
          </el-col>
        </el-row>

        <el-row :gutter="12" style="margin-top:12px">
          <el-col :span="12">
            <el-card shadow="never">
              <template #header>⚠️ 最近报警（{{ alerts.length }}）</template>
              <el-table v-if="alerts.length" :data="alerts" size="small">
                <el-table-column label="级别" width="70">
                  <template #default="{ row }">
                    <el-tag :type="levelTagType(row.level)" size="small">{{ ALERT_LEVELS[row.level] }}</el-tag>
                  </template>
                </el-table-column>
                <el-table-column prop="message" label="内容" min-width="200" show-overflow-tooltip />
                <el-table-column label="状态" width="80">
                  <template #default="{ row }">{{ ALERT_STATUS[row.status] }}</template>
                </el-table-column>
                <el-table-column label="时间" width="150">
                  <template #default="{ row }">{{ fmtTime(row.occurredAt) }}</template>
                </el-table-column>
              </el-table>
              <el-empty v-else description="暂无报警" :image-size="60" />
              <el-button text type="primary" @click="router.push('/alerts')">查看全部报警 →</el-button>
            </el-card>
          </el-col>
          <el-col :span="12">
            <el-card shadow="never">
              <template #header>📁 最近文件（{{ files.length }}）</template>
              <el-table v-if="files.length" :data="files" size="small">
                <el-table-column prop="fileName" label="文件名" min-width="180" show-overflow-tooltip />
                <el-table-column label="大小" width="90">
                  <template #default="{ row }">{{ fmtSize(row.size) }}</template>
                </el-table-column>
                <el-table-column label="采集时间" width="150">
                  <template #default="{ row }">{{ fmtTime(row.collectedAt) }}</template>
                </el-table-column>
              </el-table>
              <el-empty v-else description="暂无文件" :image-size="60" />
              <el-button text type="primary" @click="router.push('/files')">查看全部文件 →</el-button>
            </el-card>
          </el-col>
        </el-row>
      </el-tab-pane>

      <!-- ============ 策略配置 ============ -->
      <el-tab-pane label="策略配置" name="policy">
        <el-row :gutter="12">
          <el-col :span="12">
            <el-card shadow="never">
              <template #header>📡 采集策略（下发至采集站热加载）</template>
              <el-form label-width="140px" label-position="left">
                <el-form-item label="接入自动采集">
                  <el-switch v-model="collectPolicy.autoCollectOnConnect" :disabled="!canManageStation" />
                </el-form-item>
                <el-form-item label="采集后擦除">
                  <el-switch v-model="collectPolicy.eraseAfterComplete" :disabled="!canManageStation" />
                </el-form-item>
                <el-form-item label="跳过已采集">
                  <el-switch v-model="collectPolicy.skipCollected" :disabled="!canManageStation" />
                </el-form-item>
              </el-form>
              <el-button v-if="canManageStation" type="primary" :loading="publishing" @click="publishCollectPolicy">下发采集策略</el-button>
              <div class="hint" style="margin-top:8px">配置下发后采集站下次轮询同步时热加载生效，无需重启</div>
            </el-card>
          </el-col>
          <el-col :span="12">
            <el-card shadow="never">
              <template #header>🛠️ 设备自检</template>
              <p class="hint">自检（USB 端口、磁盘读写、网络、存储目标）由采集站本地执行，报告由采集站生成。</p>
              <el-button v-if="canManageStation" type="primary" plain :loading="dispatching === '3'" @click="dispatchCommand(3)">
                下发一键自检指令
              </el-button>
              <p class="hint" style="margin-top:8px">执行结果将作为指令回执显示在"设备信息 → 远程指令"列表中。</p>
            </el-card>
            <el-card shadow="never" style="margin-top:12px">
              <template #header>📦 配置下发记录（v{{ configs.length }}）</template>
              <el-table :data="configs.slice(-8).reverse()" size="small">
                <el-table-column prop="version" label="版本" width="70" />
                <el-table-column prop="entityType" label="类型" width="130" />
                <el-table-column prop="payloadJson" label="内容" min-width="220" show-overflow-tooltip />
              </el-table>
              <el-empty v-if="!configs.length" description="暂无下发记录" :image-size="60" />
            </el-card>
          </el-col>
        </el-row>
      </el-tab-pane>

      <!-- ============ 操作授权 ============ -->
      <el-tab-pane label="操作授权" name="auth">
        <el-row :gutter="12">
          <el-col :span="12">
            <el-card shadow="never">
              <template #header>🏢 归属部门</template>
              <p class="hint" style="margin-bottom:10px">
                数据权限按组织树隔离：上级部门可查看所有下级数据，同级/下级不可越级访问；历史文件与报警保留上报时归属快照。
              </p>
              <el-form label-width="90px" label-position="left">
                <el-form-item label="归属部门">
                  <el-select v-model="deptId" clearable placeholder="选择部门" :disabled="!canManageStation" style="width:240px">
                    <el-option v-for="d in depts" :key="d.id" :label="d.name" :value="d.id" />
                  </el-select>
                </el-form-item>
              </el-form>
              <el-button v-if="canManageStation" type="primary" :loading="deptAssigning" @click="assignDept">保存归属</el-button>
            </el-card>
          </el-col>
          <el-col :span="12">
            <el-card shadow="never">
              <template #header>🔄 用户域/白名单同步</template>
              <p class="hint" style="margin-bottom:10px">
                组织、用户、账号、角色与记录仪白名单以平台为准：点击下发后采集站本地全量同步（本地只读，缺失项自动软停用）。
              </p>
              <el-button v-if="canManageUser" type="primary" :loading="publishing" @click="syncDomain">下发用户域配置</el-button>
              <el-alert v-if="!canManageUser" type="info" :closable="false" title="需要 user:manage 权限" style="max-width:360px" />
            </el-card>
          </el-col>
        </el-row>
      </el-tab-pane>

      <!-- ============ 记录仪白名单 ============ -->
      <el-tab-pane label="记录仪白名单" name="whitelist">
        <el-card shadow="never">
          <template #header>
            <div class="card-header">
              <span>记录仪白名单（{{ recorders.length }}）</span>
              <div>
                <el-button v-if="canManageUser" size="small" type="primary" plain :loading="publishing" @click="syncDomain">同步白名单至采集站</el-button>
                <el-button size="small" @click="router.push('/recorders')">记录仪管理</el-button>
              </div>
            </div>
          </template>
          <el-table :data="recorders" border stripe>
            <el-table-column prop="recorderSerial" label="序列号" min-width="150" />
            <el-table-column label="协议" width="90">
              <template #default="{ row }">{{ ['UMS', 'MTP', '私有SDK'][row.protocol ?? 0] }}</template>
            </el-table-column>
            <el-table-column label="绑定用户" width="120">
              <template #default="{ row }">{{ row.boundUserName || '—' }}</template>
            </el-table-column>
            <el-table-column label="绑定部门" width="120">
              <template #default="{ row }">{{ row.boundDeptName || '—' }}</template>
            </el-table-column>
            <el-table-column prop="fileCount" label="文件数" width="90" />
            <el-table-column label="最后上报" width="150">
              <template #default="{ row }">{{ fmtTime(row.lastSeenAt) }}</template>
            </el-table-column>
            <el-table-column label="白名单" width="100">
              <template #default="{ row }">
                <el-tag :type="row.isWhitelisted ? 'success' : 'danger'" size="small">
                  {{ row.isWhitelisted ? '已授权' : '未授权' }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column v-if="canManageRecorder" label="操作" width="140" fixed="right">
              <template #default="{ row }">
                <el-button link :type="row.isWhitelisted ? 'danger' : 'success'" @click="toggleWhitelist(row as RecorderItem)">
                  {{ row.isWhitelisted ? '移出' : '加入' }}
                </el-button>
                <el-button link type="primary" @click="router.push('/recorders')">绑定</el-button>
              </template>
            </el-table-column>
          </el-table>
          <el-empty v-if="!recorders.length" description="该采集站暂无关联记录仪" :image-size="80" />
        </el-card>
      </el-tab-pane>

      <!-- ============ 存储管理 ============ -->
      <el-tab-pane label="存储管理" name="storage">
        <el-row :gutter="12">
          <el-col :span="14">
            <el-card shadow="never">
              <template #header>📊 存储位置分布（已上报文件）</template>
              <el-table v-if="detail?.storageUsage?.length" :data="detail.storageUsage" border stripe>
                <el-table-column prop="location" label="存储位置" min-width="220" show-overflow-tooltip />
                <el-table-column prop="fileCount" label="文件数" width="100" />
                <el-table-column label="容量" width="120">
                  <template #default="{ row }">{{ fmtSize(row.totalSize) }}</template>
                </el-table-column>
                <el-table-column label="占比" min-width="180">
                  <template #default="{ row }">
                    <el-progress :percentage="Math.round((row.totalSize / (detail?.totalSize || 1)) * 100)" :stroke-width="14" />
                  </template>
                </el-table-column>
              </el-table>
              <el-empty v-else description="暂无存储位置数据" :image-size="70" />
            </el-card>
          </el-col>
          <el-col :span="10">
            <el-card shadow="never">
              <template #header>⚙️ 存储配置（下发至采集站）</template>
              <el-form label-width="130px" label-position="left">
                <el-form-item label="熔断阈值">
                  <el-input-number v-model="storagePolicy.circuitBreakerThreshold" :disabled="!canManageStation" :min="1" style="width:120px" />
                  <span class="hint" style="margin-left:8px">连续失败次数</span>
                </el-form-item>
                <el-form-item label="上传重试次数">
                  <el-input-number v-model="storagePolicy.retryCount" :disabled="!canManageStation" :min="0" :max="20" style="width:120px" />
                </el-form-item>
              </el-form>
              <el-button v-if="canManageStation" type="primary" :loading="publishing" @click="publishStoragePolicy">下发存储配置</el-button>
              <div class="hint" style="margin-top:8px">视频/日志保留天数、清理时间等本地策略请在采集站"系统设置"中维护</div>
            </el-card>
          </el-col>
        </el-row>
      </el-tab-pane>
    </el-tabs>
  </div>
</template>

<style scoped>
.detail-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 12px;
}
.header-left { display: flex; flex-direction: column; gap: 6px; }
.station-title { display: flex; align-items: center; gap: 10px; }
.station-title h2 { margin: 0; color: #0a2f6c; }
.station-meta { display: flex; gap: 16px; color: #64748b; font-size: 13px; flex-wrap: wrap; }
.header-actions { display: flex; gap: 8px; }
.stat-grid { margin-bottom: 4px; }
.stat-grid .el-card { text-align: center; }
.stat-label { color: #64748b; font-size: 13px; }
.stat-num { font-size: 28px; font-weight: 700; color: #1e293b; margin: 4px 0; }
.stat-sub { color: #94a3b8; font-size: 12px; }
.detail-tabs { background: #fff; border: 1px solid #e2e8f0; border-radius: 8px; padding: 8px 16px 16px; }
.chips { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 8px; }
.chip { max-width: 240px; }
.cmd-grid { display: flex; flex-wrap: wrap; gap: 8px; }
.hint { color: #94a3b8; font-size: 12px; line-height: 1.6; }
.card-header { display: flex; justify-content: space-between; align-items: center; }
</style>
