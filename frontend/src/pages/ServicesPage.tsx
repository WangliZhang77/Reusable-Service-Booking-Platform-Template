import { Link } from 'react-router-dom'
import SectionTitle from '../components/common/SectionTitle'
import { serviceHighlights } from '../content/siteContent'

const detailedServices = [
  {
    name: 'Full Grooming Package',
    details: 'Complete grooming including wash, dry, clipping, styling, ear clean, and sanitary trim.',
    suitableFor: 'Regular grooming every 6-10 weeks',
  },
  {
    name: 'Wash, Blow-Dry & Brush',
    details: 'Ideal between full grooms to keep coat clean and reduce matting.',
    suitableFor: 'Active pets and longer coats',
  },
  {
    name: 'Puppy Intro Groom',
    details: 'Gentle first appointment focused on comfort, handling, and familiarization.',
    suitableFor: 'Puppies under 6 months',
  },
]

function ServicesPage() {
  return (
    <section className="section container page-top">
      <SectionTitle
        eyebrow="Our Services"
        title="Practical grooming options for every stage of pet care"
        description="Choose a routine that suits your pet's coat type, age, and comfort level."
      />

      <div className="card-grid">
        {detailedServices.map((service) => (
          <article className="card" key={service.name}>
            <h3>{service.name}</h3>
            <p>{service.details}</p>
            <p className="muted"><strong>Best for:</strong> {service.suitableFor}</p>
          </article>
        ))}
      </div>

      <div className="highlight-row">
        {serviceHighlights.map((item) => (
          <div key={item.title} className="mini-highlight">
            <h4>{item.title}</h4>
            <p className="muted">{item.description}</p>
          </div>
        ))}
      </div>

      <div className="center">
        <Link to="/booking" className="btn btn-primary">Book a Service</Link>
      </div>
    </section>
  )
}

export default ServicesPage
