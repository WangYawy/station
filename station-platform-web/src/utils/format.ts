/**
 * 公共格式化常量/函数（ALERT_LEVELS、fmtSize、fmtTime 等）来自 @station/shared；
 * 本文件只保留平台端产品特有映射。
 */
export * from '@station/shared'

export const FILE_KINDS = ['视频', '音频', '图片', '其他']
export const LICENSE_STATUS = ['试用', '宽限', '已激活', '锁定']
export const CMD_TYPES = ['重启服务', '清理缓存', '重拉配置', '执行自检', '停止采集', '启动采集', '写入绑定']
export const CMD_STATUS = ['待下发', '已拉取', '执行中', '成功', '失败', '超时']

export function levelTagType(level: number): 'info' | 'warning' | 'danger' {
  return level === 0 ? 'info' : level === 1 ? 'warning' : 'danger'
}
