import { useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import { sendChatMessage } from '../../services/chatApi'

type ChatMessage = {
  id: string
  role: 'user' | 'assistant'
  text: string
  intent?: string
  /** After Confirm booking succeeds, hide duplicate submit */
  confirmConsumed?: boolean
}

/** Matches assistant "Yes, confirm <base64>" lines (user may paste multiline). */
const CONFIRM_LINE_RE = /yes, confirm\s+([A-Za-z0-9+/=]+)/i

function extractConfirmationPayload(text: string): string | null {
  const m = text.match(CONFIRM_LINE_RE)
  return m ? `Yes, confirm ${m[1]}` : null
}

function newId() {
  return typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : `m-${Date.now()}-${Math.random().toString(16).slice(2)}`
}

function isBookingConfirmationIntent(intent: string | undefined) {
  return (intent ?? '').toLowerCase() === 'booking_confirmation'
}

/** Hide long base64 in UI; full text stays in state for the confirm button payload. */
function displayAssistantBody(text: string, intent: string | undefined): string {
  if (!isBookingConfirmationIntent(intent)) return text
  return text.replace(
    CONFIRM_LINE_RE,
    'Use the Confirm booking button below to save this appointment — you do not need to copy any code.'
  )
}

function displayUserBody(text: string): string {
  return extractConfirmationPayload(text) !== null ? 'Confirm booking' : text
}

function Chatbox() {
  const [open, setOpen] = useState(false)
  const [input, setInput] = useState('')
  const [sending, setSending] = useState(false)
  const [sessionId] = useState(() => {
    const key = 'chat_session_id'
    const existing = localStorage.getItem(key)
    if (existing) return existing

    const id =
      typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
        ? crypto.randomUUID()
        : `sess-${Date.now()}-${Math.random().toString(16).slice(2)}`

    localStorage.setItem(key, id)
    return id
  })
  const [messages, setMessages] = useState<ChatMessage[]>([
    {
      id: 'intro',
      role: 'assistant',
      text: 'Hi — I can help you explore services, compare options, check prices and open times, or walk through a booking. What brings you in today?',
    },
  ])

  const pendingBookingConfirm = useMemo(() => {
    const last = messages[messages.length - 1]
    if (!last || last.role !== 'assistant') return null
    if (last.confirmConsumed || !isBookingConfirmationIntent(last.intent)) return null
    const payload = extractConfirmationPayload(last.text)
    return payload ? { assistantId: last.id, payload } : null
  }, [messages])

  const sendUserText = async (content: string) => {
    const trimmed = content.trim()
    if (!trimmed) return

    setMessages((prev) => [...prev, { id: newId(), role: 'user', text: trimmed }])
    setSending(true)

    try {
      const response = await sendChatMessage({ message: trimmed, sessionId })
      setMessages((prev) => [
        ...prev,
        {
          id: newId(),
          role: 'assistant',
          text: response.reply,
          intent: response.intent,
        },
      ])
    } catch (error) {
      const text = error instanceof Error ? error.message : 'Chat request failed.'
      setMessages((prev) => [...prev, { id: newId(), role: 'assistant', text }])
    } finally {
      setSending(false)
    }
  }

  /** Button sends the real Yes, confirm <token> line to the API */
  const sendConfirmation = async (assistantId: string, payload: string) => {
    setMessages((prev) => [...prev, { id: newId(), role: 'user', text: payload }])
    setSending(true)

    try {
      const response = await sendChatMessage({ message: payload, sessionId })
      setMessages((prev) => [
        ...prev.map((m) => (m.id === assistantId ? { ...m, confirmConsumed: true } : m)),
        {
          id: newId(),
          role: 'assistant',
          text: response.reply,
          intent: response.intent,
        },
      ])
    } catch (error) {
      const text = error instanceof Error ? error.message : 'Chat request failed.'
      setMessages((prev) => [...prev, { id: newId(), role: 'assistant', text }])
    } finally {
      setSending(false)
    }
  }

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault()
    const content = input.trim()
    if (!content) return

    setInput('')
    await sendUserText(content)
  }

  return (
    <div className="chatbox-root">
      {open ? (
        <div className="chatbox-panel card">
          <div className="chatbox-header">
            <strong>Assistant</strong>
            <button type="button" onClick={() => setOpen(false)}>Close</button>
          </div>
          <div className="chatbox-messages">
            {messages.map((msg) => {
              const tokenPayload =
                msg.role === 'assistant' &&
                !msg.confirmConsumed &&
                isBookingConfirmationIntent(msg.intent)
                  ? extractConfirmationPayload(msg.text)
                  : null

              return (
                <div key={msg.id} className={`chat-message ${msg.role}`}>
                  <div className="chat-message-body">
                    {msg.role === 'user' ? displayUserBody(msg.text) : displayAssistantBody(msg.text, msg.intent)}
                  </div>
                  {tokenPayload ? (
                    <button
                      type="button"
                      className="btn btn-primary chatbox-confirm-btn"
                      disabled={sending}
                      onClick={() => void sendConfirmation(msg.id, tokenPayload)}
                    >
                      Confirm booking
                    </button>
                  ) : null}
                </div>
              )
            })}
          </div>
          {pendingBookingConfirm ? (
            <p className="chatbox-pending-hint">
              Your details look complete. Tap <strong>Confirm booking</strong> on the message above to save it to the
              admin schedule — typing a normal message alone will not finalize the appointment.
            </p>
          ) : null}
          <form className="chatbox-input" onSubmit={onSubmit}>
            <input
              value={input}
              onChange={(e) => setInput(e.target.value)}
              placeholder="Ask a question..."
              disabled={sending}
            />
            <button type="submit" className="btn btn-primary" disabled={sending}>
              {sending ? '...' : 'Send'}
            </button>
          </form>
        </div>
      ) : null}

      <button type="button" className="chatbox-toggle btn btn-primary" onClick={() => setOpen((v) => !v)}>
        {open ? 'Hide Chat' : 'Chat'}
      </button>
    </div>
  )
}

export default Chatbox
