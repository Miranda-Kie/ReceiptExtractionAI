export type ReceiptRow = {
  receiptName: string
  storeName?: string | null
  invoiceNumber?: string | null
  currency?: string | null
  transactionTime?: string | null
  subtotal?: number | null
  gstHst?: number | null
  totalAmount?: number | null
  receiptDate?: string | null
  warnings?: string[]
  success?: boolean
  errorMessage?: string | null
  /** Client-only: user confirmed this preview row before DB save. */
  validated?: boolean
}

export type AiLearningState = {
  showToggle: boolean
  configured: boolean
  enabled: boolean
}

export type BatchState = {
  batchId: string
  receipts: ReceiptRow[]
  message?: string
  aiLearning: AiLearningState
}

export const EXPORT_FIELDS = [
  'InvoiceNumber',
  'StoreName',
  'Currency',
  'Subtotal',
  'GstHst',
  'TotalAmount',
  'ReceiptDate',
  'TransactionTime',
] as const

export type ExportField = (typeof EXPORT_FIELDS)[number]

/** Display labels for export field ids (API values stay PascalCase). */
export const EXPORT_FIELD_LABELS: Record<ExportField, string> = {
  InvoiceNumber: 'Invoice Number',
  StoreName: 'Store Name',
  Currency: 'Currency',
  Subtotal: 'Subtotal',
  GstHst: 'GST/HST',
  TotalAmount: 'Total Amount',
  ReceiptDate: 'Receipt Date',
  TransactionTime: 'Transaction Time',
}

export function exportFieldLabel(field: string): string {
  return EXPORT_FIELD_LABELS[field as ExportField] ?? field.replace(/([a-z])([A-Z])/g, '$1 $2')
}

export function toEditPayload(r: ReceiptRow) {
  return {
    invoiceNumber: r.invoiceNumber ?? '',
    storeName: r.storeName ?? '',
    currency: r.currency ?? '',
    transactionTime: r.transactionTime ?? '',
    subtotal: r.subtotal ?? null,
    gstHst: r.gstHst ?? null,
    totalAmount: r.totalAmount ?? null,
    receiptDate: r.receiptDate ?? null,
  }
}

export function moneyOk(sub?: number | null, gst?: number | null, total?: number | null) {
  if (sub == null || gst == null || total == null) return false
  return Math.abs(total - (sub + gst)) <= 0.02
}

export async function downloadBlob(res: Response, fallbackName: string) {
  const blob = await res.blob()
  const disposition = res.headers.get('Content-Disposition') || ''

  // Prefer RFC 5987 filename*; fall back to filename=. Never use values that
  // make browsers ignore the download attribute (which navigates away / blanks the SPA).
  let fileName = fallbackName
  const star = /filename\*=(?:UTF-8''|utf-8'')([^;\n]+)/i.exec(disposition)
  const plain = /filename="([^\"]+)"|filename=([^;\n]+)/i.exec(disposition)
  const raw = (star?.[1] || plain?.[1] || plain?.[2] || '').trim()
  if (raw) {
    try {
      const decoded = decodeURIComponent(raw.replace(/^UTF-8''/i, ''))
      const base = decoded.split(/[/\\]/).pop()?.trim() || ''
      if (base && /^[\w.\- ()]+\.(xlsx|xls|csv)$/i.test(base)) {
        fileName = base
      }
    } catch {
      /* keep fallbackName */
    }
  }

  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = fileName
  a.rel = 'noopener'
  a.style.display = 'none'
  document.body.appendChild(a)
  a.click()
  // Delay revoke so the browser can start the download before the blob URL is invalidated.
  window.setTimeout(() => {
    a.remove()
    URL.revokeObjectURL(url)
  }, 2000)
}
