import { useState } from 'react'
import type { FormEvent } from 'react'
import { sendChatMessage } from '../../services/chatApi'

type ChatMessage = {
  role: 'user' | 'assistant'
  text: string
}

function Chatbox() {
  const [open, setOpen] = useState(false)
  const [input, setInput] = useState('')
  const [sending, setSending] = useState(false)
  const [messages, setMessages] = useState<ChatMessage[]>([
    { role: 'assistant', text: 'Hi! Ask me about FAQ, pricing, availability, or booking.' },
  ])

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault()
    const content = input.trim()
    if (!content) return

    setMessages((prev) => [...prev, { role: 'user', text: content }])
    setInput('')
    setSending(true)

    try {
      const response = await sendChatMessage({ message: content })
      setMessages((prev) => [...prev, { role: 'assistant', text: response.reply }])
    } catch (error) {
      const text = error instanceof Error ? error.message : 'Chat request failed.'
      setMessages((prev) => [...prev, { role: 'assistant', text }])
    } finally {
      setSending(false)
    }
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
            {messages.map((msg, index) => (
              <div key={`${msg.role}-${index}`} className={`chat-message ${msg.role}`}>
                {msg.text}
              </div>
            ))}
          </div>
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
