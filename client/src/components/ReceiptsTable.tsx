import React from 'react'
import { ReceiptRow, moneyOk, normalizeTimeValue, toTimeInputValue } from '../types'

interface ReceiptsTableProps {
  receipts: ReceiptRow[]
  userIsDemo: boolean | undefined
  exportFields: string[]
  selectedForExport: boolean[]
  busy: boolean
  updateRow: (index: number, patch: Partial<ReceiptRow>) => void
  toggleExportField: (field: string, checked: boolean) => void
  toggleExportSelection: (index: number, checked: boolean) => void
  toggleAllExportSelection: (checked: boolean) => void
  requestDeleteRow: (index: number) => void
}

export function ReceiptsTable({
  receipts,
  userIsDemo,
  exportFields,
  selectedForExport,
  busy,
  updateRow,
  toggleExportField,
  toggleExportSelection,
  toggleAllExportSelection,
  requestDeleteRow,
}: ReceiptsTableProps) {
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
    <section className="card">
      <p className="toolbar-note">
        {userIsDemo ? (
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

      {/* This section will be extracted into a separate component later */}
      {/* <div className="row-actions wrap">...</div> */}

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
              {!userIsDemo && <th className="col-validate">Validate</th>}
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
                  {!userIsDemo && (
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
  )
}
