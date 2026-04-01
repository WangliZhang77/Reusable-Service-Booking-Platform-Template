import BookingStatusBadge from './BookingStatusBadge'
import type { BookingResponse } from '../../services/types'

type BookingTableProps = {
  bookings: BookingResponse[]
  updatingBookingId: string | null
  onUpdateStatus: (bookingId: string, nextStatus: number) => void
}

function BookingTable({ bookings, updatingBookingId, onUpdateStatus }: BookingTableProps) {
  if (bookings.length === 0) {
    return <p className="muted">No bookings found for the selected filter.</p>
  }

  return (
    <div className="admin-table-wrap">
      <table className="admin-table">
        <thead>
          <tr>
            <th>Date</th>
            <th>Time</th>
            <th>Service</th>
            <th>Customer</th>
            <th>Pet</th>
            <th>Status</th>
            <th>Action</th>
          </tr>
        </thead>
        <tbody>
          {bookings.map((booking) => (
            <tr key={booking.id}>
              <td>{booking.bookingDate}</td>
              <td>
                {booking.startTime.slice(0, 5)} - {booking.endTime.slice(0, 5)}
              </td>
              <td>{booking.serviceName}</td>
              <td>
                <div className="muted">
                  {booking.customerName != null && booking.customerName !== '' ? booking.customerName : 'null'}
                </div>
                <small className="muted">{booking.customerPhone ?? ''}</small>
              </td>
              <td>{booking.petName ?? '-'}</td>
              <td>
                <BookingStatusBadge status={booking.status} />
              </td>
              <td>
                <select
                  value={booking.status}
                  onChange={(e) => onUpdateStatus(booking.id, Number(e.target.value))}
                  disabled={updatingBookingId === booking.id}
                >
                  <option value={0}>Pending</option>
                  <option value={1}>Confirmed</option>
                  <option value={2}>Cancelled</option>
                  <option value={3}>Completed</option>
                </select>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

export default BookingTable
