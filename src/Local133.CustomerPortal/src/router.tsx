import { createBrowserRouter, Navigate } from 'react-router-dom'
import { PageSpinner } from '@/components/PageSpinner'
import { RouteError } from '@/components/RouteError'
import MerchantLayout from '@/layouts/MerchantLayout'
import PublicLayout from '@/layouts/PublicLayout'
import ChargePage from '@/pages/merchant/ChargePage'
import HomePage from '@/pages/merchant/HomePage'
import MorePage from '@/pages/merchant/MorePage'
import SalesPage from '@/pages/merchant/SalesPage'
import LoginPage from '@/pages/public/LoginPage'
import NotFoundPage from '@/pages/public/NotFoundPage'
import RegisterPage from '@/pages/public/RegisterPage'

// Las rutas de vendedor van en el bundle principal (uso diario en móvil);
// el portal admin se carga bajo demanda en chunks separados.
// Los guards por rol (Merchant/Admin) llegan con la autenticación en Fase 1.
export const router = createBrowserRouter([
  {
    errorElement: <RouteError />,
    HydrateFallback: PageSpinner,
    children: [
      { index: true, element: <Navigate to="/app" replace /> },
      {
        path: 'app',
        element: <MerchantLayout />,
        children: [
          { index: true, element: <HomePage /> },
          { path: 'cobrar', element: <ChargePage /> },
          { path: 'ventas', element: <SalesPage /> },
          { path: 'mas', element: <MorePage /> },
        ],
      },
      {
        path: 'admin',
        lazy: async () => ({ Component: (await import('@/layouts/AdminLayout')).default }),
        HydrateFallback: PageSpinner,
        children: [
          { index: true, lazy: () => import('@/pages/admin/DashboardPage') },
          { path: 'clientes', lazy: () => import('@/pages/admin/CustomersPage') },
          { path: 'transacciones', lazy: () => import('@/pages/admin/TransactionsPage') },
          { path: 'parametros', lazy: () => import('@/pages/admin/SettingsPage') },
        ],
      },
      {
        element: <PublicLayout />,
        children: [
          { path: 'login', element: <LoginPage /> },
          { path: 'registro', element: <RegisterPage /> },
          { path: '*', element: <NotFoundPage /> },
        ],
      },
    ],
  },
])
