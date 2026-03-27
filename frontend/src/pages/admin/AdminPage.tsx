import { useEffect, useMemo, useState } from 'react'
import SectionTitle from '../../components/common/SectionTitle'
import BookingTable from '../../components/admin/BookingTable'
import { getBookings, updateBookingStatus } from '../../services/bookingApi'
import type { BookingResponse } from '../../services/types'

const statusFilters = [
  { value: 'all', label: 'All' },
  { value: '0', label: 'Pending' },
  { value: '1', label: 'Confirmed' },
  { value: '2', label: 'Cancelled' },
  { value: '3', label: 'Completed' },
] as const

function AdminPage() {
  const [bookings, setBookings] = useState<BookingResponse[]>([])
  const [statusFilter, setStatusFilter] = useState<(typeof statusFilters)[number]['value']>('all')
  const [loading, setLoading] = useState(false)
  const [updatingBookingId, setUpdatingBookingId] = useState<string | null>(null)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [infoMessage, setInfoMessage] = useState<string | null>(null)

  const loadBookings = async () => {
    setLoading(true)
    setErrorMessage(null)
    try {
      const data = await getBookings()
      setBookings(data)
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Failed to load bookings.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    loadBookings()
  }, [])

  const filteredBookings = useMemo(() => {
    if (statusFilter === 'all') return bookings
    return bookings.filter((booking) => String(booking.status) === statusFilter)
  }, [bookings, statusFilter])

  const handleUpdateStatus = async (bookingId: string, nextStatus: number) => {
    setInfoMessage(null)
    setErrorMessage(null)
    setUpdatingBookingId(bookingId)
    try {
      const updated = await updateBookingStatus(bookingId, { status: nextStatus })
      setBookings((prev) => prev.map((booking) => (booking.id === bookingId ? updated : booking)))
      setInfoMessage('Booking status updated successfully.')
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Failed to update booking status.')
    } finally {
      setUpdatingBookingId(null)
    }
  }

  return (
    <section className="section container page-top">
      <SectionTitle
        eyebrow="Admin"
        title="Booking Management (MVP)"
        description="Internal view for daily booking operations. Authentication can be added as a route guard later."
      />

      <div className="card admin-panel">
        <div className="admin-toolbar">
          <label className="form-field">
            <span>Status Filter</span>
            <select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value as typeof statusFilter)}>
              {statusFilters.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>
          <button className="btn btn-outline" type="button" onClick={loadBookings} disabled={loading}>
            {loading ? 'Refreshing...' : 'Refresh'}
          </button>
        </div>

        {errorMessage ? <p className="form-error-global">{errorMessage}</p> : null}
        {infoMessage ? <p className="form-success">{infoMessage}</p> : null}

        {loading ? (
          <p className="muted">Loading bookings...</p>
        ) : (
          <BookingTable
            bookings={filteredBookings}
            updatingBookingId={updatingBookingId}
            onUpdateStatus={handleUpdateStatus}
          />
        )}
      </div>
    </section>
  )
}

export default AdminPage
