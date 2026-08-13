<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import * as echarts from 'echarts/core'
import { BarChart } from 'echarts/charts'
import { GridComponent, TooltipComponent } from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'
import { api, ApiError } from '../api/client'
import type { AlertStats, CountItem, OverviewStats, PagedResult, StationStat, TrendPoint } from '../api/types'
import { ALERT_LEVELS, ALERT_STATUS, ALERT_TYPES, fmtSize, fmtTime } from '../utils/format'
import { downloadFile } from '../utils/export'

echarts.use([BarChart, GridComponent, TooltipComponent, CanvasRenderer])

const loading = ref(false)
const overview = ref<OverviewStats | null>(null)
const ranking = ref<StationStat[]>([])
const alertStats = ref<AlertStats | null>(null)
const chartEl = ref<HTMLDivElement | null>(null)
let chart: echarts.ECharts | null = null

function renderTrend(points: TrendPoint[]) {
  if (!chart) return
  chart.setOption({
    tooltip: { trigger: 'axis' },
    grid: { left: 48, right: 16, top: 28, bottom: 28 },
    xAxis: { type: 'category', data: points.map((p) => p.date.slice(0, 10)) },
    yAxis: { type: 'value', minInterval: 1 },
    series: [
      {
        name: '采集量',
        type: 'bar',
        barMaxWidth: 28,
        data: points.map((p) => p.fileCount),
        itemStyle: { color: '#0a2f6c' }
      }
    ]
  })
}

async function loadStats() {
  loading.value = true
  try {
    overview.value = await api<OverviewStats>('/stats/overview')
    const trend = await api<TrendPoint[]>('/stats/collection-trend?days=14')
    renderTrend(trend)
    const rank = await api<PagedResult<StationStat>>('/stats/stations?page=1&size=50')
    ranking.value = rank.items
    alertStats.value = await api<AlertStats>('/stats/alerts')
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '加载失败')
  } finally {
    loading.value = false
  }
}

function countOf(list: CountItem[], key: string): number {
  return list.find((x) => x.key === key)?.count ?? 0
}

function onResize() {
  chart?.resize()
}

async function doExport(kind: 'trend' | 'stations', format: string) {
  try {
    const base = kind === 'trend' ? '/exports/stats-trend?days=14' : '/exports/stats-stations'
    await downloadFile(`${base}&format=${format}`)
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '导出失败')
  }
}

onMounted(() => {
  chart = echarts.init(chartEl.value!)
  window.addEventListener('resize', onResize)
  loadStats()
})

onBeforeUnmount(() => {
  window.removeEventListener('resize', onResize)
  chart?.dispose()
  chart = null
})
</script>

