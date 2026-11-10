import { Link } from 'react-router-dom'
import { ComingSoon } from '@/components/ComingSoon'

export default function RegisterPage() {
  return (
    <ComingSoon title="Crear cuenta" phase={1}>
      Registro de vendedor. <Link to="/login">Ya tengo cuenta</Link>
    </ComingSoon>
  )
}
