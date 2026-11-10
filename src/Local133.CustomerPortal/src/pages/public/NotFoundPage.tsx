import { Link } from 'react-router-dom'
import Card from 'react-bootstrap/Card'

export default function NotFoundPage() {
  return (
    <Card body className="text-center">
      <h1 className="h4">Página no encontrada</h1>
      <p className="text-secondary">La dirección no existe.</p>
      <Link to="/">Ir al inicio</Link>
    </Card>
  )
}
