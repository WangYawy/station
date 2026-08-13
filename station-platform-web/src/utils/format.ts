export const FILE_KINDS = ['视频', '音频', '图片', '其他']
export const ALERT_LEVELS = ['提示', '警告', '严重']
export const ALERT_STATUS = ['待处理', '已确认', '已处理', '已关闭']
export const ALERT_TYPES = ['磁盘不足', '网络中断', 'USB故障', '校验失败', '非授权接入', '绑定异常', '存储不可达', '授权到期']
export const LICENSE_STATUS = ['试用', '宽限', '已激活', '锁定']
export const CMD_TYPES = ['重启服务', '清理缓存', '重拉配置', '执行自检', '停止采集', '启动采集']
export const CMD_STATUS = ['待下发', '已拉取', '执行中', '成功', '失败', '超时']

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

export function levelTagType(level: number): 'info' | 'warning' | 'danger' {
  return level === 0 ? 'info' : level === 1 ? 'warning' : 'danger'
}
