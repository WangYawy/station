import type { ApiResponse, AuthSession } from './types'

export class ApiError extends Error {
  status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

const API_BASE = '/api/v1'

export async function api<T>(path: string, options: RequestInit = {}): Promise<T> {
  const resp = await fetch(API_BASE + path, {
    headers: { 'Content-Type': 'application/json' },
    ...options
  })
  if (resp.status === 401) {
    if (location.hash !== '#/login') {
      location.hash = '#/login'
    }
    throw new ApiError(401, '未登录或会话已过期')
  }
  const body = (await resp.json().catch(() => null)) as ApiResponse<T> | null
  if (!resp.ok || !body?.success) {
    throw new ApiError(resp.status, body?.message || `请求失败（${resp.status}）`)
  }
  return body.data
}

export async function apiRaw(path: string, options: RequestInit = {}): Promise<Response> {
  return fetch(API_BASE + path, {
    headers: { 'Content-Type': 'application/json' },
    ...options
  })
}

/** 提交 CSV 文本（text/csv），返回统一响应体。 */
export async function apiText<T>(path: string, body: string): Promise<T> {
  const resp = await fetch(API_BASE + path, {
    method: 'POST',
    headers: { 'Content-Type': 'text/plain; charset=utf-8' },
    body
  })
  if (resp.status === 401) {
    location.hash = '#/login'
    throw new ApiError(401, '未登录或会话已过期')
  }
  const json = (await resp.json().catch(() => null)) as ApiResponse<T> | null
  if (!resp.ok || !json?.success) {
    throw new ApiError(resp.status, json?.message || `导入失败（${resp.status}）`)
  }
  return json.data
}

export async function login(userName: string, password: string): Promise<AuthSession> {
  const resp = await apiRaw('/auth/login', {
    method: 'POST',
    body: JSON.stringify({ userName, password })
  })
  if (resp.status === 401) {
    throw new ApiError(401, '用户名或密码错误')
  }
  if (!resp.ok) {
    throw new ApiError(resp.status, '登录失败')
  }
  const body = (await resp.json()) as { session: AuthSession }
  return body.session
}
