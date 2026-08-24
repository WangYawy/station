/** 两前端共用的格式化常量与函数。产品特有映射（FILE_STATUS、CMD_STATUS 等）留在各自前端。 */

export const ALERT_LEVELS = ['提示', '警告', '严重']
export const ALERT_STATUS = ['待处理', '已确认', '已处理', '已关闭']
export const ALERT_TYPES = ['磁盘不足', '网络中断', 'USB故障', '校验失败', '非授权接入', '绑定异常', '存储不可达', '授权到期']
export const DATA_SCOPES = ['全部数据', '本部门及下级', '仅本部门', '仅本人']

export function fmtSize(bytes: number): string {
  if (!bytes) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  let v = bytes
  let i = 0
  while (v >= 1024 && i < units.length - 1) {
    v /= 1024
    i++
  }
  return `${v.toFixed(1)} ${units[i]}`
}

export function fmtTime(value?: string | null): string {
  if (!value) return ''
  return value.replace('T', ' ').slice(0, 19)
}
