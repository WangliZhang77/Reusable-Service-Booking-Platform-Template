import { Link } from 'react-router-dom'
import SectionTitle from '../components/common/SectionTitle'
import { businessProfile, pricingItems, serviceHighlights, testimonials } from '../content/siteContent'

function HomePage() {
  return (
    <>
      <section className="hero-section">
        <div className="container hero-grid">
          <div>
            <p className="eyebrow">Trusted Local Pet Grooming</p>
            <h1>Professional grooming that keeps your pet healthy and comfortable.</h1>
            <p className="lead">{businessProfile.tagline}</p>
            <div className="cta-row">
              <Link to="/booking" className="btn btn-primary">Book a Grooming Session</Link>
              <Link to="/services" className="btn btn-outline">View Services</Link>
            </div>
          </div>
          <div className="hero-card">
            <h3>Why pet owners choose us</h3>
            <ul>
              <li>Calm and clean salon environment</li>
              <li>Gentle handling with experienced groomers</li>
              <li>Clear pricing and practical care advice</li>
            </ul>
          </div>
        </div>
      </section>

      <section className="section container">
        <SectionTitle
          eyebrow="Services"
          title="Popular grooming services"
          description="Essential care options for routine maintenance and full grooming."
        />
        <div className="card-grid">
          {serviceHighlights.map((item) => (
            <article key={item.title} className="card">
              <h3>{item.title}</h3>
              <p className="muted">{item.description}</p>
            </article>
          ))}
        </div>
      </section>

      <section className="section section-soft">
        <div className="container">
          <SectionTitle
            eyebrow="Pricing"
            title="Simple, transparent pricing"
            description="Final price may vary by coat condition and behavior. We always confirm before starting."
          />
          <div className="pricing-teaser">
            {pricingItems.slice(0, 3).map((item) => (
              <div key={item.name} className="price-row">
                <div>
                  <p className="price-name">{item.name}</p>
                  <p className="muted">{item.duration}</p>
                </div>
                <strong>{item.price}</strong>
              </div>
            ))}
            <Link to="/pricing" className="btn btn-outline">See Full Pricing</Link>
          </div>
        </div>
      </section>

      <section className="section container">
        <SectionTitle eyebrow="Testimonials" title="What local pet owners say" />
        <div className="card-grid">
          {testimonials.map((item) => (
            <article key={item.author} className="card">
              <p>"{item.quote}"</p>
              <p className="muted testimonial-author">
                {item.author}, {item.suburb}
              </p>
            </article>
          ))}
        </div>
      </section>
    </>
  )
}

export default HomePage
