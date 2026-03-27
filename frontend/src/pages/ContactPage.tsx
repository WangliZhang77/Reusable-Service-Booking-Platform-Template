import SectionTitle from '../components/common/SectionTitle'
import { businessProfile } from '../content/siteContent'

function ContactPage() {
  return (
    <section className="section container page-top">
      <SectionTitle
        eyebrow="Contact"
        title="Get in touch with our team"
        description="Call or email us for service questions, special care requests, or appointment support."
      />

      <div className="two-col">
        <article className="card">
          <h3>Contact Details</h3>
          <p><strong>Phone:</strong> {businessProfile.phone}</p>
          <p><strong>Email:</strong> {businessProfile.email}</p>
          <p><strong>Address:</strong> {businessProfile.address}</p>
          <p><strong>Hours:</strong> {businessProfile.hoursSummary}</p>
        </article>

        <article className="card">
          <h3>Before your visit</h3>
          <ul className="list">
            <li>Please let us know if your pet has skin sensitivities or anxiety.</li>
            <li>Arrive 5 minutes early for your first appointment.</li>
            <li>Keep contact details current so we can reach you during the service.</li>
          </ul>
        </article>
      </div>
    </section>
  )
}

export default ContactPage
