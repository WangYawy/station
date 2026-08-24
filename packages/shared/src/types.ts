/**
 * 两前端完全一致、且与后端 Station.Shared 契约对齐的公共类型。
 * 产品特有类型（如桌面端 FileItem/AlertItem、平台端 StationItem 等）保留在
 * 各自前端的 src/api/types.ts 中，不放入本包。
 */

/** 统一响应体 */
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
