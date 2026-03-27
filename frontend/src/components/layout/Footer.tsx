import { NavLink } from 'react-router-dom'
import { businessProfile } from '../../content/siteContent'

function Footer() {
  return (
    <footer className="site-footer">
      <div className="container footer-grid">
        <div>
          <h3>{businessProfile.name}</h3>
          <p className="muted">{businessProfile.tagline}</p>
        </div>
        <div>
          <h4>Contact</h4>
          <p>{businessProfile.phone}</p>
          <p>{businessProfile.email}</p>
          <p>{businessProfile.address}</p>
        </div>
        <div>
          <h4>Hours</h4>
          <p>{businessProfile.hoursSummary}</p>
          <NavLink to="/booking" className="btn btn-outline footer-btn">
            Book an Appointment
          </NavLink>
        </div>
      </div>
      <div className="footer-base">
        <p>{new Date().getFullYear()} {businessProfile.name}. All rights reserved.</p>
      </div>
    </footer>
  )
}

export default Footer
