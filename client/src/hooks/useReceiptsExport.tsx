import { useEffect, useMemo, useState } from 'react'
import { readJson } from '../http'
import { EXPORT_FIELDS, ReceiptRow, downloadBlob, moneyOk, toEditPayload } from '../types'

interface UseReceiptsExportProps {
  receipts: ReceiptRow[]
  batchId: string | null
  busy: boolean
  setError: (error: string | null) => void
  setMessage: (message: string | null) => void
  setBatchId: (batchId: string | null) => void
  isDemoUser: boolean | undefined
}

export function useReceiptsExport({
  receipts,
  batchId,
  busy,
  setError,
  setMessage,
  setBatchId,
  isDemoUser,
}: UseReceiptsExportProps) {
  const [exportFields, setExportFields] = useState<string[]>([...EXPORT_FIELDS])
  const [conflicts, setConflicts] = useState<any[] | null>(null)
  const [showSaveRemind, setShowSaveRemind] = useState(false)

  // Reset modal state when receipts are cleared (e.g. after logout/login).
  useEffect(() => {
    if (receipts.length === 0) {
      setConflicts(null)
      setShowSaveRemind(false)
    }
  }, [receipts.length])

  const allRowsValidated = receipts.length > 0 && receipts.every((r) => Boolean(r.validated))
  const validatedCount = receipts.filter((r) => r.validated).length

  function toggleExportField(field: string, checked: boolean) {
    setExportFields((prev) => (checked ? [...prev, field] : prev.filter((x) => x !== field)))
  }

  function validateRequired(): string | null {
    const validatedReceipts = receipts.filter((r) => r.validated)
    const missing: { [key: string]: number[] } = {
      invoice: [],
      store: [],
      currency: [],
      subtotal: [],
      tax: [],
      total: [],
      date: [],
      time: [],
    }

    validatedReceipts.forEach((r) => {
      const originalIndex = receipts.indexOf(r) + 1
      if (!r.invoiceNumber?.trim()) missing.invoice.push(originalIndex)
      if (!r.storeName?.trim()) missing.store.push(originalIndex)
      if (!r.currency?.trim()) missing.currency.push(originalIndex)
      if (r.subtotal === null || r.subtotal === undefined) missing.subtotal.push(originalIndex)
      if (r.gstHst === null || r.gstHst === undefined) missing.tax.push(originalIndex)
      if (r.totalAmount === null || r.totalAmount === undefined) missing.total.push(originalIndex)
      if (!r.receiptDate) missing.date.push(originalIndex)
      if (!r.transactionTime?.trim()) missing.time.push(originalIndex)
    })

    const parts: string[] = []
    if (missing.invoice.length) parts.push(`missing InvoiceNumber on row(s) ${missing.invoice.join(', ')}`)
    if (missing.store.length) parts.push(`missing StoreName on row(s) ${missing.store.join(', ')}`)
    if (missing.currency.length) parts.push(`missing Currency on row(s) ${missing.currency.join(', ')}`)
    if (missing.subtotal.length) parts.push(`missing Subtotal on row(s) ${missing.subtotal.join(', ')}`)
    if (missing.tax.length) parts.push(`missing Tax on row(s) ${missing.tax.join(', ')}`)
    if (missing.total.length) parts.push(`missing Total Amount on row(s) ${missing.total.join(', ')}`)
    if (missing.date.length) parts.push(`missing Date on row(s) ${missing.date.join(', ')}`)
    if (missing.time.length) parts.push(`missing Time on row(s) ${missing.time.join(', ')}`)

    if (Object.values(missing).every((arr) => arr.length === 0)) return null
    return `Cannot export: ${parts.join('; ')}.`
  }

  async function exportOnly() {
    const err = validateRequired()
    if (err) {
      setError(err)
      return
    }
    const validatedReceipts = receipts.filter((r) => r.validated)
    if (validatedReceipts.length === 0) {
      setError('No validated rows to export. Validate rows using the checkbox at the end of each row.')
      return
    }

    if (!batchId) return
    setError(null)
    try {
      const res = await fetch('/api/receipts/export-only', {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          batchId,
          receipts: validatedReceipts.map(toEditPayload),
          exportFields,
          previewValidated: true,
        }),
      })
      if (!res.ok) {
        const body = await readJson<{ error?: string }>(res)
        setError(body.error || 'Export failed.')
        return
      }
      await downloadBlob(res, 'receipts.xlsx')
      setMessage('Excel downloaded. You can export again or upload more receipts.')
    } finally {
      // setBusy(false) // Busy state is managed by parent or useReceiptsProcessing
    }
  }

  async function exportSave(confirmed = false) {
    const err = validateRequired()
    if (err) {
      setError(err)
      setShowSaveRemind(false)
      return
    }
    const validatedReceipts = receipts.filter((r) => r.validated)
    if (validatedReceipts.length === 0) {
      setError('No validated rows to save. Validate rows using the checkbox at the end of each row.')
      setShowSaveRemind(true)
      return
    }
    if (!batchId) return
    setShowSaveRemind(false)
    setError(null)
    try {
      if (!confirmed) {
        const payload = {
          batchId,
          receipts: validatedReceipts.map(toEditPayload),
          exportFields,
          previewValidated: true,
        }
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

      const payload = {
        batchId,
        receipts: validatedReceipts.map(toEditPayload),
        exportFields,
        previewValidated: true,
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
      await downloadBlob(res, 'receipts.xlsx')
      setMessage('Saved to database and Excel downloaded. You can export again or upload more receipts.')
    } finally {
      // setBusy(false) // Busy state is managed by parent or useReceiptsProcessing
    }
  }

  return {
    exportFields,
    setExportFields,
    conflicts,
    setConflicts,
    showSaveRemind,
    setShowSaveRemind,
    allRowsValidated,
    validatedCount,
    toggleExportField,
    exportOnly,
    exportSave,
    validateRequired,
  }
}
