/** Cuerpo RFC 7807 que devuelve la API (AddProblemDetails agrega traceId). */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  traceId?: string
  errors?: Record<string, string[]>
}

export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails

  constructor(status: number, problem: ProblemDetails) {
    super(problem.detail ?? problem.title ?? `Error ${status}`)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }
}

type ApiInit = Omit<RequestInit, 'body'> & { body?: unknown }

/** fetch hacia la API del mismo host (cookie de sesión incluida). Lanza ApiError en respuestas no exitosas. */
export async function apiFetch<T>(path: string, init: ApiInit = {}): Promise<T> {
  const { body, headers, ...rest } = init
  const response = await fetch(path, {
    ...rest,
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
      ...headers,
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })

  const isJson = response.headers.get('content-type')?.includes('json') ?? false
  const payload: unknown = isJson ? await response.json() : undefined

  if (!response.ok) {
    throw new ApiError(response.status, (payload as ProblemDetails | undefined) ?? { status: response.status })
  }
  return payload as T
}
