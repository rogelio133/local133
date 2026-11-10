import Spinner from 'react-bootstrap/Spinner'

export function PageSpinner() {
  return (
    <div className="d-flex justify-content-center py-5">
      <Spinner animation="border" role="status">
        <span className="visually-hidden">Cargando…</span>
      </Spinner>
    </div>
  )
}
