export interface ServiceItem {
  id: string
  name: string
  description?: string | null
  durationMinutes: number
  price: number
}

export interface AvailabilitySlot {
  startTime: string
  endTime: string
  isAvailable: boolean
}

export interface AvailabilityResponse {
  serviceId: string
  date: string
  slots: AvailabilitySlot[]
}

export interface BookingStatusResponse {
  id: string
  status: number
}

export interface CreateBookingPayload {
  serviceId: string
  bookingDate: string
  startTime: string
  customerMessage?: string
  customer: {
    fullName: string
    phone: string
    email?: string
  }
  pet: {
    name: string
    species: string
    breed?: string
    size: number
    specialNotes?: string
  }
}

export interface BookingResponse {
  id: string
  serviceId: string
  serviceName: string
  customerId?: string
  customerName?: string
  customerPhone?: string
  petId?: string
  petName?: string
  bookingDate: string
  startTime: string
  endTime: string
  status: number
  customerMessage?: string | null
  adminNotes?: string | null
  createdAt?: string
}

export interface UpdateBookingStatusPayload {
  status: number
  adminNotes?: string
  cancelReason?: string
}

export interface FaqItem {
  id: string
  question: string
  answer: string
  category?: string | null
  sortOrder: number
  isPublished: boolean
}

export interface ChatRequestPayload {
  message: string
  sessionId?: string
}

export interface ChatResponsePayload {
  reply: string
  intent: string
}
