import { useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import { sendChatMessage } from '../../services/chatApi'

type ChatMessage = {
  id: string
  role: 'user' | 'assistant'
  text: string
  intent?: string
  /** 已点击「确认预约」并成功提交，避免重复写入 admin */
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
      text: 'Hi! Ask me about FAQ, pricing, availability, or booking.',
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

  /** 仅通过按钮提交：写入数据库 / admin 列表在此步发生 */
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
                  <div className="chat-message-body">{msg.text}</div>
                  {tokenPayload ? (
                    <button
                      type="button"
                      className="btn btn-primary chatbox-confirm-btn"
                      disabled={sending}
                      onClick={() => void sendConfirmation(msg.id, tokenPayload)}
                    >
                      确认预约（提交到后台）
                    </button>
                  ) : null}
                </div>
              )
            })}
          </div>
          {pendingBookingConfirm ? (
            <p className="chatbox-pending-hint">
              信息已齐全。请点击上一条消息里的「确认预约」后，预约才会出现在管理后台；仅发送文字不会完成提交。
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
