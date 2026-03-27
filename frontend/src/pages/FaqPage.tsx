import { useEffect, useMemo, useState } from 'react'
import SectionTitle from '../components/common/SectionTitle'
import { getPublishedFaqs } from '../services/faqApi'
import type { FaqItem } from '../services/types'

function FaqPage() {
  const [items, setItems] = useState<FaqItem[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const load = async () => {
      setLoading(true)
      setError(null)
      try {
        const data = await getPublishedFaqs()
        setItems(data)
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to load FAQs.')
      } finally {
        setLoading(false)
      }
    }

    load()
  }, [])

  const grouped = useMemo(() => {
    const map = new Map<string, FaqItem[]>()
    for (const item of items) {
      const key = item.category?.trim() || 'General'
      const list = map.get(key) ?? []
      list.push(item)
      map.set(key, list)
    }
    return Array.from(map.entries()).map(([category, faqs]) => ({
      category,
      faqs: [...faqs].sort((a, b) => a.sortOrder - b.sortOrder),
    }))
  }, [items])

  return (
    <section className="section container page-top">
      <SectionTitle
        eyebrow="FAQ"
        title="Frequently Asked Questions"
        description="Find quick answers about services, pricing, appointments, and pet care."
      />

      {loading ? <p className="muted">Loading FAQs...</p> : null}
      {error ? <p className="form-error-global">{error}</p> : null}

      {!loading && !error && grouped.length === 0 ? (
        <p className="muted">No FAQ entries available yet.</p>
      ) : null}

      <div className="faq-groups">
        {grouped.map((group) => (
          <section key={group.category} className="card faq-group">
            <h3>{group.category}</h3>
            <div className="faq-list">
              {group.faqs.map((faq) => (
                <details key={faq.id} className="faq-item">
                  <summary>{faq.question}</summary>
                  <p>{faq.answer}</p>
                </details>
              ))}
            </div>
          </section>
        ))}
      </div>
    </section>
  )
}

export default FaqPage
