import { useEffect, useState } from 'react'

const STORAGE_KEY = 'hst-theme'

export type ThemeMode = 'light' | 'dark'

function normalizeMode(value: string | null): ThemeMode {
  return value === 'dark' ? 'dark' : 'light'
}

export function applyStoredTheme() {
  applyTheme(normalizeMode(localStorage.getItem(STORAGE_KEY)))
}

export function applyTheme(mode: ThemeMode) {
  document.documentElement.setAttribute('data-theme', mode)
  localStorage.setItem(STORAGE_KEY, mode)
}

export function ThemeToggle() {
  const [mode, setMode] = useState<ThemeMode>(() => normalizeMode(localStorage.getItem(STORAGE_KEY)))

  useEffect(() => {
    applyTheme(mode)
  }, [mode])

  function cycle() {
    setMode((m) => (m === 'light' ? 'dark' : 'light'))
  }

  const label = mode === 'light' ? 'Theme: Light' : 'Theme: Dark'

  return (
    <button type="button" className="ghost theme-toggle" onClick={cycle} aria-label={label} title={label}>
      {mode === 'dark' ? 'Dark' : 'Light'}
    </button>
  )
}
