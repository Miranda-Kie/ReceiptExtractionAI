import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { canManageUsers, useAuth } from '../auth'
import { ThemeToggle } from '../theme'

export default function AppShell() {
  const { user, logout } = useAuth()
  const navigate = useNavigate()

  async function handleLogout() {
    await logout()
    navigate('/login', { replace: true })
  }

  return (
    <div className="app-shell">
      <header className="topbar">
        <div className="topbar-left">
          <div className="brand">
            HST <span>Receipts</span>
          </div>
          <nav className="app-tabs" aria-label="Main">
            <NavLink to="/" end className={({ isActive }) => (isActive ? 'app-tab active' : 'app-tab')}>
              Receipts
            </NavLink>
            {canManageUsers(user) || user?.role === 'Officer' ? (
              <NavLink to="/users" className={({ isActive }) => (isActive ? 'app-tab active' : 'app-tab')}>
                Users
              </NavLink>
            ) : (
              <span
                className="app-tab disabled"
                title="Sign in with a paid account to manage users"
                aria-disabled="true"
              >
                Users
              </span>
            )}
          </nav>
        </div>
        <div className="topbar-right">
          <span className="user-chip" title={user?.role}>
            {user?.username}
          </span>
          <ThemeToggle />
          <button type="button" className="ghost" onClick={() => handleLogout()}>
            Sign out
          </button>
        </div>
      </header>
      <Outlet />
    </div>
  )
}
