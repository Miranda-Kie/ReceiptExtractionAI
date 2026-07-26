import { useMemo, useState } from 'react'
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

  const allRowsValidated = receipts.length > 0 && receipts.every((r) => Boolean(r.validated))
  const validatedCount = receipts.filter((r) => r.validated).length

  function toggleExportField(field: string, checked: boolean) {
    setExportFields((prev) => (checked ? [...prev, field] : prev.filter((x) => x !== field)))
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
      setBatchId(null)
      setMessage('Excel downloaded. Upload receipts again to start a new preview.')
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
      const saveHeader = res.headers.get('X-Save-Result')
      await downloadBlob(res, 'receipts.xlsx')
      setBatchId(null)
      setMessage('Saved to database and Excel downloaded.')
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
