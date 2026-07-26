import React from 'react'

interface SaveReminderModalProps {
  showSaveRemind: boolean
  setShowSaveRemind: (show: boolean) => void
  allRowsValidated: boolean
  validatedCount: number
  receiptsLength: number
  busy: boolean
  exportSave: (confirmed?: boolean) => Promise<void>
}

export function SaveReminderModal({
  showSaveRemind,
  setShowSaveRemind,
  allRowsValidated,
  validatedCount,
  receiptsLength,
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
        <h2 id="save-remind-title">Validate before saving</h2>
        <p>
          Please review every preview row — especially <strong>InvoiceNumber</strong> — and check the{' '}
          <strong>Validate</strong> box at the end of each row before writing to the database.
        </p>
        <p className={allRowsValidated ? 'ok-note' : 'error'} role="status">
          {validatedCount} of {receiptsLength} row(s) validated.
          {!allRowsValidated && ' Check every row’s Validate box to continue.'}
        </p>
        <div className="row-actions">
          <button type="button" className="ghost" onClick={() => setShowSaveRemind(false)}>
            Cancel
          </button>
          <button
            type="button"
            className="btn-stamp"
            disabled={!allRowsValidated || busy}
            onClick={() => exportSave(false)}
          >
            Continue and save
          </button>
        </div>
      </div>
    </div>
  )
}
