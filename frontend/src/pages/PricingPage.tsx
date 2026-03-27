import { Link } from 'react-router-dom'
import SectionTitle from '../components/common/SectionTitle'
import { pricingItems } from '../content/siteContent'

function PricingPage() {
  return (
    <section className="section container page-top">
      <SectionTitle
        eyebrow="Pricing"
        title="Clear pricing with no surprise add-ons"
        description="Pricing is a guide and may vary based on coat condition, temperament, and service complexity."
      />

      <div className="pricing-table">
        {pricingItems.map((item) => (
          <article key={item.name} className="price-row">
            <div>
              <h3 className="price-name">{item.name}</h3>
              <p className="muted">{item.duration}</p>
            </div>
            <strong>{item.price}</strong>
          </article>
        ))}
      </div>

      <div className="notice-box">
        <p>
          If your pet has matting or requires extra handling time, we will discuss any additional cost before we proceed.
        </p>
      </div>

      <div className="center">
        <Link to="/booking" className="btn btn-primary">Check Availability</Link>
      </div>
    </section>
  )
}

export default PricingPage
