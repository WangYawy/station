<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { api, apiText, ApiError } from '../api/client'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()

interface LicenseInfo {
  status: string
  message: string
  expiresAt: string | null
  daysLeft: number
}

interface SelfCheckItem {
  name: string
  ok: boolean
  detail: string
}

const loading = ref(false)
const readOnly = ref(false)
const activeGroup = ref('basic')
const canManage = () => auth.hasPermission('setting:manage')

const basic = reactive({ stationNo: '', installLocation: '', runMode: 'standalone', platformBaseUrl: '', platformStationCode: '' })
const storage = reactive({
  target: 'Local',
  localRoot: '',
  directoryTemplate: '',
  ftpHost: '',
  ftpPort: 21,
  ftpUser: '',
  ftpPassword: '',
  sftpHost: '',
  sftpPort: 22,
  sftpUser: '',
  sftpPassword: '',
  sftpRoot: '',
  circuitBreakerThreshold: 5,
  circuitBreakerCooldownSeconds: 60,
  videoRetentionDays: 90,
  logRetentionDays: 180,
  cleanupTime: '04:00'
})
const collect = reactive({ autoCollectOnConnect: true, eraseAfterComplete: false, skipCollected: true, collectAncillaryFiles: true })
const network = reactive({
  webPort: 5000,
  httpsPort: 5443,
  enableLan: false,
  enableHttps: false,
  allowHttp: false,
  certificateStatus: '未安装',
  maxFailedAttempts: 5,
  lockoutMinutes: 15
})
const license = reactive<LicenseInfo>({ status: '', message: '', expiresAt: null, daysLeft: 0 })
const licenseText = ref('')
const activating = ref(false)

const certFile = ref<File | null>(null)
const certPassword = ref('')
const certUploading = ref(false)

const selfChecking = ref(false)
const selfCheckItems = ref<SelfCheckItem[]>([])
const selfCheckTime = ref('')

async function loadSettings() {
  loading.value = true
  try {
    const data = await api<{
      basic: typeof basic
      storage: typeof storage
      collect: typeof collect
      license: LicenseInfo
      network: typeof network
      readOnly: boolean
    }>('/settings')
    Object.assign(basic, data.basic)
    Object.assign(storage, data.storage)
    Object.assign(collect, data.collect)
    Object.assign(network, data.network)
    Object.assign(license, data.license)
    readOnly.value = data.readOnly
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '加载设置失败')
  } finally {
    loading.value = false
  }
}

async function saveGroup(group: string, values: Record<string, unknown>) {
  try {
    const data = await api<{ hints: string[] }>(`/settings/${group}`, {
      method: 'PUT',
      body: JSON.stringify({ group, values })
    })
    if (data.hints?.length) {
      ElMessage.warning(data.hints.join('；'))
    } else {
      ElMessage.success('保存成功')
    }
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '保存失败')
  }
}

function saveBasic() {
  saveGroup('basic', { ...basic })
}

function saveStorage() {
  const values: Record<string, unknown> = { ...storage }
  if (!values.ftpPassword) delete values.ftpPassword
  if (!values.sftpPassword) delete values.sftpPassword
  saveGroup('storage', values)
}

function saveCollect() {
  saveGroup('collect', { ...collect })
}

function saveNetwork() {
  saveGroup('network', { ...network })
}

async function activateLicense() {
  if (!licenseText.value.trim()) {
    ElMessage.warning('请粘贴授权文件内容（内部工具生成的 station.lic JSON）')
    return
  }
  activating.value = true
  try {
    const data = await apiText<{ message: string }>('/settings/license/activate', licenseText.value)
    ElMessage.success(data.message)
    licenseText.value = ''
    await loadSettings()
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '激活失败')
  } finally {
    activating.value = false
  }
}

function onCertSelected(event: Event) {
  const input = event.target as HTMLInputElement
  certFile.value = input.files?.[0] ?? null
}

async function uploadCertificate() {
  if (!certFile.value) {
    ElMessage.warning('请选择 PFX 证书文件')
    return
  }
  certUploading.value = true
  try {
    const buffer = await certFile.value.arrayBuffer()
    const bytes = new Uint8Array(buffer)
    let binary = ''
    bytes.forEach((b) => (binary += String.fromCharCode(b)))
    const base64 = btoa(binary)
    const data = await api<{ message: string }>('/settings/certificate', {
      method: 'POST',
      body: JSON.stringify({ fileName: certFile.value.name, base64, password: certPassword.value })
    })
    ElMessage.success(data.message)
    certPassword.value = ''
    await loadSettings()
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '证书上传失败')
  } finally {
    certUploading.value = false
  }
}

