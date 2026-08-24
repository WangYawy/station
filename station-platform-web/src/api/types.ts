/**
 * 公共类型（ApiResponse/PagedResult/AuthSession/PermissionItem/AuditLogItem/
 * ImportResult 等）来自 @station/shared；本文件只保留平台端产品特有类型。
 * 新增与桌面端一致的类型时优先下沉到共享包。
 */
export * from '@station/shared'

export interface DeptItem {
  id: number
  code: string
  name: string
  parentId: number | null
  sortOrder: number
}

export interface FileItem {
  stationId: number
  localFileId: number
  fileNo: string
  fileName: string
  size: number
  kind: number
  sm3: string
  collectedAt: string
  recorderSerial: string
  userNo: string | null
  deptCode: string | null
  storageLocation: string | null
}

export interface AlertItem {
  id: number
  stationId: number
  deptId: number | null
  type: number
  level: number
  status: number
  source: string
  message: string
  occurredAt: string
  receivedAt: string
}

export interface StationItem {
  stationId: number
  stationCode: string
  osVersion: string
  cpuArch: string
  softwareVersion: string
  licenseStatus: number
  licenseExpiresAt: string | null
  licenseDaysLeft: number
  deptId: number | null
  registeredAt: string
  operationalStatus: number
}

export interface CommandItem {
  commandId: number
  type: number
  status: number
  message: string | null
  issuedAt: string | null
}

export interface OverviewStats {
  stationCount: number
  onlineCount: number
  offlineCount: number
  fileCount: number
  totalSize: number
  todayFileCount: number
  todaySize: number
  videoCount: number
  pendingAlertCount: number
  alertCount: number
}

export interface TrendPoint {
  date: string
  fileCount: number
  size: number
}

export interface StationStat {
  stationId: number
  stationCode: string
  deptId: number | null
  deptName: string | null
  fileCount: number
  totalSize: number
  alertCount: number
  lastCollectedAt: string | null
  isOnline: boolean
}

export interface StorageUsage {
  location: string
  fileCount: number
  totalSize: number
}

export interface EmergencyTaskItem {
  taskNo: string
  recorderName: string
  recorderSerial: string | null
  protocol: number
  progress: number
  startedAt: string | null
}

export interface StationDetail {
  stationId: number
  stationCode: string
  osVersion: string
  cpuArch: string
  softwareVersion: string
  operationalStatus: number
  licenseStatus: number
  licenseExpiresAt: string | null
  licenseDaysLeft: number
  deptId: number | null
  registeredAt: string
  deptName: string | null
  cpuSerial: string
  motherboardSerial: string
  diskSerial: string
  macAddress: string
  usbPortCount: number
  configVersion: number
  stationBaseUrl: string | null
  lastHeartbeatAt: string | null
  isOnline: boolean
  fileCount: number
  totalSize: number
  todayFileCount: number
  todaySize: number
  pendingAlertCount: number
  alertLevels: CountItem[]
  recorderCount: number
  whitelistedRecorderCount: number
  storageUsage: StorageUsage[]
  emergencyTaskCount: number
  emergencyTasks: EmergencyTaskItem[]
}

export interface ConfigChangeItem {
  entityType: string
  operation: number
  payloadJson: string
  version: number
}

export interface CountItem {
  key: string
  count: number
}

export interface AlertStats {
  byLevel: CountItem[]
  byStatus: CountItem[]
  byType: CountItem[]
}

export interface UserItem {
  id: number
  userNo: string
  name: string
  deptId: number
  deptName: string | null
  accountName: string | null
  isActive: boolean
  roles: string[]
}

export interface RoleItem {
  id: number
  code: string
  name: string
  dataScope: number
  isSystem: boolean
  isActive: boolean
  permissions: string[]
}

export interface RecorderItem {
  id: number
  recorderSerial: string
  lastStationId: number | null
  deptId: number | null
  protocol: number | null
  firstSeenAt: string
  lastSeenAt: string
  fileCount: number
  totalSize: number
  lastFileAt: string | null
  boundUserNo: string | null
  boundUserName: string | null
  boundDeptCode: string | null
  boundDeptName: string | null
  boundAt: string | null
  isWhitelisted: boolean
  isActive: boolean
  updatedAt: string
  lifecycleWarnings: string[]
}

export interface RecorderTrail {
  recorderSerial: string
  byDay: Array<{ date: string; fileCount: number; size: number }>
  byStation: Array<{
    stationId: number
    stationCode: string
    fileCount: number
    totalSize: number
    firstSeenAt: string
    lastSeenAt: string
  }>
}

export interface BackupFileItem {
  fileName: string
  fullPath: string
  size: number
  createdAt: string
}
