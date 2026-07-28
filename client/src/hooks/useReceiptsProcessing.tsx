import { useEffect, useState } from 'react'
import { useAuth } from '../auth'
import { readJson } from '../http'
import { ReceiptRow } from '../types'

export function useReceiptsProcessing() {
  const { user } = useAuth()
  const [files, setFiles] = useState<File[]>([])
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [batchId, setBatchId] = useState<string | null>(null)
  const [receipts, setReceipts] = useState<ReceiptRow[]>([])

  useEffect(() => {
    // Always reset when the signed-in identity changes (including demo → demo).
    setBatchId(null)
    setReceipts([])
    setFiles([])
    setMessage(null)
    setError(null)

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
          ? `${data.message || 'Processing…'} Waiting for the Azure Function worker.`
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

  return {
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
  }
}
