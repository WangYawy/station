export interface ApiResponse<T> {
  success: boolean
  code: number
  message: string
  data: T
}

export interface PagedResult<T> {
  pageIndex: number
  pageSize: number
  totalCount: number
  items: T[]
}

export interface AuthSession {
  accountId: number
  userId: number | null
  userName: string
  userNo: string | null
  name: string | null
  deptId: number | null
  dataScope: number
  roles: string[]
  permissions: string[]
}

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

export interface PermissionItem {
  id: number
  code: string
  name: string
  module: string
}

export interface AuditLogItem {
  id: number
  operatorAccount: string | null
  operatorName: string | null
  deptId: number | null
  sourceIp: string | null
  operationType: string
  target: string | null
  detail: string | null
  result: number
  createdAt: string
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
}
