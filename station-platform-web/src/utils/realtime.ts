import * as signalR from '@microsoft/signalr'

/** 平台实时推送：SignalR 连接单例，事件按类型分发给订阅者；载荷仅含类型/归属，数据按权限自行刷新。 */

export interface StationRealtimeEvent {
  type: string
  stationId: number | null
  deptId: number | null
  occurredAt: string
}

let connection: signalR.HubConnection | null = null
let started = false
const handlers = new Map<string, Set<(e: StationRealtimeEvent) => void>>()

export function connectRealtime(): void {
  if (connection) return
  connection = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/stations', { withCredentials: true })
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Warning)
    .build()
  connection.on('station.event', (e: StationRealtimeEvent) => {
    handlers.get(e.type)?.forEach((h) => h(e))
  })
  startRealtime()
}

function startRealtime(): void {
  if (!connection || started) return
  started = true
  connection.start().catch(() => {
    started = false
    // 失败时由 withAutomaticReconnect 自动重连；无需用户干预
  })
}

export function onRealtimeEvent(type: string, handler: (e: StationRealtimeEvent) => void): () => void {
  let set = handlers.get(type)
  if (!set) {
    set = new Set()
    handlers.set(type, set)
  }
  set.add(handler)
  return () => {
    set?.delete(handler)
  }
}

export function disconnectRealtime(): void {
  handlers.clear()
  const conn = connection
  connection = null
  started = false
  if (conn) {
    void conn.stop()
  }
}
