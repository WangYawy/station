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
  isActive: boolean
}

export interface UserItem {
  id: number
  userNo: string
  name: string
  deptId: number
  isActive: boolean
}

export interface RoleItem {
  id: number
  code: string
  name: string
  dataScope: number
  isSystem: boolean
  isActive: boolean
}

export interface PermissionItem {
  id: number
  code: string
  name: string
  module: string
}

export interface RecorderItem {
  id: number
  serialNumber: string
  model: string
  protocol: number
  boundUserId: number | null
  deptId: number | null
  isAuthorized: boolean
  isActive: boolean
}

export interface FileItem {
  id: number
  fileNo: string | null
  fileName: string
  extension: string
  size: number
  status: number
  syncStatus: number
  collectedAt: string | null
  originalModifiedAt: string | null
  sm3: string | null
  recorder: string
  operatorUserId: number | null
  deptName: string | null
  remotePath: string | null
  errorMessage: string | null
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

export interface AlertItem {
  id: number
  type: number
  level: number
  status: number
  title: string
  detail: string | null
  source: string | null
  createdAt: string
}

export interface ImportErrorItem {
  line: number
  message: string
}

export interface ImportResult {
  total: number
  success: number
  failed: number
  errors: ImportErrorItem[]
}
