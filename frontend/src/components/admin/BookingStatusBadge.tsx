type BookingStatusBadgeProps = {
  status: number
}

const statusMap: Record<number, { label: string; className: string }> = {
  0: { label: 'Pending', className: 'status-pending' },
  1: { label: 'Confirmed', className: 'status-confirmed' },
  2: { label: 'Cancelled', className: 'status-cancelled' },
  3: { label: 'Completed', className: 'status-completed' },
}

function BookingStatusBadge({ status }: BookingStatusBadgeProps) {
  const entry = statusMap[status] ?? { label: 'Unknown', className: 'status-pending' }
  return <span className={`status-badge ${entry.className}`}>{entry.label}</span>
}

export default BookingStatusBadge
