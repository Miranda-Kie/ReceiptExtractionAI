import { FormEvent, useCallback, useEffect, useMemo, useState } from 'react'
import { Navigate } from 'react-router-dom'
import { canManageUsers as userCanManageUsers, useAuth } from '../auth'
import { readJson } from '../http'
import { applyStoredTheme } from '../theme'

type DirectoryRole = 'Owner' | 'Admin' | 'Officer'

type ManagedUser = {
  id: string
  username: string
  email?: string | null
  role: string
  isActive: boolean
  status: string
}

function normalizeDirectoryRole(role: string): DirectoryRole {
  if (role === 'Owner' || role === 'Admin' || role === 'Officer') return role
  return 'Officer'
}

/** Admins manage Officers only; Owners manage Admins/Officers/Owners. */
function canManageTargetRole(actorIsOwner: boolean, targetRole: string): boolean {
  const role = normalizeDirectoryRole(targetRole)
  if (role === 'Owner' || role === 'Admin') return actorIsOwner
  return true
}

/** Roles the signed-in actor may assign when creating or changing users. */
function assignableRoles(actorIsOwner: boolean, ownerExists: boolean): DirectoryRole[] {
  if (!actorIsOwner) return ['Officer', 'Admin']
  // Owner role is only offered when no Owner exists yet.
  return ownerExists ? ['Officer', 'Admin'] : ['Officer', 'Admin', 'Owner']
}

type EmailChangeChallenge = {
  verificationToken: string
  maskedEmail: string
  emailSent?: boolean
  message?: string
  canResendAtUtc?: string
  devCode?: string
}

function secondsUntil(iso?: string): number {
  if (!iso) return 0
  const ms = Date.parse(iso) - Date.now()
  return Number.isFinite(ms) ? Math.max(0, Math.ceil(ms / 1000)) : 0
}

function formatCountdown(totalSeconds: number): string {
  const minutes = Math.floor(totalSeconds / 60)
  const seconds = totalSeconds % 60
  if (minutes <= 0) return `${seconds}s`
  return `${minutes}:${seconds.toString().padStart(2, '0')}`
}

