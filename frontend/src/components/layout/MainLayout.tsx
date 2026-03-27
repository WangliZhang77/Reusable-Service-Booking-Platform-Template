import { Outlet } from 'react-router-dom'
import Header from './Header'
import Footer from './Footer'
import Chatbox from '../chat/Chatbox'

function MainLayout() {
  return (
    <div className="app-shell">
      <Header />
      <main>
        <Outlet />
      </main>
      <Chatbox />
      <Footer />
    </div>
  )
}

export default MainLayout
