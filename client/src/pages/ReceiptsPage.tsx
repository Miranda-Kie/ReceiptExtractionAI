import { useEffect, useMemo, useState } from 'react'
import { useAuth } from '../auth'
import { readJson } from '../http'
import { applyStoredTheme } from '../theme'
import {
  EXPORT_FIELDS,
  ReceiptRow,
  downloadBlob,
  moneyOk,
  normalizeTimeValue,
  toEditPayload,
  toTimeInputValue,
} from '../types'
import { useReceiptsProcessing } from '../hooks/useReceiptsProcessing'
import { useReceiptsExport } from '../hooks/useReceiptsExport'
import { ReceiptUploadSection } from '../components/ReceiptUploadSection'
import { DeleteConfirmationModal } from '../components/DeleteConfirmationModal'
import { SaveReminderModal } from '../components/SaveReminderModal'
import { ConflictsModal } from '../components/ConflictsModal'

export default function ReceiptsPage() {
  const { user } = useAuth()

  const { 
    files,
    setFiles,
    busy,
    message,
    setMessage,
    error,
    setError,
    batchId,
    setBatchId,
    receipts,
    setReceipts,
    onFilesChosen,
    processUploads,
    updateRow,
  } = useReceiptsProcessing()

  const {
    exportFields,
    setExportFields,
    selectedForExport,
    setSelectedForExport,
    conflicts,
    setConflicts,
    showSaveRemind,
    setShowSaveRemind,
    allRowsValidated,
    validatedCount,
    toggleExportField,
    toggleExportSelection,
    toggleAllExportSelection,
    exportOnly,
    exportSave,
    validateRequired,
  } = useReceiptsExport({
    receipts,
    batchId,
    busy,
    setError,
    setMessage,
    setBatchId,
    isDemoUser: user?.isDemo,
  })

  const [deleteConfirm, setDeleteConfirm] = useState<{ index: number; label: string } | null>(null)

  useEffect(() => {
    applyStoredTheme()
    // Drop legacy client cache from earlier builds.
    try {
      sessionStorage.removeItem('hst-preview-batch')
    } catch {
      /* ignore */
    }
  }, [])

  // This part was moved to useReceiptsProcessing.tsx
  // useEffect(() => {
  //   // Always reset when the signed-in identity changes (including demo → demo).
  //   setBatchId(null)
  //   setReceipts([])
  //   setFiles([])
  //   setMessage(null)
  //   setError(null)
  //   setConflicts(null)
  //   setShowSaveRemind(false)
  //   setDeleteConfirm(null)
  //   setExportFields([...EXPORT_FIELDS])

  //   let cancelled = false
  //   fetch('/api/receipts/session', { credentials: 'include' })
  //     .then(async (r) => (r.ok ? readJson<{ batchId?: string; receipts?: ReceiptRow[] }>(r) : null))
  //     .then((data) => {
  //       if (cancelled || !data) return
  //       // After login/logout the server clears the batch; only hydrate when present.
  //       if (data.batchId && Array.isArray(data.receipts) && data.receipts.length > 0) {
  //         setBatchId(data.batchId)
  //         setReceipts(data.receipts.map((r: ReceiptRow) => ({ ...r, validated: false })))
  //         setSelectedForExport(new Array(data.receipts.length).fill(true))
  //       }
  //     })
  //     .catch(() => undefined)

  //   return () => {
  //     cancelled = true
  //   }
  // }, [user?.username, user?.isDemo, user?.authenticated])

  function requestDeleteRow(index: number) {
    const row = receipts[index]
    if (!row) return
    const label =
      row.invoiceNumber?.trim() ||
      row.storeName?.trim() ||
      row.receiptName ||
      `row ${index + 1}`
    setDeleteConfirm({ index, label })
  }

  function confirmDeleteRow() {
    if (!deleteConfirm) return
    const { index } = deleteConfirm
    setDeleteConfirm(null)

    setReceipts((rows) => {
      const next = rows.filter((_, i) => i !== index)
      if (next.length === 0) {
        setBatchId(null)
        setMessage('All preview rows removed. Upload receipts again to start a new preview.')
      }
      return next
    })
    setError(null)
    setConflicts(null)
    setShowSaveRemind(false)
  }

  function headerWithExport(field: string, className: string, label: string) {
    return (
      <th className={className}>
        <span className="th-with-export">
          <span className="th-label">{label}</span>
          <input
            type="checkbox"
            className="th-export-check"
            checked={exportFields.includes(field)}
            onChange={(e) => toggleExportField(field, e.target.checked)}
            title={`Include ${label} in Excel export`}
            aria-label={`Include ${label} in Excel export`}
          />
        </span>
      </th>
    )
  }

  return (
    <>
      <main className="page">
        <header className="page-head">
          <h1>Receipt Upload</h1>
          <p>Upload receipt images and PDFs to extract store, invoice, tax, and totals.</p>
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

        <ReceiptUploadSection
          files={files}
          busy={busy}
          onFilesChosen={onFilesChosen}
          processUploads={processUploads}
        />

        {receipts.length > 0 && (
          <section className="card">
            <p className="toolbar-note">
              {user?.isDemo ? (
                <>
                  Demo mode: use the header checkboxes to choose Excel columns, then{' '}
                  <strong>Export Excel only</strong>.{' '}
                  <span className="demo-upsell">
                    Saving to the database and managing users require a paid account.
                  </span>
                </>
              ) : user?.isAdmin ? (
                <>
                  Use header checkboxes to choose Excel columns. Validate each preview row.{' '}
                  <span className="demo-upsell">
                    Azure SQL database configuration is required to save to the database.
                  </span>
                </>
              ) : (
                <>
                  Use header checkboxes to choose Excel columns. Validate each preview row, then use{' '}
                  <strong>Export Excel and save to database</strong> (upserts by InvoiceNumber).
                </>
              )}
            </p>
            <div className="row-actions wrap">
              <button type="button" className="ghost" onClick={() => setExportFields([...EXPORT_FIELDS])}>
                Select all
              </button>
              <button type="button" className="ghost" onClick={() => setExportFields([])}>
                Clear
              </button>
              <button
                type="button"
                className="btn-stamp"
                disabled={busy || Boolean(user?.isDemo || user?.isAdmin)}
                title={
                  user?.isDemo
                    ? 'Sign in with a paid account to save to the database'
                    : user?.isAdmin
                      ? 'Azure SQL database must be configured to save to the database'
                      : undefined
                }
                onClick={() => {
                  if (user?.isDemo || user?.isAdmin) return
                  setError(null)
                  setShowSaveRemind(true)
                }}
              >
                Export Excel and save to database
              </button>
              <button
                type="button"
                className={user?.isDemo ? 'btn-stamp' : 'btn-ghost'}
                disabled={busy}
                onClick={exportOnly}
              >
                Export Excel only
              </button>
            </div>

            <div className="table-wrap">
              <table className="preview-table">
                <thead>
                  <tr>
                    <th className="col-export">
                      <input
                        type="checkbox"
                        checked={selectedForExport.every(Boolean)}
                        onChange={(e) => toggleAllExportSelection(e.target.checked)}
                        title="Select all rows for export"
                        aria-label="Select all rows for export"
                      />
                    </th>
                    {headerWithExport('InvoiceNumber', 'col-invoice', 'Invoice Number')}
                    {headerWithExport('StoreName', 'col-store', 'Store Name')}
                    {headerWithExport('Currency', 'col-currency', 'Currency')}
                    {headerWithExport('Subtotal', 'col-money', 'Subtotal')}
                    {headerWithExport('GstHst', 'col-money', 'GST/HST')}
                    {headerWithExport('TotalAmount', 'col-money', 'Total Amount')}
                    {headerWithExport('ReceiptDate', 'col-date', 'Date')}
                    {headerWithExport('TransactionTime', 'col-time', 'Time')}
                    <th className="col-status">Status</th>
                    <th className="col-validate">Validate</th>
                    <th className="col-delete">Delete</th>
                  </tr>
                </thead>
                <tbody>
                  {receipts.map((r, i) => {
                    const amountsBad = !moneyOk(r.subtotal, r.gstHst, r.totalAmount)
                    const needsReview =
                      amountsBad || !r.invoiceNumber?.trim() || !r.storeName?.trim() || !r.receiptDate
                    return (
                      <tr
                        key={`${r.receiptName}-${i}`}
                        className={r.validated ? 'row-validated' : undefined}
                      >
                        <td className="col-export">
                          <input
                            type="checkbox"
                            checked={selectedForExport[i] ?? false}
                            onChange={(e) => toggleExportSelection(i, e.target.checked)}
                            aria-label={`Select row ${i + 1} for export`}
                            title="Select this row for export"
                          />
                        </td>
                        <td className="col-invoice">
                          <input
                            className={!r.invoiceNumber?.trim() ? 'invalid' : undefined}
                            value={r.invoiceNumber ?? ''}
                            onChange={(e) => updateRow(i, { invoiceNumber: e.target.value })}
                          />
                        </td>
                        <td className="col-store">
                          <input
                            className={!r.storeName?.trim() ? 'invalid' : undefined}
                            value={r.storeName ?? ''}
                            onChange={(e) => updateRow(i, { storeName: e.target.value })}
                          />
                        </td>
                        <td className="col-currency">
                          <input
                            value={r.currency ?? ''}
                            onChange={(e) => updateRow(i, { currency: e.target.value })}
                          />
                        </td>
                        <td className="col-money">
                          <input
                            className={amountsBad ? 'invalid' : undefined}
                            type="number"
                            step="0.01"
                            value={r.subtotal ?? ''}
                            onChange={(e) =>
                              updateRow(i, {
                                subtotal: e.target.value === '' ? null : Number(e.target.value),
                              })
                            }
                          />
                        </td>
                        <td className="col-money">
                          <input
                            className={amountsBad ? 'invalid' : undefined}
                            type="number"
                            step="0.01"
                            value={r.gstHst ?? ''}
                            onChange={(e) =>
                              updateRow(i, {
                                gstHst: e.target.value === '' ? null : Number(e.target.value),
                              })
                            }
                          />
                        </td>
                        <td className="col-money">
                          <input
                            className={amountsBad ? 'invalid' : undefined}
                            type="number"
                            step="0.01"
                            value={r.totalAmount ?? ''}
                            onChange={(e) =>
                              updateRow(i, {
                                totalAmount: e.target.value === '' ? null : Number(e.target.value),
                              })
                            }
                          />
                        </td>
                        <td className="col-date">
                          <input
                            className={!r.receiptDate ? 'invalid' : undefined}
                            type="date"
                            value={r.receiptDate ?? ''}
                            onChange={(e) => updateRow(i, { receiptDate: e.target.value || null })}
                          />
                        </td>
                        <td className="col-time">
                          <input
                            type="time"
                            step="1"
                            className={!r.transactionTime?.trim() ? 'invalid' : undefined}
                            value={toTimeInputValue(r.transactionTime)}
                            onChange={(e) =>
                              updateRow(i, {
                                transactionTime: e.target.value ? normalizeTimeValue(e.target.value) : null,
                              })
                            }
                          />
                        </td>
                        <td className="col-status">
                          <span
                            className={needsReview ? 'badge warn' : 'badge ok'}
                            title={needsReview ? 'Needs review' : 'OK'}
                          >
                            {needsReview ? 'Review' : 'OK'}
                          </span>
                        </td>
                        <td className="validate-cell col-validate">
                          <input
                            type="checkbox"
                            className="row-validate-check"
                            checked={Boolean(r.validated)}
                            disabled={Boolean(user?.isDemo)}
                            onChange={(e) => updateRow(i, { validated: e.target.checked })}
                            aria-label={`Validate row ${i + 1}`}
                            title={user?.isDemo ? 'Sign in with a paid account to validate rows' : 'Validate this row'}
                          />
                        </td>
                        <td className="delete-cell col-delete">
                          <button
                            type="button"
                            className="btn-row-delete"
                            disabled={busy}
                            onClick={() => requestDeleteRow(i)}
                            title="Delete this row"
                          >
                            Delete
                          </button>
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          </section>
        )}
      </main>

      <DeleteConfirmationModal
        deleteConfirm={deleteConfirm}
        setDeleteConfirm={setDeleteConfirm}
        confirmDeleteRow={confirmDeleteRow}
      />

      <SaveReminderModal
        showSaveRemind={showSaveRemind}
        setShowSaveRemind={setShowSaveRemind}
        allRowsValidated={allRowsValidated}
        validatedCount={validatedCount}
        receiptsLength={receipts.length}
        busy={busy}
        exportSave={exportSave}
      />

      <ConflictsModal
        conflicts={conflicts}
        setConflicts={setConflicts}
        exportSave={exportSave}
      />
    </>
  )
}