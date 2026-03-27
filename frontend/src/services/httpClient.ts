const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5003'

export async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    headers: {
      'Content-Type': 'application/json',
      ...(init?.headers ?? {}),
    },
    ...init,
  })

  if (!response.ok) {
    const contentType = response.headers.get('content-type') ?? ''
    let message = `Request failed with status ${response.status}`

    if (contentType.includes('application/json')) {
      const body = (await response.json()) as { message?: string }
      message = body.message ?? message
    }

    throw new Error(message)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}