async function runSelfCheck() {
  selfChecking.value = true
  try {
    selfCheckItems.value = await api<SelfCheckItem[]>('/settings/self-check', { method: 'POST' })
    selfCheckTime.value = new Date().toLocaleString()
  } catch (e) {
    ElMessage.error(e instanceof ApiError ? e.message : '自检失败')
  } finally {
    selfChecking.value = false
  }
}

async function downloadReport(format: string) {
  try {
    const resp = await fetch(`/api/v1/settings/self-check/report?format=${format}`)
    const blob = await resp.blob()
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `设备自检报告.${format === 'xlsx' ? 'xlsx' : format === 'pdf' ? 'pdf' : 'csv'}`
    a.click()
    URL.revokeObjectURL(url)
  } catch {
    ElMessage.error('报告下载失败')
  }
}

const disabled = () => readOnly.value || !canManage()

onMounted(loadSettings)
</script>

<template>
  <div v-loading="loading" class="settings-layout">
    <div class="settings-nav">
      <div class="nav-item" :class="{ active: activeGroup === 'basic' }" @click="activeGroup = 'basic'">基本设置</div>
      <div class="nav-item" :class="{ active: activeGroup === 'storage' }" @click="activeGroup = 'storage'">存储策略</div>
      <div class="nav-item" :class="{ active: activeGroup === 'license' }" @click="activeGroup = 'license'">授权与激活</div>
      <div class="nav-item" :class="{ active: activeGroup === 'collect' }" @click="activeGroup = 'collect'">采集策略</div>
      <div class="nav-item" :class="{ active: activeGroup === 'network' }" @click="activeGroup = 'network'">网络安全</div>
      <div class="nav-item" :class="{ active: activeGroup === 'selfcheck' }" @click="activeGroup = 'selfcheck'">设备自检</div>
    </div>

    <div class="settings-content">
      <el-alert v-if="readOnly" type="warning" :closable="false" show-icon
                title="平台版配置由平台统一下发，本地只读" style="margin-bottom:12px" />
      <el-alert v-if="!canManage()" type="info" :closable="false" show-icon
                title="当前账号仅可查看设置，修改需管理员或授权用户（setting:manage）" style="margin-bottom:12px" />

      <!-- 基本设置 -->
      <div v-show="activeGroup === 'basic'">
        <div class="group-title">📋 基本设置</div>
        <el-form label-width="110px" label-position="left">
          <el-form-item label="本机编号">
            <el-input v-model="basic.stationNo" readonly style="width:220px" />
          </el-form-item>
          <el-form-item label="安装位置">
            <el-input v-model="basic.installLocation" :disabled="disabled()" style="width:320px" placeholder="如：一楼大厅东侧" />
          </el-form-item>
          <el-form-item label="运行模式">
            <el-select v-model="basic.runMode" :disabled="disabled()" style="width:160px">
              <el-option label="单机版" value="standalone" />
              <el-option label="平台版" value="platform" />
            </el-select>
          </el-form-item>
          <el-form-item v-if="basic.runMode === 'platform'" label="平台地址">
            <el-input v-model="basic.platformBaseUrl" :disabled="disabled()" style="width:320px" placeholder="http://平台IP:端口" />
          </el-form-item>
          <el-form-item v-if="basic.runMode === 'platform'" label="平台站点编号">
            <el-input v-model="basic.platformStationCode" :disabled="disabled()" style="width:200px" />
          </el-form-item>
        </el-form>
        <el-button v-if="!readOnly" type="primary" :disabled="!canManage()" @click="saveBasic">保存基本设置</el-button>
      </div>

      <!-- 存储策略 -->
      <div v-show="activeGroup === 'storage'">
        <div class="group-title">💾 存储策略</div>
        <el-form label-width="150px" label-position="left">
          <el-form-item label="存储目标">
            <el-select v-model="storage.target" :disabled="disabled()" style="width:160px">
              <el-option label="本地磁盘" value="Local" />
              <el-option label="FTP" value="Ftp" />
              <el-option label="SFTP" value="Sftp" />
            </el-select>
          </el-form-item>
          <el-form-item v-if="storage.target === 'Local'" label="本地根目录">
            <el-input v-model="storage.localRoot" :disabled="disabled()" style="width:360px" />
          </el-form-item>
          <template v-if="storage.target !== 'Local'">
            <template v-if="storage.target === 'Ftp'">
              <el-form-item label="FTP 主机">
                <el-input v-model="storage.ftpHost" :disabled="disabled()" style="width:200px" />
                <span style="margin:0 8px">端口</span>
                <el-input-number v-model="storage.ftpPort" :disabled="disabled()" :min="1" :max="65535" style="width:110px" />
              </el-form-item>
              <el-form-item label="FTP 账号">
                <el-input v-model="storage.ftpUser" :disabled="disabled()" style="width:200px" />
              </el-form-item>
              <el-form-item label="FTP 密码">
                <el-input v-model="storage.ftpPassword" type="password" show-password
                          :disabled="disabled()" style="width:200px" placeholder="已设置（留空不修改）" />
              </el-form-item>
            </template>
            <template v-else>
              <el-form-item label="SFTP 主机">
                <el-input v-model="storage.sftpHost" :disabled="disabled()" style="width:200px" />
                <span style="margin:0 8px">端口</span>
                <el-input-number v-model="storage.sftpPort" :disabled="disabled()" :min="1" :max="65535" style="width:110px" />
              </el-form-item>
              <el-form-item label="SFTP 账号">
                <el-input v-model="storage.sftpUser" :disabled="disabled()" style="width:200px" />
              </el-form-item>
              <el-form-item label="SFTP 密码">
                <el-input v-model="storage.sftpPassword" type="password" show-password
                          :disabled="disabled()" style="width:200px" placeholder="已设置（留空不修改）" />
              </el-form-item>
            </template>
            <el-form-item v-if="storage.target === 'Sftp'" label="SFTP 根目录">
              <el-input v-model="storage.sftpRoot" :disabled="disabled()" style="width:220px" placeholder="留空使用用户主目录" />
            </el-form-item>
          </template>
          <el-form-item label="上传目录模板">
            <el-input v-model="storage.directoryTemplate" :disabled="disabled()" style="width:420px" />
          </el-form-item>
          <el-form-item label="熔断阈值">
            <el-input-number v-model="storage.circuitBreakerThreshold" :disabled="disabled()" :min="1" style="width:110px" />
            <span style="margin:0 8px">次 /</span>
            <el-input-number v-model="storage.circuitBreakerCooldownSeconds" :disabled="disabled()" :min="1" style="width:110px" />
            <span style="margin-left:8px">秒冷却</span>
          </el-form-item>
          <el-form-item label="保存天数">
            <el-input-number v-model="storage.videoRetentionDays" :disabled="disabled()" :min="1" style="width:110px" />
            <span style="margin:0 8px">天（视频）</span>
            <el-input-number v-model="storage.logRetentionDays" :disabled="disabled()" :min="1" style="width:110px" />
            <span style="margin-left:8px">天（日志）</span>
          </el-form-item>
          <el-form-item label="清理执行时间">
            <el-input type="time" v-model="storage.cleanupTime" :disabled="disabled()" style="width:120px" />
            <span class="hint">每日闲时执行</span>
          </el-form-item>
        </el-form>
        <el-button v-if="!readOnly" type="primary" :disabled="!canManage()" @click="saveStorage">保存存储策略</el-button>
        <span class="hint" style="margin-left:12px">存储目标/连接参数修改需重启后生效</span>
      </div>

      <!-- 授权与激活 -->
      <div v-show="activeGroup === 'license'">
        <div class="group-title">🔑 授权与激活</div>
        <el-descriptions :column="1" border style="max-width:520px; margin-bottom:12px">
          <el-descriptions-item label="授权状态">
            <el-tag :type="license.status === 'Activated' ? 'success' : license.status === 'Trial' ? 'warning' : 'danger'">
              {{ license.message }}
            </el-tag>
          </el-descriptions-item>
          <el-descriptions-item label="到期时间">
            {{ license.expiresAt ? new Date(license.expiresAt).toLocaleString() : '—' }}
          </el-descriptions-item>
          <el-descriptions-item label="剩余天数">{{ license.daysLeft }}</el-descriptions-item>
        </el-descriptions>
        <div v-if="!readOnly" style="max-width:520px">
          <el-input v-model="licenseText" type="textarea" :rows="6" placeholder="粘贴授权文件内容（station.lic JSON）" />
          <el-button type="primary" style="margin-top:8px" :loading="activating" :disabled="!canManage()" @click="activateLicense">激活授权</el-button>
        </div>
      </div>

      <!-- 采集策略 -->
      <div v-show="activeGroup === 'collect'">
        <div class="group-title">📡 采集策略</div>
        <el-form label-width="150px" label-position="left">
          <el-form-item label="接入自动采集">
            <el-switch v-model="collect.autoCollectOnConnect" :disabled="disabled()" />
          </el-form-item>
          <el-form-item label="采集后擦除">
            <el-switch v-model="collect.eraseAfterComplete" :disabled="disabled()" />
          </el-form-item>
          <el-form-item label="跳过已采集">
            <el-switch v-model="collect.skipCollected" :disabled="disabled()" />
          </el-form-item>
          <el-form-item label="采集附属文件">
            <el-switch v-model="collect.collectAncillaryFiles" :disabled="disabled()" />
            <span class="hint">日志、图片、音频等附属文件</span>
          </el-form-item>
        </el-form>
        <el-button v-if="!readOnly" type="primary" :disabled="!canManage()" @click="saveCollect">保存采集策略</el-button>
      </div>

      <!-- 网络安全 -->
      <div v-show="activeGroup === 'network'">
        <div class="group-title">🔐 网络安全</div>
        <el-form label-width="150px" label-position="left">
          <el-form-item label="Web 端口">
            <el-input-number v-model="network.webPort" :disabled="disabled()" :min="1" :max="65535" style="width:110px" />
            <span class="hint" style="margin-left:8px">重启后生效</span>
          </el-form-item>
          <el-form-item label="HTTPS 端口">
            <el-input-number v-model="network.httpsPort" :disabled="disabled()" :min="1" :max="65535" style="width:110px" />
          </el-form-item>
          <el-form-item label="开启局域网访问">
            <el-switch v-model="network.enableLan" :disabled="disabled()" />
            <span class="hint">开启后监听 0.0.0.0 并强制 HTTPS</span>
          </el-form-item>
          <el-form-item label="允许 HTTP">
            <el-switch v-model="network.allowHttp" :disabled="disabled()" />
            <span class="hint">关闭（推荐）后局域网仅 HTTPS</span>
          </el-form-item>
          <el-form-item label="HTTPS 证书">
            <el-tag :type="network.certificateStatus.includes('已安装') ? 'success' : 'info'">{{ network.certificateStatus }}</el-tag>
            <div v-if="!readOnly" style="margin-top:8px; display:flex; gap:8px; align-items:center">
              <input type="file" accept=".pfx,.p12" @change="onCertSelected" />
              <el-input v-model="certPassword" type="password" show-password placeholder="证书密码" style="width:160px" />
              <el-button type="primary" :disabled="!canManage() || !certFile" :loading="certUploading" @click="uploadCertificate">上传证书</el-button>
            </div>
          </el-form-item>
          <el-form-item label="登录锁定">
            <el-input-number v-model="network.maxFailedAttempts" :disabled="disabled()" :min="1" style="width:110px" />
            <span style="margin:0 8px">次 /</span>
            <el-input-number v-model="network.lockoutMinutes" :disabled="disabled()" :min="1" style="width:110px" />
            <span style="margin-left:8px">分钟</span>
          </el-form-item>
        </el-form>
        <el-button v-if="!readOnly" type="primary" :disabled="!canManage()" @click="saveNetwork">保存网络安全设置</el-button>
      </div>

      <!-- 设备自检 -->
      <div v-show="activeGroup === 'selfcheck'">
        <div class="group-title">🛠️ 设备自检</div>
        <div style="display:flex; gap:12px; align-items:center; margin-bottom:12px">
          <el-button type="primary" :loading="selfChecking" @click="runSelfCheck">一键自检</el-button>
          <span class="hint">检测 USB 端口、磁盘、网络、存储目标</span>
        </div>
        <el-table v-if="selfCheckItems.length" :data="selfCheckItems" border stripe style="max-width:720px">
          <el-table-column prop="name" label="检查项" width="140" />
          <el-table-column label="结果" width="90">
            <template #default="{ row }">
              <el-tag :type="row.ok ? 'success' : 'danger'" size="small">{{ row.ok ? '正常' : '异常' }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="detail" label="详情" min-width="300" show-overflow-tooltip />
        </el-table>
        <div v-if="selfCheckTime" style="margin-top:12px; display:flex; gap:12px; align-items:center">
          <span class="hint">自检时间：{{ selfCheckTime }}</span>
          <el-dropdown @command="downloadReport">
            <el-button>下载报告</el-button>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item command="pdf">PDF</el-dropdown-item>
                <el-dropdown-item command="xlsx">Excel(xlsx)</el-dropdown-item>
                <el-dropdown-item command="csv">CSV</el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.settings-layout {
  display: flex;
  gap: 16px;
  align-items: flex-start;
}
.settings-nav {
  width: 150px;
  background: #fff;
  border-radius: 8px;
  padding: 8px 0;
  border: 1px solid #e2e8f0;
  flex-shrink: 0;
}
.nav-item {
  padding: 10px 16px;
  cursor: pointer;
  color: #475569;
  font-size: 14px;
  border-left: 3px solid transparent;
}
.nav-item:hover { background: #f1f5f9; }
.nav-item.active { color: #2563eb; border-left-color: #2563eb; background: #eff6ff; font-weight: 600; }
.settings-content {
  flex: 1;
  background: #fff;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  padding: 16px 20px;
  min-width: 0;
}
.group-title { font-size: 16px; font-weight: 600; color: #0f172a; margin-bottom: 12px; }
.hint { color: #94a3b8; font-size: 12px; }
</style>
