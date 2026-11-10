import { useQuery } from '@tanstack/react-query'
import { apiFetch } from './client'

export interface Health {
  status: string
}

export function useHealth() {
  return useQuery({
    queryKey: ['health'],
    queryFn: () => apiFetch<Health>('/api/health'),
    refetchInterval: 60_000,
  })
}
