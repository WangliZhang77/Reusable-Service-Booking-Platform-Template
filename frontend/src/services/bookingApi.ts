import { apiFetch } from './httpClient'
import type {
  AvailabilityResponse,
  BookingResponse,
  CreateBookingPayload,
  ServiceItem,
  UpdateBookingStatusPayload,
} from './types'

export function getServices() {
  return apiFetch<ServiceItem[]>('/api/services')
}

export function getAvailability(serviceId: string, date: string) {
  const query = new URLSearchParams({ serviceId, date }).toString()
  return apiFetch<AvailabilityResponse>(`/api/availability?${query}`)
}

export function createBooking(payload: CreateBookingPayload) {
  return apiFetch<BookingResponse>('/api/bookings', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}

export function getBookings(date?: string) {
  const query = date ? `?${new URLSearchParams({ date }).toString()}` : ''
  return apiFetch<BookingResponse[]>(`/api/bookings${query}`)
}

export function updateBookingStatus(bookingId: string, payload: UpdateBookingStatusPayload) {
  return apiFetch<BookingResponse>(`/api/bookings/${bookingId}/status`, {
    method: 'PUT',
    body: JSON.stringify(payload),
  })
}
