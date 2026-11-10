import { useState } from 'react'
import { NavLink, Outlet } from 'react-router-dom'
import Container from 'react-bootstrap/Container'
import Nav from 'react-bootstrap/Nav'
import Navbar from 'react-bootstrap/Navbar'
import Offcanvas from 'react-bootstrap/Offcanvas'

const links: { to: string; label: string; end?: boolean }[] = [
  { to: '/admin', label: 'Tablero', end: true },
  { to: '/admin/clientes', label: 'Clientes' },
  { to: '/admin/transacciones', label: 'Transacciones' },
  { to: '/admin/parametros', label: 'Parámetros' },
]

/** Portal administrador: menú en Offcanvas en móvil; en ≥lg el mismo Offcanvas se muestra como sidebar fijo. */
export default function AdminLayout() {
  const [open, setOpen] = useState(false)
  const close = () => setOpen(false)

  return (
    <>
      <Navbar bg="dark" data-bs-theme="dark" sticky="top">
        <Container fluid>
          <Navbar.Toggle className="d-lg-none me-2" aria-controls="admin-menu" onClick={() => setOpen(true)} />
          <Navbar.Brand as={NavLink} to="/admin" className="me-auto">
            Local133 · Admin
          </Navbar.Brand>
        </Container>
      </Navbar>

      <div className="d-lg-flex">
        <Offcanvas id="admin-menu" show={open} onHide={close} responsive="lg" className="admin-sidebar bg-body border-end">
          <Offcanvas.Header closeButton>
            <Offcanvas.Title>Menú</Offcanvas.Title>
          </Offcanvas.Header>
          <Offcanvas.Body className="p-2">
            <Nav className="flex-column w-100" variant="pills">
              {links.map((l) => (
                <Nav.Link key={l.to} as={NavLink} to={l.to} end={l.end} onClick={close}>
                  {l.label}
                </Nav.Link>
              ))}
            </Nav>
          </Offcanvas.Body>
        </Offcanvas>

        <main className="flex-grow-1 p-3 p-lg-4">
          <Outlet />
        </main>
      </div>
    </>
  )
}
