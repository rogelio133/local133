import Card from 'react-bootstrap/Card'
import ListGroup from 'react-bootstrap/ListGroup'
import { ApiStatus } from '@/components/ApiStatus'

// Checklist estático; en Fase 1 se alimenta del estatus real del Merchant.
const onboarding = [
  { label: 'Crear tu cuenta', phase: 1 },
  { label: 'Registrar tu cuenta bancaria (CLABE)', phase: 2 },
  { label: 'Revisión de Banxico (hasta 5 días hábiles)', phase: 2 },
  { label: '¡Listo para cobrar con CoDi!', phase: 2 },
]

export default function HomePage() {
  return (
    <>
      <div className="d-flex align-items-center justify-content-between mb-3">
        <h1 className="h4 mb-0">Inicio</h1>
        <ApiStatus />
      </div>

      <Card>
        <Card.Header>Activa tu cuenta</Card.Header>
        <ListGroup variant="flush" as="ol" numbered>
          {onboarding.map((step) => (
            <ListGroup.Item key={step.label} as="li" className="d-flex justify-content-between align-items-start">
              <span className="ms-2 me-auto">{step.label}</span>
              <span className="badge text-bg-light border">Fase {step.phase}</span>
            </ListGroup.Item>
          ))}
        </ListGroup>
      </Card>
    </>
  )
}
