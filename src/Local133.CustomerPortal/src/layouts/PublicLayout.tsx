import { Link, Outlet } from 'react-router-dom'
import Container from 'react-bootstrap/Container'

/** Páginas sin sesión (login, registro, recibo público): columna centrada. */
export default function PublicLayout() {
  return (
    <Container as="main" className="py-4" style={{ maxWidth: 480 }}>
      <Link to="/" className="d-block text-center h4 mb-4 text-decoration-none">
        Local133
      </Link>
      <Outlet />
    </Container>
  )
}