<template>
  <div v-loading="loading">
    <el-row :gutter="12">
      <el-col :span="8">
        <el-card shadow="never">
          <div class="stat-num">{{ overview?.stationCount ?? '-' }}</div>
          <div class="stat-label">采集站（在线 {{ overview?.onlineCount ?? 0 }} / 离线 {{ overview?.offlineCount ?? 0 }}）</div>
        </el-card>
      </el-col>
      <el-col :span="8">
        <el-card shadow="never">
          <div class="stat-num">{{ overview?.fileCount ?? '-' }}</div>
          <div class="stat-label">文件总数（{{ fmtSize(overview?.totalSize ?? 0) }}）</div>
        </el-card>
      </el-col>
      <el-col :span="8">
        <el-card shadow="never">
          <div class="stat-num">{{ overview?.todayFileCount ?? '-' }}</div>
          <div class="stat-label">今日采集（{{ fmtSize(overview?.todaySize ?? 0) }}）</div>
        </el-card>
      </el-col>
      <el-col :span="8">
        <el-card shadow="never">
          <div class="stat-num">{{ overview?.videoCount ?? '-' }}</div>
          <div class="stat-label">视频文件</div>
        </el-card>
      </el-col>
      <el-col :span="8">
        <el-card shadow="never">
          <div class="stat-num">{{ overview?.pendingAlertCount ?? '-' }}</div>
          <div class="stat-label">待处理报警（共 {{ overview?.alertCount ?? 0 }}）</div>
        </el-card>
      </el-col>
      <el-col :span="8">
        <el-card shadow="never">
          <div class="stat-num">{{ overview ? `${overview.stationCount ? Math.round((overview.onlineCount / overview.stationCount) * 100) : 0}%` : '-' }}</div>
          <div class="stat-label">设备在线率</div>
        </el-card>
      </el-col>
    </el-row>

    <el-card shadow="never" class="block">
      <template #header>
        <span>采集趋势（近 14 天）</span>
        <el-dropdown style="float: right" @command="(f: string) => doExport('trend', f)">
          <el-button link type="primary">导出趋势</el-button>
          <template #dropdown>
            <el-dropdown-menu>
              <el-dropdown-item command="csv">CSV</el-dropdown-item>
              <el-dropdown-item command="xlsx">Excel(xlsx)</el-dropdown-item>
              <el-dropdown-item command="pdf">PDF</el-dropdown-item>
            </el-dropdown-menu>
          </template>
        </el-dropdown>
      </template>
      <div ref="chartEl" style="height: 300px" />
    </el-card>

    <el-row :gutter="12" class="block">
      <el-col :span="14">
        <el-card shadow="never">
          <template #header>
            <span>采集排行</span>
            <el-dropdown style="float: right" @command="(f: string) => doExport('stations', f)">
              <el-button link type="primary">导出排行</el-button>
              <template #dropdown>
                <el-dropdown-menu>
                  <el-dropdown-item command="csv">CSV</el-dropdown-item>
                  <el-dropdown-item command="xlsx">Excel(xlsx)</el-dropdown-item>
                  <el-dropdown-item command="pdf">PDF</el-dropdown-item>
                </el-dropdown-menu>
              </template>
            </el-dropdown>
          </template>
          <el-table :data="ranking" border stripe size="small">
            <el-table-column prop="stationCode" label="站" width="120" />
            <el-table-column prop="deptName" label="部门" width="110" />
            <el-table-column prop="fileCount" label="文件数" width="80" />
            <el-table-column label="容量" width="100">
              <template #default="{ row }">{{ fmtSize(row.totalSize) }}</template>
            </el-table-column>
            <el-table-column prop="alertCount" label="报警数" width="80" />
            <el-table-column label="最后采集" min-width="150">
              <template #default="{ row }">{{ fmtTime(row.lastCollectedAt) }}</template>
            </el-table-column>
            <el-table-column label="在线" width="70">
              <template #default="{ row }">
                <el-tag :type="row.isOnline ? 'success' : 'info'" size="small">{{ row.isOnline ? '在线' : '离线' }}</el-tag>
              </template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-col>
      <el-col :span="10">
        <el-card shadow="never">
          <template #header>报警统计</template>
          <el-table :data="alertStats?.byLevel ?? []" border stripe size="small">
            <el-table-column label="级别">
              <template #default="{ row }">{{ ALERT_LEVELS[Number(row.key)] }}</template>
            </el-table-column>
            <el-table-column prop="count" label="数量" width="90" />
          </el-table>
          <el-table :data="alertStats?.byStatus ?? []" border stripe size="small" class="sub-table">
            <el-table-column label="状态">
              <template #default="{ row }">{{ ALERT_STATUS[Number(row.key)] }}</template>
            </el-table-column>
            <el-table-column prop="count" label="数量" width="90" />
          </el-table>
          <el-table :data="alertStats?.byType ?? []" border stripe size="small" class="sub-table">
            <el-table-column label="类型">
              <template #default="{ row }">{{ ALERT_TYPES[Number(row.key)] }}</template>
            </el-table-column>
            <el-table-column prop="count" label="数量" width="90" />
          </el-table>
          <div style="font-size: 12px; color: #94a3b8">级别汇总：{{ alertStats ? `提示 ${countOf(alertStats.byLevel, '0')} / 警告 ${countOf(alertStats.byLevel, '1')} / 严重 ${countOf(alertStats.byLevel, '2')}` : '' }}</div>
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<style scoped>
.stat-num {
  font-size: 26px;
  font-weight: 700;
  color: #0a2f6c;
}
.stat-label {
  margin-top: 6px;
  font-size: 13px;
  color: #64748b;
}
.block {
  margin-top: 14px;
}
.sub-table {
  margin-top: 8px;
}
</style>
