import { apiFetch } from './httpClient'
import type { FaqItem } from './types'

export function getPublishedFaqs() {
  return apiFetch<FaqItem[]>('/api/faqs/published')
}
