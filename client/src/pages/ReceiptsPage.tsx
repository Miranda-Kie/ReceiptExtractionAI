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

export default function ReceiptsPage() {
  const { user } = useAuth()
  const [files, setFiles] = useState<File[]>([])
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [batchId, setBatchId] = useState<string | null>(null)
  const [receipts, setReceipts] = useState<ReceiptRow[]>([])
  const [exportFields, setExportFields] = useState<string[]>([...EXPORT_FIELDS])
  const [conflicts, setConflicts] = useState<any[] | null>(null)
  const [showSaveRemind, setShowSaveRemind] = useState(false)
  const [deleteConfirm, setDeleteConfirm] = useState<{ index: number; label: string } | null>(null)

  const allRowsValidated =
    receipts.length > 0 && receipts.every((r) => Boolean(r.validated))
  const validatedCount = receipts.filter((r) => r.validated).length

  useEffect(() => {
    applyStoredTheme()
    // Drop legacy client cache from earlier builds.
    try {
      sessionStorage.removeItem('hst-preview-batch')
    } catch {
      /* ignore */
    }

    // Always reset when the signed-in identity changes (including demo → demo).
    setBatchId(null)
    setReceipts([])
    setFiles([])
    setMessage(null)
    setError(null)
    setConflicts(null)
    setShowSaveRemind(false)
    setDeleteConfirm(null)
    setExportFields([...EXPORT_FIELDS])

    let cancelled = false
    fetch('/api/receipts/session', { credentials: 'include' })
      .then(async (r) => (r.ok ? readJson<{ batchId?: string; receipts?: ReceiptRow[] }>(r) : null))
      .then((data) => {
        if (cancelled || !data) return
        // After login/logout the server clears the batch; only hydrate when present.
        if (data.batchId && Array.isArray(data.receipts) && data.receipts.length > 0) {
          setBatchId(data.batchId)
          setReceipts(data.receipts.map((r: ReceiptRow) => ({ ...r, validated: false })))
        }
      })
      .catch(() => undefined)

    return () => {
      cancelled = true
    }
  }, [user?.username, user?.isDemo, user?.authenticated])

  function onFilesChosen(list: FileList | null) {
    if (!list) return
    setFiles(Array.from(list))
  }

  async function processUploads() {
    if (files.length === 0) return
    setBusy(true)
    setError(null)
    setMessage(null)
    const form = new FormData()
    for (const f of files) form.append('files', f)
    try {
      const res = await fetch('/api/receipts/process', {
        method: 'POST',
        credentials: 'include',
        body: form,
      })
      const data = await readJson<{
        error?: string
        batchId?: string
        status?: string
        message?: string
        receipts?: ReceiptRow[]
      }>(res)
      if (!res.ok) {
        setError(data.error || 'Processing failed.')
        return
      }
      setBatchId(data.batchId ?? null)
      if (data.status === 'processing' && data.batchId) {
        setMessage(data.message || 'Processing in Azure…')
        setReceipts([])
        await pollBatchUntilDone(data.batchId)
        return
      }
      setReceipts((data.receipts || []).map((r: ReceiptRow) => ({ ...r, validated: false })))
      setMessage(data.message || null)
    } catch {
      setError('Processing failed. Check your connection and try again.')
    } finally {
      setBusy(false)
    }
  }

  async function pollBatchUntilDone(id: string) {
    const started = Date.now()
    while (Date.now() - started < 10 * 60 * 1000) {
      await new Promise((r) => setTimeout(r, 2000))
      const res = await fetch(`/api/receipts/batches/${id}`, { credentials: 'include' })
      const data = await readJson<{
        error?: string
        message?: string
        status?: string
        receipts?: ReceiptRow[]
        completedFiles?: number
        totalFiles?: number
      }>(res)
      if (!res.ok) {
        setError(data.error || 'Failed to poll batch status.')
        return
      }
      const elapsedSec = Math.round((Date.now() - started) / 1000)
      const stillIdle =
        (data.completedFiles ?? 0) === 0 &&
        (data.totalFiles ?? 0) > 0 &&
        elapsedSec >= 30
      setMessage(
        stillIdle
          ? `${data.message || 'Processing…'} Waiting for the Azure Function worker — keep \`func start\` running in src/HstReceipts.Functions.`
          : data.message || null,
      )
      if (data.status === 'completed') {
        setReceipts((data.receipts || []).map((r: ReceiptRow) => ({ ...r, validated: false })))
        return
      }
      if (data.status === 'failed') {
        setError(data.error || data.message || 'Pipeline processing failed.')
        setReceipts((data.receipts || []).map((r: ReceiptRow) => ({ ...r, validated: false })))
        return
      }
    }
    setError(
      'Timed out waiting for Azure Function / Document Intelligence. Start the Function with: cd src/HstReceipts.Functions && func start',
    )
  }

  function updateRow(index: number, patch: Partial<ReceiptRow>) {
    setReceipts((rows) =>
      rows.map((r, i) => {
        if (i !== index) return r
        const next = { ...r, ...patch }
        if (!Object.prototype.hasOwnProperty.call(patch, 'validated')) {
          next.validated = false
        }
        return next
      }),
    )
  }

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

  const payload = useMemo(() => {
    if (!batchId) return null
    return {
      batchId,
      receipts: receipts.map(toEditPayload),
      exportFields,
      previewValidated: allRowsValidated,
    }
  }, [batchId, receipts, exportFields, allRowsValidated])

  function toggleExportField(field: string, checked: boolean) {
    setExportFields((prev) => (checked ? [...prev, field] : prev.filter((x) => x !== field)))
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

  function validateRequired(): string | null {
    const missingInvoice: number[] = []
    const missingStore: number[] = []
    const missingDate: number[] = []
    receipts.forEach((r, i) => {
      if (!r.invoiceNumber?.trim()) missingInvoice.push(i + 1)
      if (!r.storeName?.trim()) missingStore.push(i + 1)
      if (!r.receiptDate) missingDate.push(i + 1)
    })
    if (!missingInvoice.length && !missingStore.length && !missingDate.length) return null
    const parts: string[] = []
    if (missingInvoice.length) parts.push(`missing InvoiceNumber on row(s) ${missingInvoice.join(', ')}`)
    if (missingStore.length) parts.push(`missing StoreName on row(s) ${missingStore.join(', ')}`)
    if (missingDate.length) parts.push(`missing Date on row(s) ${missingDate.join(', ')}`)
    return `Cannot export: ${parts.join('; ')}.`
  }

  async function exportOnly() {
    const err = validateRequired()
    if (err) {
      setError(err)
      return
    }
    if (!payload) return
    setBusy(true)
    setError(null)
    try {
      const res = await fetch('/api/receipts/export-only', {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      })
      if (!res.ok) {
        const body = await readJson<{ error?: string }>(res)
        setError(body.error || 'Export failed.')
        return
      }
      await downloadBlob(res, 'receipts.xlsx')
      setBatchId(null)
      setReceipts([])
      setConflicts(null)
      setMessage('Excel downloaded. Upload receipts again to start a new preview.')
    } finally {
      setBusy(false)
    }
  }

  async function exportSave(confirmed = false) {
    const err = validateRequired()
    if (err) {
      setError(err)
      setShowSaveRemind(false)
      return
    }
    if (!allRowsValidated) {
      setError('Validate every preview row (checkbox at end of each row) before saving to the database.')
      setShowSaveRemind(true)
      return
    }
    if (!payload) return
    setShowSaveRemind(false)
    setBusy(true)
    setError(null)
    try {
      if (!confirmed) {
        const compare = await fetch('/api/receipts/compare-export', {
          method: 'POST',
          credentials: 'include',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload),
        })
        const body = await readJson<{ error?: string; conflicts?: unknown[] }>(compare)
        if (!compare.ok) {
          setError(body.error || 'Compare failed.')
          return
        }
        if ((body.conflicts || []).length > 0) {
          setConflicts(body.conflicts as any[])
          return
        }
      }

      const res = await fetch('/api/receipts/export-save', {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      })
      if (!res.ok) {
        const body = await readJson<{ error?: string }>(res)
        setError(body.error || 'Export failed.')
        return
      }
      setConflicts(null)
      const saveHeader = res.headers.get('X-Save-Result')
      await downloadBlob(res, 'receipts.xlsx')
      setBatchId(null)
      setReceipts([])
      let saveSummary = 'Saved to database and Excel downloaded.'
      if (saveHeader) {
        try {
          const decoded = decodeURIComponent(saveHeader)
          const parts = Object.fromEntries(
            decoded.split(';').map((p) => {
              const [k, v] = p.split('=')
              return [k.trim(), v?.trim() ?? '']
            }),
          )
          saveSummary = `Saved to database (inserted ${parts.inserted ?? 0}, updated ${parts.updated ?? 0}, skipped ${parts.skipped ?? 0}, corrections ${parts.corrections ?? 0}). Excel downloaded.`
        } catch {
          /* keep default */
        }
      }
      setMessage(saveSummary)
    } finally {
      setBusy(false)
    }
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

        <section className="card">
          <div
            className="drop-zone"
            onDragOver={(e) => e.preventDefault()}
            onDrop={(e) => {
              e.preventDefault()
              onFilesChosen(e.dataTransfer.files)
            }}
          >
            <p className="drop-title">Drop receipts here</p>
            <p className="muted">or choose files / a folder below</p>
            <p className="file-count">{files.length ? `${files.length} file(s) selected` : 'No files selected'}</p>
          </div>
          <div className="row-actions">
            <label className="btn-ghost file-btn">
              Choose files
              <input
                type="file"
                multiple
                accept=".jpg,.jpeg,.png,.webp,.tif,.tiff,.bmp,.pdf,.csv,image/*,application/pdf,text/csv"
                onChange={(e) => onFilesChosen(e.target.files)}
              />
            </label>
            <label className="btn-ghost file-btn">
              Choose folder
              <input
                type="file"
                // @ts-expect-error webkitdirectory
                webkitdirectory=""
                multiple
                onChange={(e) => onFilesChosen(e.target.files)}
              />
            </label>
            <button
              type="button"
              className="btn-stamp"
              disabled={busy || files.length === 0}
              onClick={processUploads}
            >
              {busy ? 'Processing…' : 'Extract receipt data'}
            </button>
          </div>
        </section>

        {receipts.length > 0 && (
          <section className="card">
            <p className="toolbar-note">
              {user?.isDemo ? (
                <>
                  Demo mode: use the header checkboxes to choose Excel columns, then{' '}
                  <strong>Export Excel only</strong>. Saving to the database is disabled.
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
              {!user?.isDemo && (
                <button
                  type="button"
                  className="btn-stamp"
                  disabled={busy}
                  onClick={() => {
                    setError(null)
                    setShowSaveRemind(true)
                  }}
                >
                  Export Excel and save to database
                </button>
              )}
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
                    {headerWithExport('InvoiceNumber', 'col-invoice', 'Invoice Number')}
                    {headerWithExport('StoreName', 'col-store', 'Store Name')}
                    {headerWithExport('Currency', 'col-currency', 'Currency')}
                    {headerWithExport('Subtotal', 'col-money', 'Subtotal')}
                    {headerWithExport('GstHst', 'col-money', 'GST/HST')}
                    {headerWithExport('TotalAmount', 'col-money', 'Total Amount')}
                    {headerWithExport('ReceiptDate', 'col-date', 'Date')}
                    {headerWithExport('TransactionTime', 'col-time', 'Time')}
                    <th className="col-status">Status</th>
                    {!user?.isDemo && <th className="col-validate">Validate</th>}
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
                        {!user?.isDemo && (
                          <td className="validate-cell col-validate">
                            <input
                              type="checkbox"
                              className="row-validate-check"
                              checked={Boolean(r.validated)}
                              onChange={(e) => updateRow(i, { validated: e.target.checked })}
                              aria-label={`Validate row ${i + 1}`}
                              title="Validate this row"
                            />
                          </td>
                        )}
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

      {deleteConfirm && (
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
      )}

      {showSaveRemind && (
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
              {validatedCount} of {receipts.length} row(s) validated.
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
      )}

      {conflicts && (
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
      )}
    </>
  )
}
