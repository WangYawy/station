import { ApiError } from '../api/client'

/** 下载导出文件（CSV/xlsx/PDF 由 URL 的 format 参数决定），文件名取自 Content-Disposition。 */
export async function downloadFile(path: string): Promise<void> {
  const resp = await fetch('/api/v1' + path)
  if (resp.status === 401) {
    location.hash = '#/login'
    throw new ApiError(401, '未登录或会话已过期')
  }
  if (!resp.ok) {
    throw new ApiError(resp.status, '导出失败（无权限或数据异常）')
  }
  const blob = await resp.blob()
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  const cd = resp.headers.get('Content-Disposition') || ''
  const star = cd.match(/filename\*=UTF-8''([^;]+)/i)
  const plain = cd.match(/filename="?([^";]+)"?/i)
  a.download = star ? decodeURIComponent(star[1]) : plain ? plain[1] : 'export.csv'
  a.click()
  URL.revokeObjectURL(url)
}

/** 兼容旧调用：下载 CSV（UTF-8 BOM，Excel 可直接打开）。 */
export function downloadCsv(path: string): Promise<void> {
  return downloadFile(path)
}

/** 下载本地生成的 CSV 文本（UTF-8 BOM）。 */
export function downloadText(filename: string, content: string): void {
  const blob = new Blob(['\uFEFF' + content], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  a.click()
  URL.revokeObjectURL(url)
}
