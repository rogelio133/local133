import Badge from 'react-bootstrap/Badge'
import { useHealth } from '@/api/health'

/** Indicador de conectividad con la API (GET /api/health a través del proxy en dev). */
export function ApiStatus() {
  const { data, isPending, isError } = useHealth()

  if (isPending) return <Badge bg="secondary">API: verificando…</Badge>
  if (isError) return <Badge bg="danger">API: sin conexión</Badge>
  return <Badge bg={data.status === 'Healthy' ? 'success' : 'warning'}>API: {data.status}</Badge>
}
