import { isRouteErrorResponse, Link, useRouteError } from 'react-router-dom'
import Alert from 'react-bootstrap/Alert'
import Container from 'react-bootstrap/Container'

export function RouteError() {
  const error = useRouteError()
  const message = isRouteErrorResponse(error)
    ? `${error.status} ${error.statusText}`
    : error instanceof Error
      ? error.message
      : 'Error inesperado'

  return (
    <Container className="py-5" style={{ maxWidth: 480 }}>
      <Alert variant="danger">
        <Alert.Heading className="h5">Algo salió mal</Alert.Heading>
        <p className="mb-0">{message}</p>
      </Alert>
      <Link to="/">Volver al inicio</Link>
    </Container>
  )
}
