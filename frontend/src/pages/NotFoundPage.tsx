import { Link } from 'react-router-dom'

function NotFoundPage() {
  return (
    <section className="section container page-top center">
      <h1>Page Not Found</h1>
      <p className="muted">The page you are looking for does not exist.</p>
      <Link to="/" className="btn btn-primary">Back to Home</Link>
    </section>
  )
}

export default NotFoundPage
