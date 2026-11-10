import type { ReactNode } from 'react'
import Card from 'react-bootstrap/Card'

/** Marcador de página aún no implementada; indica la fase del plan donde se construye. */
export function ComingSoon({ title, phase, children }: { title: string; phase: number; children?: ReactNode }) {
  return (
    <>
      <h1 className="h4 mb-3">{title}</h1>
      <Card body className="text-secondary">
        {children ?? 'Próximamente.'} <span className="badge text-bg-light border">Fase {phase}</span>
      </Card>
    </>
  )
}
