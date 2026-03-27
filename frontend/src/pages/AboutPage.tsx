import SectionTitle from '../components/common/SectionTitle'
import { aboutPoints } from '../content/siteContent'

function AboutPage() {
  return (
    <section className="section container page-top">
      <SectionTitle
        eyebrow="About Us"
        title="A local team focused on quality care and consistent grooming results"
        description="We are a small Auckland-based grooming studio serving dogs and cats with practical, compassionate care."
      />

      <div className="two-col">
        <article className="card">
          <h3>Our approach</h3>
          <p>
            We prioritize your pet's comfort, safety, and hygiene while delivering a clean, polished finish.
            Every visit is handled with patience and clear communication.
          </p>
        </article>

        <article className="card">
          <h3>What to expect</h3>
          <ul className="list">
            {aboutPoints.map((point) => (
              <li key={point}>{point}</li>
            ))}
          </ul>
        </article>
      </div>
    </section>
  )
}

export default AboutPage
