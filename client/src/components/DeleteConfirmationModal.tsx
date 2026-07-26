import React from 'react'

interface DeleteConfirmationModalProps {
  deleteConfirm: { index: number; label: string } | null
  setDeleteConfirm: (confirm: { index: number; label: string } | null) => void
  confirmDeleteRow: () => void
}

export function DeleteConfirmationModal({
  deleteConfirm,
  setDeleteConfirm,
  confirmDeleteRow,
}: DeleteConfirmationModalProps) {
  if (!deleteConfirm) return null

  return (
    <div
      className="modal-backdrop"
      role="presentation"
      onClick={() => setDeleteConfirm(null)}
      onKeyDown={(e) => {
        if (e.key === 'Escape') setDeleteConfirm(null)
      }}
    >
      <div
        className="modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="delete-row-title"
        onClick={(e) => e.stopPropagation()}
      >
        <h2 id="delete-row-title">Delete preview row?</h2>
        <p>
          Remove <strong>{deleteConfirm.label}</strong> from the current preview?
        </p>
        <p className="muted">
          This only removes it from the preview. It does not delete anything already saved in the
          database.
        </p>
        <div className="row-actions">
          <button type="button" className="ghost" onClick={() => setDeleteConfirm(null)}>
            Cancel
          </button>
          <button type="button" className="btn-row-delete" onClick={confirmDeleteRow}>
            Delete row
          </button>
        </div>
      </div>
    </div>
  )
}
