import { Navigate, Route, Routes } from 'react-router-dom'
import { AuthProvider, canManageUsers, useAuth } from './auth'
import AppShell from './components/AppShell'
import LoginPage from './pages/LoginPage'
import ReceiptsPage from './pages/ReceiptsPage'
import ResetPasswordPage from './pages/ResetPasswordPage'
import UsersPage from './pages/UsersPage'
import VerifyLoginPage from './pages/VerifyLoginPage'

function Protected({ children }: { children: React.ReactNode }) {
  const { user, loading } = useAuth()
  if (loading) return <div className="page-loading">Loading…</div>
  if (!user?.authenticated) return <Navigate to="/login" replace />
  return <>{children}</>
}

function AdminOnly({ children }: { children: React.ReactNode }) {
  const { user } = useAuth()
  if (!canManageUsers(user)) return <Navigate to="/" replace />
  return <>{children}</>
}

export default function App() {
  return (
    <AuthProvider>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/verify" element={<VerifyLoginPage />} />
        <Route path="/reset-password" element={<ResetPasswordPage />} />
        <Route path="/set-password" element={<ResetPasswordPage />} />
        <Route
          element={
            <Protected>
              <AppShell />
            </Protected>
          }
        >
          <Route path="/" element={<ReceiptsPage />} />
          <Route
            path="/users"
            element={
              <AdminOnly>
                <UsersPage />
              </AdminOnly>
            }
          />
        </Route>
      </Routes>
    </AuthProvider>
  )
}
