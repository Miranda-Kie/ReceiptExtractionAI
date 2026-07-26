import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import { readJson } from './http'

export type AuthUser = {
  authenticated: boolean
  username?: string
  role?: string
  /** True for Owner — can manage Admin accounts. */
  isOwner?: boolean
  /** True when role is Admin (not Owner). */
  isAdmin?: boolean
  /** True for Owner or Admin (Users page access). */
  canManageUsers?: boolean
  isDemo?: boolean
}

/** Owner or Admin — can open the Users directory. */
export function canManageUsers(user: AuthUser | null | undefined): boolean {
  if (!user?.authenticated) return false
  if (user.canManageUsers) return true
  return Boolean(user.isOwner || user.isAdmin || user.role === 'Owner' || user.role === 'Admin')
}

export type LoginChallenge = {
  requiresVerification: true
  verificationToken: string
  maskedEmail: string
  emailSent?: boolean
  emailConfigured?: boolean
  message?: string
  /** ISO timestamp; resend is blocked until this time (10 minutes after send). */
  canResendAtUtc?: string
  expiresAtUtc?: string
  /** Present only in Development when email was not sent successfully. */
  devCode?: string
}

type AuthContextValue = {
  user: AuthUser | null
  loading: boolean
  /** null = signed in; LoginChallenge = needs email code; string = error */
  login: (username: string, password: string) => Promise<string | LoginChallenge | null>
  completeVerification: (verificationToken: string, code: string) => Promise<string | null>
  resendVerificationCode: (verificationToken: string) => Promise<string | LoginChallenge>
  loginDemo: () => Promise<string | null>
  logout: () => Promise<void>
  refresh: () => Promise<void>
}

const AuthContext = createContext<AuthContextValue | null>(null)

async function fetchMe(): Promise<AuthUser> {
  const res = await fetch('/api/auth/me', { credentials: 'include' })
  const body = await readJson<AuthUser>(res)
  if (typeof body.authenticated !== 'boolean') {
    return { authenticated: false }
  }
  return body
}

function asChallenge(body: Record<string, unknown>): LoginChallenge | null {
  if (body.requiresVerification !== true || typeof body.verificationToken !== 'string') {
    return null
  }
  return {
    requiresVerification: true,
    verificationToken: body.verificationToken,
    maskedEmail: typeof body.maskedEmail === 'string' ? body.maskedEmail : '***',
    emailSent: body.emailSent === true,
    emailConfigured: body.emailConfigured === true,
    message: typeof body.message === 'string' ? body.message : undefined,
    canResendAtUtc: typeof body.canResendAtUtc === 'string' ? body.canResendAtUtc : undefined,
    expiresAtUtc: typeof body.expiresAtUtc === 'string' ? body.expiresAtUtc : undefined,
    devCode: typeof body.devCode === 'string' ? body.devCode : undefined,
  }
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
    const body = await readJson(res)
    if (!res.ok) {
      return (body.error as string) || 'Login failed.'
    }

    const challenge = asChallenge(body)
    if (challenge) {
      return challenge
    }

    setUser(body as unknown as AuthUser)
    return null
  }, [])

  const completeVerification = useCallback(async (verificationToken: string, code: string) => {
    const res = await fetch('/api/auth/verify', {
      method: 'POST',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ verificationToken, code }),
    })
    const body = await readJson(res)
    if (!res.ok) {
      return (body.error as string) || 'Verification failed.'
    }
    setUser(body as unknown as AuthUser)
    return null
  }, [])

  const resendVerificationCode = useCallback(async (verificationToken: string) => {
    const res = await fetch('/api/auth/resend-code', {
      method: 'POST',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ verificationToken }),
    })
    const body = await readJson(res)
    if (!res.ok) {
      return (body.error as string) || 'Could not resend code.'
    }
    const challenge = asChallenge(body)
    return challenge ?? 'Could not resend code.'
  }, [])

  const loginDemo = useCallback(async () => {
    const res = await fetch('/api/auth/demo', {
      method: 'POST',
      credentials: 'include',
    })
    const body = await readJson<AuthUser & { error?: string }>(res)
    if (!res.ok) {
      return body.error || 'Demo sign-in failed.'
    }
    setUser(body)
    return null
  }, [])

  const logout = useCallback(async () => {
    await fetch('/api/auth/logout', { method: 'POST', credentials: 'include' })
    setUser({ authenticated: false })
  }, [])

  const value = useMemo(
    () => ({
      user,
      loading,
      login,
      completeVerification,
      resendVerificationCode,
      loginDemo,
      logout,
      refresh,
    }),
    [user, loading, login, completeVerification, resendVerificationCode, loginDemo, logout, refresh],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth outside provider')
  return ctx
}
