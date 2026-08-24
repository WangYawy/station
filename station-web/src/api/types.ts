/**
 * 公共类型（ApiResponse/PagedResult/AuthSession/PermissionItem/AuditLogItem/
 * ImportResult 等）来自 @station/shared；本文件只保留桌面端产品特有类型。
 * 新增与平台端一致的类型时优先下沉到共享包。
 */
export * from '@station/shared'

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
