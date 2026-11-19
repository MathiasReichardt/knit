import { Link } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { AlertTriangle } from 'lucide-react'
import { doctorApi, doctorQk, verifyIssueCount } from '../api/doctor'
import { brokenMounts, mountsApi, mountsQk } from '../api/mounts'

// Top-bar chip that surfaces repo-health issues at a glance. Shares the
// Doctor screen's query key, so a "Run verify" there refreshes the chip
// and vice-versa. Renders nothing on the all-clear / loading / error
// paths — the absence of the chip *is* the "all good" signal.
export function DoctorChip() {
  const verify = useQuery({
    queryKey: doctorQk.verify,
    queryFn: doctorApi.verify,
    staleTime: 60_000,
    retry: false,
  })
  const mounts = useQuery({ queryKey: mountsQk.list(), queryFn: mountsApi.list, staleTime: Infinity })

  const count = verify.data ? verifyIssueCount(verify.data) + brokenMounts(mounts.data?.mounts).length : 0
  if (count === 0) return null

  return (
    <Link
      to="/doctor"
      title={`${count} repo health issue${count === 1 ? '' : 's'} — open Doctor`}
      aria-label={`${count} repo health issue${count === 1 ? '' : 's'}, open Doctor`}
      className="inline-flex items-center gap-1.5 h-7 rounded-full border border-amber-300 dark:border-amber-700 bg-amber-50 dark:bg-amber-950/40 px-2.5 text-xs font-medium text-amber-700 dark:text-amber-300 hover:bg-amber-100 dark:hover:bg-amber-900/50"
    >
      <AlertTriangle className="h-3.5 w-3.5" aria-hidden />
      {count}
    </Link>
  )
}
