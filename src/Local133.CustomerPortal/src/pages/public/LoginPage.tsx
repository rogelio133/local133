import { Link } from 'react-router-dom'
import { ComingSoon } from '@/components/ComingSoon'

export default function LoginPage() {
  return (
    <ComingSoon title="Iniciar sesión" phase={1}>
      Correo/contraseña, Google y Facebook. <Link to="/registro">Crear cuenta</Link>
    </ComingSoon>
  )
}
