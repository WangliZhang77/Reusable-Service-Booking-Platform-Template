import { apiFetch } from './httpClient'
import type { ChatRequestPayload, ChatResponsePayload } from './types'

export function sendChatMessage(payload: ChatRequestPayload) {
  return apiFetch<ChatResponsePayload>('/api/chat', {
    method: 'POST',
    body: JSON.stringify(payload),
  })
}
