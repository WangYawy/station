import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { api, apiRaw, login as apiLogin } from '../api/client'
import type { AuthSession } from '../api/types'

export const useAuthStore = defineStore('auth', () => {
  const session = ref<AuthSession | null>(null)
  const loggedIn = computed(() => session.value !== null)
  const userName = computed(() => session.value?.name || session.value?.userName || '')
  const roleText = computed(() => session.value?.roles.join('、') || '')

  function hasPermission(code: string): boolean {
    return session.value?.roles.includes('admin') || session.value?.permissions.includes(code) || false
  }

  async function login(userName: string, password: string) {
    session.value = await apiLogin(userName, password)
  }

  async function logout() {
    try {
      await api('/auth/logout', { method: 'POST' })
    } finally {
      session.value = null
    }
  }

  async function restore() {
    try {
      const resp = await apiRaw('/auth/me')
      if (resp.ok) {
        const body = (await resp.json()) as { session: AuthSession }
        session.value = body.session
      }
    } catch {
      session.value = null
    }
  }

  return { session, loggedIn, userName, roleText, hasPermission, login, logout, restore }
})
