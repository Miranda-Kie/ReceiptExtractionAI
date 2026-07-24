import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'

export type AuthUser = {
  authenticated: boolean
  username?: string
  role?: string
  isAdmin?: boolean
  isDemo?: boolean
}

type AuthContextValue = {
  user: AuthUser | null
  loading: boolean
  login: (username: string, password: string) => Promise<string | null>
  loginDemo: () => Promise<string | null>
  logout: () => Promise<void>
  refresh: () => Promise<void>
}

const AuthContext = createContext<AuthContextValue | null>(null)

async function fetchMe(): Promise<AuthUser> {
  const res = await fetch('/api/auth/me', { credentials: 'include' })
  return res.json()
}

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null)
  const [loading, setLoading] = useState(true)

  const refresh = useCallback(async () => {
    const me = await fetchMe()
    setUser(me)
  }, [])

  useEffect(() => {
    refresh()
      .catch(() => setUser({ authenticated: false }))
      .finally(() => setLoading(false))
  }, [refresh])

  const login = useCallback(async (username: string, password: string) => {
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
    })
    if (!res.ok) {
      const body = await res.json().catch(() => ({}))
      return (body.error as string) || 'Login failed.'
    }
    const me = await res.json()
    setUser(me)
    return null
  }, [])

  const loginDemo = useCallback(async () => {
    const res = await fetch('/api/auth/demo', {
      method: 'POST',
      credentials: 'include',
    })
    if (!res.ok) {
      const body = await res.json().catch(() => ({}))
      return (body.error as string) || 'Demo sign-in failed.'
    }
    const me = await res.json()
    setUser(me)
    return null
  }, [])

  const logout = useCallback(async () => {
    await fetch('/api/auth/logout', { method: 'POST', credentials: 'include' })
    setUser({ authenticated: false })
  }, [])

  const value = useMemo(
    () => ({ user, loading, login, loginDemo, logout, refresh }),
    [user, loading, login, loginDemo, logout, refresh],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth outside provider')
  return ctx
}