export default function UsersPage() {
  const { user } = useAuth()
  const actorIsOwner = Boolean(user?.isOwner || user?.role === 'Owner')
  const canManageUsers = userCanManageUsers(user)
  const [users, setUsers] = useState<ManagedUser[]>([])
  const ownerExists = useMemo(() => users.some((u) => u.role === 'Owner'), [users])
  const roleOptions = useMemo(() => assignableRoles(actorIsOwner, ownerExists), [actorIsOwner, ownerExists])
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [editEmailFor, setEditEmailFor] = useState<ManagedUser | null>(null)
  const [editEmailValue, setEditEmailValue] = useState('')
  const [emailChallenge, setEmailChallenge] = useState<EmailChangeChallenge | null>(null)
  const [emailCode, setEmailCode] = useState('')
  const [emailModalError, setEmailModalError] = useState<string | null>(null)
  const [emailModalStatus, setEmailModalStatus] = useState<string | null>(null)
  const [nowTick, setNowTick] = useState(0)
  const [roleConfirm, setRoleConfirm] = useState<{
    user: ManagedUser
    next: DirectoryRole
  } | null>(null)
  const [roleConfirmError, setRoleConfirmError] = useState<string | null>(null)
  const [statusConfirm, setStatusConfirm] = useState<{
    user: ManagedUser
    next: 'Active' | 'Inactive'
  } | null>(null)
  const [statusConfirmError, setStatusConfirmError] = useState<string | null>(null)
  const [showAdd, setShowAdd] = useState(false)
  const [addUsername, setAddUsername] = useState('')
  const [addEmail, setAddEmail] = useState('')
  const [addRole, setAddRole] = useState<DirectoryRole>('Officer')
  const [addError, setAddError] = useState<string | null>(null)

  useEffect(() => {
    if (!emailChallenge) return
    const id = window.setInterval(() => setNowTick((n) => n + 1), 1000)
    return () => window.clearInterval(id)
  }, [emailChallenge])

  const emailResendWait = useMemo(
    () => secondsUntil(emailChallenge?.canResendAtUtc),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [emailChallenge?.canResendAtUtc, nowTick],
  )

  const sortedUsers = useMemo(() => {
    const selfName = user?.username
    return [...users].sort((a, b) => {
      const aSelf = selfName != null && a.username === selfName
      const bSelf = selfName != null && b.username === selfName
      if (aSelf !== bSelf) return aSelf ? -1 : 1
      return a.username.localeCompare(b.username, undefined, { sensitivity: 'base' })
    })
  }, [users, user?.username])

  const load = useCallback(async () => {
    setError(null)
    const res = await fetch('/api/users', { credentials: 'include' })
    if (res.status === 403) {
      setError('Owner or Admin access required.')
      return
    }
    if (!res.ok) {
      setError('Could not load users.')
      return
    }
    const data = await readJson<{ users?: ManagedUser[] }>(res)
    setUsers(data.users || [])
  }, [])

  useEffect(() => {
    applyStoredTheme()
    if (canManageUsers) {
      void load()
    }
  }, [canManageUsers, load])

  if (!canManageUsers) {
    return <Navigate to="/" replace />
  }

  function closeAdd() {
    setShowAdd(false)
    setAddUsername('')
    setAddEmail('')
    setAddRole('Officer')
    setAddError(null)
  }

  async function onAddUser(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setAddError(null)
    setError(null)
    setMessage(null)
    try {
      const res = await fetch('/api/users', {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          username: addUsername,
          email: addEmail,
          role: addRole,
        }),
      })
      const data = await readJson<{
        error?: string
        message?: string
        setPasswordLink?: string
        resetLink?: string
        user?: { username?: string }
      }>(res)
      if (!res.ok) {
        setAddError(data.error || 'Could not create user.')
        return
      }
      const name = data.user?.username || addUsername
      const inviteLink =
        typeof data.setPasswordLink === 'string'
          ? data.setPasswordLink
          : typeof data.resetLink === 'string'
            ? data.resetLink
            : null
      if (inviteLink) {
        setMessage(
          `${data.message || `Created ${name}.`} Development set-password link: ${window.location.origin}${inviteLink}`,
        )
      } else {
        setMessage(data.message || `Created user ${name}. A set-password link was emailed.`)
      }
      closeAdd()
      await load()
    } finally {
      setBusy(false)
    }
  }

  async function onReset(u: ManagedUser) {
    setBusy(true)
    setError(null)
    setMessage(null)
    try {
      const res = await fetch(`/api/users/${u.id}/reset-password`, {
        method: 'POST',
        credentials: 'include',
      })
      const data = await readJson<{ error?: string; message?: string; resetLink?: string }>(res)
      if (!res.ok) {
        setError(data.error || 'Reset failed.')
        return
      }
      if (data.resetLink) {
        setMessage(
          `${data.message} Development link: ${window.location.origin}${data.resetLink}`,
        )
      } else {
        setMessage(data.message || 'Reset link sent.')
      }
    } finally {
      setBusy(false)
    }
  }

  function closeEmailModal() {
    setEditEmailFor(null)
    setEditEmailValue('')
    setEmailChallenge(null)
    setEmailCode('')
    setEmailModalError(null)
    setEmailModalStatus(null)
  }

  async function onSendEmailCode(e: FormEvent) {
    e.preventDefault()
    if (!editEmailFor) return
    setBusy(true)
    setEmailModalError(null)
    setEmailModalStatus(null)
    setError(null)
    setMessage(null)
    try {
      const res = await fetch(`/api/users/${editEmailFor.id}/email`, {
        method: 'PUT',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: editEmailValue.trim() }),
      })
      const data = await readJson<{
        error?: string
        requiresVerification?: boolean
        verificationToken?: string
        maskedEmail?: string
        emailSent?: boolean
        message?: string
        canResendAtUtc?: string
        devCode?: string
      }>(res)
      if (!res.ok) {
        setEmailModalError(data.error || 'Could not start email change.')
        return
      }
      if (data.requiresVerification && typeof data.verificationToken === 'string') {
        setEmailChallenge({
          verificationToken: data.verificationToken,
          maskedEmail: typeof data.maskedEmail === 'string' ? data.maskedEmail : '***',
          emailSent: data.emailSent === true,
          message: typeof data.message === 'string' ? data.message : undefined,
          canResendAtUtc: typeof data.canResendAtUtc === 'string' ? data.canResendAtUtc : undefined,
          devCode: typeof data.devCode === 'string' ? data.devCode : undefined,
        })
        setEmailCode('')
        setEmailModalStatus(data.message || 'Verification code sent.')
        return
      }
      setEmailModalError('Unexpected response from server.')
    } finally {
      setBusy(false)
    }
  }

  async function onConfirmEmailCode(e: FormEvent) {
    e.preventDefault()
    if (!editEmailFor || !emailChallenge) return
    setBusy(true)
    setEmailModalError(null)
    setError(null)
    setMessage(null)
    try {
      const res = await fetch(`/api/users/${editEmailFor.id}/email/verify`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          verificationToken: emailChallenge.verificationToken,
          code: emailCode,
        }),
      })
      const data = await readJson<{ error?: string }>(res)
      if (!res.ok) {
        setEmailModalError(data.error || 'Verification failed.')
        return
      }
      setMessage(`Email updated for ${editEmailFor.username}.`)
      closeEmailModal()
      await load()
    } finally {
      setBusy(false)
    }
  }

  async function onResendEmailCode() {
    if (!editEmailFor || !emailChallenge) return
    if (emailResendWait > 0) {
      setEmailModalError(
        `A verification code was already sent. You can request a new one in ${formatCountdown(emailResendWait)}.`,
      )
      return
    }
    setBusy(true)
    setEmailModalError(null)
    try {
      const res = await fetch(`/api/users/${editEmailFor.id}/email/resend`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ verificationToken: emailChallenge.verificationToken }),
      })
      const data = await readJson<{
        error?: string
        verificationToken?: string
        maskedEmail?: string
        emailSent?: boolean
        message?: string
        canResendAtUtc?: string
        devCode?: string
      }>(res)
      if (!res.ok) {
        setEmailModalError(data.error || 'Could not resend code.')
        return
      }
      setEmailChallenge({
        verificationToken: data.verificationToken || emailChallenge.verificationToken,
        maskedEmail: typeof data.maskedEmail === 'string' ? data.maskedEmail : emailChallenge.maskedEmail,
        emailSent: data.emailSent === true,
        message: typeof data.message === 'string' ? data.message : undefined,
        canResendAtUtc: typeof data.canResendAtUtc === 'string' ? data.canResendAtUtc : undefined,
        devCode: typeof data.devCode === 'string' ? data.devCode : undefined,
      })
      setEmailCode('')
      setEmailModalStatus(data.message || 'A new code was emailed.')
    } finally {
      setBusy(false)
    }
  }

  function requestRoleChange(u: ManagedUser, next: DirectoryRole) {
    if (normalizeDirectoryRole(u.role) === next) return
    setRoleConfirmError(null)
    setRoleConfirm({ user: u, next })
  }

  function requestStatusChange(u: ManagedUser, next: 'Active' | 'Inactive') {
    const isActive = next === 'Active'
    if (u.isActive === isActive) return
    setStatusConfirmError(null)
    setStatusConfirm({ user: u, next })
  }

  async function confirmRoleChange() {
    if (!roleConfirm) return
    const { user: u, next } = roleConfirm
    setBusy(true)
    setError(null)
    setRoleConfirmError(null)
    setMessage(null)
    try {
      const res = await fetch(`/api/users/${u.id}/role`, {
        method: 'PUT',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ role: next }),
      })
      const data = await readJson<{ error?: string }>(res)
      if (!res.ok) {
        setRoleConfirmError(data.error || 'Could not update role.')
        await load()
        return
      }
      setMessage(`Role for ${u.username} set to ${next}.`)
      setRoleConfirm(null)
      await load()
    } finally {
      setBusy(false)
    }
  }

  async function confirmStatusChange() {
    if (!statusConfirm) return
    const { user: u, next } = statusConfirm
    const isActive = next === 'Active'
    setBusy(true)
    setError(null)
    setStatusConfirmError(null)
    setMessage(null)
    try {
      const res = await fetch(`/api/users/${u.id}/status`, {
        method: 'PUT',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ isActive }),
      })
      const data = await readJson<{ error?: string; status?: string }>(res)
      if (!res.ok) {
        setStatusConfirmError(data.error || 'Could not update status.')
        await load()
        return
      }
      setMessage(`Status for ${u.username} set to ${data.status || next}.`)
      setStatusConfirm(null)
      await load()
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="page">
      <header className="page-head">
        <div className="page-head-row">
          <div>
            <h1>Users</h1>
          </div>
          <button
            type="button"
            className="btn-stamp"
            disabled={busy}
            onClick={() => {
              setAddError(null)
              setShowAdd(true)
            }}
          >
            Add
          </button>
        </div>
      </header>

      {error && (
        <div className="alert danger" role="alert">
          {error}
        </div>
      )}
      {message && (
        <div className="alert info" role="status">
          {message}
        </div>
      )}

      <section className="card">
        <div className="table-wrap users-table-wrap">
          <table className="users-table">
            <thead>
              <tr>
                <th className="col-user">Username</th>
                <th className="col-email">Email</th>
                <th className="col-role">Role</th>
                <th className="col-status">Status</th>
                <th className="col-actions">Actions</th>
              </tr>
            </thead>
            <tbody>
              {sortedUsers.map((u) => {
                const directoryRole = normalizeDirectoryRole(u.role)
                const canManageRow = canManageTargetRole(actorIsOwner, directoryRole)
                const isSelf = u.username === user.username
                // Own account may always request a reset link while signed in.
                const canResetPassword = Boolean(u.email) && (isSelf || canManageRow)
                const roleLockedReason = isSelf
                  ? 'You cannot change your own role'
                  : !canManageRow
                    ? 'Only an Owner can change Admin or Owner roles'
                    : 'Change account role'
                const statusLockedReason = isSelf
                  ? 'You cannot change your own status'
                  : !canManageRow
                    ? 'Only an Owner can change Admin or Owner status'
                    : 'Change account status'
                return (
                  <tr key={u.id}>
                    <td className="col-user">
                      <span className="user-name">{u.username}</span>
                    </td>
                    <td className="col-email">
                      <div className="email-cell">
                        <span className="email-cell-text" title={u.email || undefined}>
                          {u.email || <span className="muted">—</span>}
                        </span>
                        {isSelf && (
                          <button
                            type="button"
                            className="linkish"
                            disabled={busy}
                            title="Change your email address"
                            onClick={() => {
                              setEditEmailFor(u)
                              setEditEmailValue(u.email || '')
                              setEmailChallenge(null)
                              setEmailCode('')
                              setEmailModalError(null)
                              setEmailModalStatus(null)
                            }}
                          >
                            Change email
                          </button>
                        )}
                      </div>
                    </td>
                    <td className="col-role">
                      {isSelf || !canManageRow ? (
                        <span
                          className={`role-select role-${directoryRole.toLowerCase()} role-readonly`}
                          title={isSelf ? 'Your role' : roleLockedReason}
                        >
                          {directoryRole}
                        </span>
                      ) : (
                        <select
                          className={`role-select role-${directoryRole.toLowerCase()}`}
                          value={directoryRole}
                          disabled={busy}
                          title={roleLockedReason}
                          aria-label={`Role for ${u.username}`}
                          onChange={(e) =>
                            requestRoleChange(u, e.target.value as DirectoryRole)
                          }
                        >
                          {roleOptions.map((role) => (
                            <option key={role} value={role}>
                              {role}
                            </option>
                          ))}
                        </select>
                      )}
                    </td>
                    <td className="col-status">
                      <select
                        className={`status-select ${u.isActive ? 'is-active' : 'is-inactive'}`}
                        value={u.isActive ? 'Active' : 'Inactive'}
                        disabled={busy || isSelf || !canManageRow}
                        title={statusLockedReason}
                        aria-label={`Status for ${u.username}`}
                        onChange={(e) =>
                          requestStatusChange(u, e.target.value as 'Active' | 'Inactive')
                        }
                      >
                        <option value="Active">Active</option>
                        <option value="Inactive">Inactive</option>
                      </select>
                    </td>
                    <td className="col-actions">
                      <div className="user-actions">
                        <button
                          type="button"
                          className="linkish"
                          disabled={busy || !canResetPassword}
                          title={
                            !u.email
                              ? 'No email on file for password reset'
                              : isSelf
                                ? 'Send a password reset link to your email'
                                : !canManageRow
                                  ? 'Only an Owner can reset Admin or Owner passwords'
                                  : 'Send password reset link'
                          }
                          onClick={() => onReset(u)}
                        >
                          Reset password
                        </button>
                      </div>
                    </td>
                  </tr>
                )
              })}
              {users.length === 0 && (
                <tr>
                  <td colSpan={5} className="muted empty-users">
                    No users found.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </section>

      {showAdd && (
        <div className="modal-backdrop" role="presentation" onClick={closeAdd}>
          <div
            className="modal"
            role="dialog"
            aria-labelledby="add-user-title"
            onClick={(e) => e.stopPropagation()}
          >
            <h2 id="add-user-title">Add user</h2>
            <p className="muted">
              Create the account with username, email, and role. We email a link so they can set
              their own password.
            </p>
            <form onSubmit={onAddUser} className="auth-form">
              <label>
                Username
                <input
                  type="text"
                  value={addUsername}
                  onChange={(e) => setAddUsername(e.target.value)}
                  required
                  minLength={2}
                  maxLength={64}
                  autoComplete="off"
                />
              </label>
              <label>
                Email
                <input
                  type="email"
                  value={addEmail}
                  onChange={(e) => setAddEmail(e.target.value)}
                  required
                  autoComplete="email"
                  placeholder="name@example.com"
                />
              </label>
              <label>
                Role
                <select
                  value={addRole}
                  onChange={(e) => setAddRole(e.target.value as DirectoryRole)}
                  aria-label="Role"
                >
                  {roleOptions.map((role) => (
                    <option key={role} value={role}>
                      {role}
                    </option>
                  ))}
                </select>
              </label>
              {addError && (
                <p className="error" role="alert">
                  {addError}
                </p>
              )}
              <div className="row-actions">
                <button type="button" className="ghost" onClick={closeAdd} disabled={busy}>
                  Cancel
                </button>
                <button type="submit" className="btn-stamp" disabled={busy}>
                  {busy ? 'Creating…' : 'Create & send link'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {editEmailFor && (
        <div className="modal-backdrop" role="presentation" onClick={closeEmailModal}>
          <div
            className="modal"
            role="dialog"
            aria-labelledby="edit-email-title"
            onClick={(e) => e.stopPropagation()}
          >
            <h2 id="edit-email-title">Change your email</h2>
            {!emailChallenge ? (
              <>
                <p className="muted">
                  Only you can change the email on this account. We will send a verification code to
                  the new address; it updates only after you confirm that code.
                </p>
                <form onSubmit={onSendEmailCode} className="auth-form">
                  <label>
                    New email
                    <input
                      type="email"
                      value={editEmailValue}
                      onChange={(e) => setEditEmailValue(e.target.value)}
                      required
                      autoComplete="email"
                      placeholder="name@example.com"
                    />
                  </label>
                  {emailModalError && (
                    <p className="error" role="alert">
                      {emailModalError}
                    </p>
                  )}
                  <div className="row-actions">
                    <button type="button" className="ghost" onClick={closeEmailModal}>
                      Cancel
                    </button>
                    <button type="submit" className="btn-stamp" disabled={busy}>
                      {busy ? 'Sending…' : 'Send verification code'}
                    </button>
                  </div>
                </form>
              </>
            ) : (
              <>
                {emailModalStatus && (
                  <p className="auth-ok" role="status">
                    {emailModalStatus}
                  </p>
                )}
                {emailChallenge.devCode && (
                  <p className="auth-dev-code" role="note">
                    Development code (email not delivered): <strong>{emailChallenge.devCode}</strong>
                  </p>
                )}
                <form onSubmit={onConfirmEmailCode} className="auth-form">
                  <label>
                    Verification code
                    <input
                      value={emailCode}
                      onChange={(e) => setEmailCode(e.target.value.replace(/\D/g, '').slice(0, 6))}
                      inputMode="numeric"
                      autoComplete="one-time-code"
                      placeholder="6-digit code"
                      required
                      minLength={6}
                      maxLength={6}
                    />
                  </label>
                  {emailModalError && (
                    <p className="error" role="alert">
                      {emailModalError}
                    </p>
                  )}
                  <div className="row-actions">
                    <button
                      type="button"
                      className="ghost"
                      onClick={() => {
                        setEmailChallenge(null)
                        setEmailCode('')
                        setEmailModalError(null)
                        setEmailModalStatus(null)
                      }}
                    >
                      Back
                    </button>
                    <button type="submit" className="btn-stamp" disabled={busy || emailCode.length < 6}>
                      {busy ? 'Confirming…' : 'Confirm email'}
                    </button>
                  </div>
                </form>
                <button
                  type="button"
                  className="btn-ghost demo-btn"
                  disabled={busy || emailResendWait > 0}
                  onClick={onResendEmailCode}
                >
                  {emailResendWait > 0
                    ? `Resend code (${formatCountdown(emailResendWait)})`
                    : 'Resend code'}
                </button>
              </>
            )}
          </div>
        </div>
      )}

      {roleConfirm && (
        <div
          className="modal-backdrop"
          role="presentation"
          onClick={() => {
            if (!busy) {
              setRoleConfirm(null)
              setRoleConfirmError(null)
            }
          }}
        >
          <div
            className="modal"
            role="dialog"
            aria-labelledby="role-confirm-title"
            onClick={(e) => e.stopPropagation()}
          >
            <h2 id="role-confirm-title">Change role?</h2>
            <p>
              Change <strong>{roleConfirm.user.username}</strong> from{' '}
              <strong>{normalizeDirectoryRole(roleConfirm.user.role)}</strong> to{' '}
              <strong>{roleConfirm.next}</strong>?
            </p>
            {roleConfirmError && (
              <p className="error" role="alert">
                {roleConfirmError}
              </p>
            )}
            <div className="row-actions">
              <button
                type="button"
                className="ghost"
                disabled={busy}
                onClick={() => {
                  setRoleConfirm(null)
                  setRoleConfirmError(null)
                }}
              >
                Cancel
              </button>
              <button
                type="button"
                className="btn-stamp"
                disabled={busy}
                onClick={() => void confirmRoleChange()}
              >
                {busy ? 'Saving…' : 'Confirm role'}
              </button>
            </div>
          </div>
        </div>
      )}

      {statusConfirm && (
        <div
          className="modal-backdrop"
          role="presentation"
          onClick={() => {
            if (!busy) {
              setStatusConfirm(null)
              setStatusConfirmError(null)
            }
          }}
        >
          <div
            className="modal"
            role="dialog"
            aria-labelledby="status-confirm-title"
            onClick={(e) => e.stopPropagation()}
          >
            <h2 id="status-confirm-title">Change status?</h2>
            <p>
              Change <strong>{statusConfirm.user.username}</strong> from{' '}
              <strong>{statusConfirm.user.isActive ? 'Active' : 'Inactive'}</strong> to{' '}
              <strong>{statusConfirm.next}</strong>?
            </p>
            {statusConfirm.next === 'Inactive' && (
              <p className="muted">Inactive accounts cannot sign in.</p>
            )}
            {statusConfirmError && (
              <p className="error" role="alert">
                {statusConfirmError}
              </p>
            )}
            <div className="row-actions">
              <button
                type="button"
                className="ghost"
                disabled={busy}
                onClick={() => {
                  setStatusConfirm(null)
                  setStatusConfirmError(null)
                }}
              >
                Cancel
              </button>
              <button
                type="button"
                className="btn-stamp"
                disabled={busy}
                onClick={() => void confirmStatusChange()}
              >
                {busy ? 'Saving…' : 'Confirm status'}
              </button>
            </div>
          </div>
        </div>
      )}

    </main>
  )
}
