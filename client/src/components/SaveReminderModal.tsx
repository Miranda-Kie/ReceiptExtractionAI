import React from 'react'

interface SaveReminderModalProps {
  showSaveRemind: boolean
  setShowSaveRemind: (show: boolean) => void
  validatedCount: number
  busy: boolean
  exportSave: (confirmed?: boolean) => Promise<void>
}

export function SaveReminderModal({
  showSaveRemind,
  setShowSaveRemind,
  validatedCount,
  busy,
  exportSave,
}: SaveReminderModalProps) {
  if (!showSaveRemind) return null

  return (
    <div
      className="modal-backdrop"
      role="presentation"
      onClick={() => setShowSaveRemind(false)}
      onKeyDown={(e) => {
        if (e.key === 'Escape') setShowSaveRemind(false)
      }}
    >
      <div
        className="modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="save-remind-title"
        onClick={(e) => e.stopPropagation()}
      >
        <h2 id="save-remind-title">Save to database</h2>
        <p>
          Save <strong>{validatedCount} row(s)</strong> to the database?
        </p>
        <div className="row-actions">
          <button type="button" className="ghost" onClick={() => setShowSaveRemind(false)}>
            Cancel
          </button>
          <button
            type="button"
            className="btn-stamp"
            disabled={validatedCount === 0 || busy}
            onClick={() => exportSave(false)}
          >
            Continue and save
          </button>
        </div>
      </div>
    </div>
  )
}
