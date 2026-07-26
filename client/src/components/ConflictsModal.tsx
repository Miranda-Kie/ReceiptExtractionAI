import React from 'react'

interface ConflictsModalProps {
  conflicts: any[] | null
  setConflicts: (conflicts: any[] | null) => void
  exportSave: (confirmed: boolean) => Promise<void>
}

export function ConflictsModal({
  conflicts,
  setConflicts,
  exportSave,
}: ConflictsModalProps) {
  if (!conflicts) return null

  return (
    <div
      className="modal-backdrop"
      role="presentation"
      onClick={() => setConflicts(null)}
      onKeyDown={(e) => {
        if (e.key === 'Escape') setConflicts(null)
      }}
    >
      <div
        className="modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="conflict-title"
        onClick={(e) => e.stopPropagation()}
      >
        <h2 id="conflict-title">Confirm overwrite</h2>
        <p>
          An existing receipt already uses this InvoiceNumber. Confirm to overwrite that row and
          export.
        </p>
        <div className="conflict-list">
          {conflicts.map((c, idx) => (
            <div key={idx} className="conflict-block">
              <strong>
                Invoice {c.invoiceNumber}
                {c.storeName ? ` · ${c.storeName}` : ''}
                {c.receiptDate ? ` · Date ${c.receiptDate}` : ''}
              </strong>
              <table>
                <thead>
                  <tr>
                    <th>Field</th>
                    <th>Database</th>
                    <th>Preview</th>
                  </tr>
                </thead>
                <tbody>
                  {(c.differences || []).map((d: any, j: number) => (
                    <tr key={j}>
                      <td>{d.field}</td>
                      <td>{d.databaseValue}</td>
                      <td>{d.previewValue}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ))}
        </div>
        <div className="row-actions">
          <button type="button" className="ghost" onClick={() => setConflicts(null)}>
            Cancel
          </button>
          <button type="button" className="btn-stamp" onClick={() => exportSave(true)}>
            Confirm and export
          </button>
        </div>
      </div>
    </div>
  )
}
