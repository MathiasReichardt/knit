/** The status page is older than `status_report.stale_days`. */
export function StaleDot({ generated, className = '' }: { generated: string | null; className?: string }) {
  const title = generated
    ? `Status page is stale: generated ${generated}`
    : 'Status page is stale: it has no Generated date'
  return (
    <span
      role="img"
      title={title}
      aria-label={title}
      className={`inline-block h-2 w-2 rounded-full bg-amber-500 ${className}`}
    />
  )
}
