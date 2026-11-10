import { NavLink, Outlet } from 'react-router-dom'
import Container from 'react-bootstrap/Container'
import Nav from 'react-bootstrap/Nav'
import Navbar from 'react-bootstrap/Navbar'
import { Icon, type IconName } from '@/components/Icon'

const tabs: { to: string; label: string; icon: IconName; end?: boolean }[] = [
  { to: '/app', label: 'Inicio', icon: 'home', end: true },
  { to: '/app/cobrar', label: 'Cobrar', icon: 'qr' },
  { to: '/app/ventas', label: 'Ventas', icon: 'list' },
  { to: '/app/mas', label: 'Más', icon: 'more' },
]

/** Portal del vendedor: móvil primero (pestañas inferiores); en ≥md las pestañas pasan a la barra superior. */
export default function MerchantLayout() {
  return (
    <>
      <Navbar bg="primary" data-bs-theme="dark" sticky="top">
        <Container>
          <Navbar.Brand as={NavLink} to="/app">
            Local133
          </Navbar.Brand>
          <Nav className="d-none d-md-flex">
            {tabs.map((t) => (
              <Nav.Link key={t.to} as={NavLink} to={t.to} end={t.end}>
                {t.label}
              </Nav.Link>
            ))}
          </Nav>
        </Container>
      </Navbar>

      <Container as="main" className="app-main pt-3">
        <Outlet />
      </Container>

      <Nav as="nav" className="tabbar fixed-bottom bg-body border-top d-md-none" justify aria-label="Navegación principal">
        {tabs.map((t) => (
          <Nav.Item key={t.to}>
            <Nav.Link as={NavLink} to={t.to} end={t.end}>
              <Icon name={t.icon} className="tab-icon" />
              {t.label}
            </Nav.Link>
          </Nav.Item>
        ))}
      </Nav>
    </>
  )
}
