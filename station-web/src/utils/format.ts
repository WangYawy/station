/**
 * 公共格式化常量/函数（ALERT_LEVELS、fmtSize、fmtTime 等）来自 @station/shared；
 * 本文件只保留桌面端产品特有映射。
 */
export * from '@station/shared'

export const FILE_STATUS = ['待采集', '采集中', '校验中', '已完成', '跳过', '失败', '异常', '已取消']
export const UPLOAD_STATUS = ['待上传', '上传中', '已上传', '失败']
