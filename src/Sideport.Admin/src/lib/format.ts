import { formatDistanceToNowStrict, formatDistanceStrict } from 'date-fns'

export function shortDateTime(value?: string): string {
  if (!value) return 'Unknown'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return 'Unknown'
  return new Intl.DateTimeFormat('en', {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(date)
}

export function relativeTime(value?: string): string {
  if (!value) return 'Unknown'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return 'Unknown'
  return `${formatDistanceToNowStrict(date, { addSuffix: true })}`
}

export function timeUntil(value?: string): string {
  if (!value) return 'Unknown'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return 'Unknown'
  const now = new Date('2026-06-07T21:00:00Z')
  if (date.getTime() < now.getTime()) return `Expired ${formatDistanceStrict(date, now)} ago`
  return `${formatDistanceStrict(now, date)} left`
}

export function compactUdid(udid: string): string {
  if (udid.length <= 14) return udid
  return `${udid.slice(0, 8)}...${udid.slice(-6)}`
}

export function sourceLabel(source: string): string {
  if (source === 'live') return 'Live API'
  if (source === 'derived') return 'Derived'
  if (source === 'planned') return 'Pending'
  return 'Demo'
}
