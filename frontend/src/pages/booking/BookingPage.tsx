import { useEffect, useMemo, useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import SectionTitle from '../../components/common/SectionTitle'
import { createBooking, getAvailability, getServices } from '../../services/bookingApi'
import type {
  AvailabilitySlot,
  CreateBookingPayload,
  ServiceItem,
} from '../../services/types'

type FormValues = {
  serviceId: string
  date: string
  slot: string
  name: string
  email: string
  phone: string
  petName: string
  petType: string
  breed: string
  size: string
  notes: string
}

type FormErrors = Partial<Record<keyof FormValues, string>>

const initialForm: FormValues = {
  serviceId: '',
  date: '',
  slot: '',
  name: '',
  email: '',
  phone: '',
  petName: '',
  petType: '',
  breed: '',
  size: '0',
  notes: '',
}

function BookingPage() {
  const [services, setServices] = useState<ServiceItem[]>([])
  const [slots, setSlots] = useState<AvailabilitySlot[]>([])
  const [form, setForm] = useState<FormValues>(initialForm)
  const [errors, setErrors] = useState<FormErrors>({})
  const [loadingServices, setLoadingServices] = useState(false)
  const [loadingSlots, setLoadingSlots] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [apiError, setApiError] = useState<string | null>(null)
  const [successMessage, setSuccessMessage] = useState<string | null>(null)

  useEffect(() => {
    const loadServices = async () => {
      setLoadingServices(true)
      setApiError(null)
      try {
        const data = await getServices()
        setServices(data)
      } catch (error) {
        setApiError(error instanceof Error ? error.message : 'Failed to load services.')
      } finally {
        setLoadingServices(false)
      }
    }

    loadServices()
  }, [])

  useEffect(() => {
    const canLoadSlots = form.serviceId && form.date
    if (!canLoadSlots) {
      setSlots([])
      return
    }

    const loadAvailability = async () => {
      setLoadingSlots(true)
      setApiError(null)
      setForm((prev) => ({ ...prev, slot: '' }))
      try {
        const data = await getAvailability(form.serviceId, form.date)
        setSlots(data.slots)
      } catch (error) {
        setSlots([])
        setApiError(error instanceof Error ? error.message : 'Failed to load available slots.')
      } finally {
        setLoadingSlots(false)
      }
    }

    loadAvailability()
  }, [form.serviceId, form.date])

  const availableSlots = useMemo(() => slots.filter((s) => s.isAvailable), [slots])

  const validate = (): FormErrors => {
    const nextErrors: FormErrors = {}
    if (!form.serviceId) nextErrors.serviceId = 'Please select a service.'
    if (!form.date) nextErrors.date = 'Please select a booking date.'
    if (!form.slot) nextErrors.slot = 'Please select an available time slot.'
    if (!form.name.trim()) nextErrors.name = 'Name is required.'
    if (!form.phone.trim()) nextErrors.phone = 'Phone number is required.'
    if (!form.petName.trim()) nextErrors.petName = 'Pet name is required.'
    if (!form.petType.trim()) nextErrors.petType = 'Pet type is required.'
    if (form.email && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(form.email)) {
      nextErrors.email = 'Please enter a valid email address.'
    }
    return nextErrors
  }

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault()
    setSuccessMessage(null)
    setApiError(null)

    const nextErrors = validate()
    setErrors(nextErrors)
    if (Object.keys(nextErrors).length > 0) return

    const payload: CreateBookingPayload = {
      serviceId: form.serviceId,
      bookingDate: form.date,
      startTime: form.slot,
      customerMessage: form.notes.trim() || undefined,
      customer: {
        fullName: form.name.trim(),
        phone: form.phone.trim(),
        email: form.email.trim() || undefined,
      },
      pet: {
        name: form.petName.trim(),
        species: form.petType.trim(),
        breed: form.breed.trim() || undefined,
        size: Number(form.size),
        specialNotes: form.notes.trim() || undefined,
      },
    }

    setSubmitting(true)
    try {
      await createBooking(payload)
      setSuccessMessage('Booking submitted successfully. We will confirm your appointment shortly.')
      setForm((prev) => ({ ...initialForm, serviceId: prev.serviceId, date: prev.date }))
      setSlots([])
    } catch (error) {
      setApiError(error instanceof Error ? error.message : 'Failed to submit booking.')
    } finally {
      setSubmitting(false)
    }
  }

  const setField = (field: keyof FormValues, value: string) => {
    setForm((prev) => ({ ...prev, [field]: value }))
    setErrors((prev) => ({ ...prev, [field]: undefined }))
  }

  return (
    <section className="section container page-top">
      <SectionTitle
        eyebrow="Booking"
        title="Book your pet grooming appointment"
        description="Select a service and date to view live availability, then complete your details."
      />

      <div className="booking-layout">
        <form className="card booking-form" onSubmit={handleSubmit} noValidate>
          <div className="form-grid">
            <FormField label="Service" error={errors.serviceId}>
              <select
                value={form.serviceId}
                onChange={(e) => setField('serviceId', e.target.value)}
                disabled={loadingServices}
              >
                <option value="">Select a service</option>
                {services.map((service) => (
                  <option key={service.id} value={service.id}>
                    {service.name} ({service.durationMinutes} mins)
                  </option>
                ))}
              </select>
            </FormField>

            <FormField label="Date" error={errors.date}>
              <input
                type="date"
                value={form.date}
                min={new Date().toISOString().split('T')[0]}
                onChange={(e) => setField('date', e.target.value)}
              />
            </FormField>

            <FormField label="Full Name" error={errors.name}>
              <input value={form.name} onChange={(e) => setField('name', e.target.value)} />
            </FormField>

            <FormField label="Email" error={errors.email}>
              <input
                type="email"
                value={form.email}
                onChange={(e) => setField('email', e.target.value)}
              />
            </FormField>

            <FormField label="Phone" error={errors.phone}>
              <input value={form.phone} onChange={(e) => setField('phone', e.target.value)} />
            </FormField>

            <FormField label="Pet Name" error={errors.petName}>
              <input value={form.petName} onChange={(e) => setField('petName', e.target.value)} />
            </FormField>

            <FormField label="Pet Type" error={errors.petType}>
              <input
                placeholder="e.g. Dog, Cat"
                value={form.petType}
                onChange={(e) => setField('petType', e.target.value)}
              />
            </FormField>

            <FormField label="Breed">
              <input value={form.breed} onChange={(e) => setField('breed', e.target.value)} />
            </FormField>

            <FormField label="Size">
              <select value={form.size} onChange={(e) => setField('size', e.target.value)}>
                <option value="0">Small</option>
                <option value="1">Medium</option>
                <option value="2">Large</option>
                <option value="3">Extra Large</option>
              </select>
            </FormField>

            <FormField label="Notes">
              <textarea
                rows={4}
                value={form.notes}
                onChange={(e) => setField('notes', e.target.value)}
              />
            </FormField>
          </div>

          {apiError ? <p className="form-error-global">{apiError}</p> : null}
          {successMessage ? <p className="form-success">{successMessage}</p> : null}

          <button className="btn btn-primary" type="submit" disabled={submitting || loadingSlots}>
            {submitting ? 'Submitting...' : 'Submit Booking'}
          </button>
        </form>

        <aside className="card booking-slots">
          <h3>Available time slots</h3>
          <p className="muted">Select a service and date first.</p>

          {loadingSlots ? <p className="muted">Loading slots...</p> : null}
          {!loadingSlots && form.serviceId && form.date && availableSlots.length === 0 ? (
            <p className="muted">No available slots for this date.</p>
          ) : null}

          <div className="slot-grid">
            {availableSlots.map((slot) => {
              const selected = form.slot === slot.startTime
              return (
                <button
                  type="button"
                  key={slot.startTime}
                  className={selected ? 'slot-btn selected' : 'slot-btn'}
                  onClick={() => setField('slot', slot.startTime)}
                >
                  {slot.startTime.slice(0, 5)} - {slot.endTime.slice(0, 5)}
                </button>
              )
            })}
          </div>
          {errors.slot ? <p className="field-error">{errors.slot}</p> : null}
        </aside>
      </div>
    </section>
  )
}

type FormFieldProps = {
  label: string
  error?: string
  children: ReactNode
}

function FormField({ label, error, children }: FormFieldProps) {
  return (
    <label className="form-field">
      <span>{label}</span>
      {children}
      {error ? <span className="field-error">{error}</span> : null}
    </label>
  )
}

export default BookingPage
