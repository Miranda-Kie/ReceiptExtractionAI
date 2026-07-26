import { useState } from 'react'

interface ReceiptUploadSectionProps {
  files: File[];
  busy: boolean;
  onFilesChosen: (list: FileList | null) => void;
  processUploads: () => Promise<void>;
}

export function ReceiptUploadSection({
  files,
  busy,
  onFilesChosen,
  processUploads,
}: ReceiptUploadSectionProps) {
  return (
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
  )
}
