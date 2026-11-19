import { ApiError, apiWrite } from './http'

/**
 * The server's item cache. It validates against the files on every
 * request; clearing it is for a user who distrusts what the SPA shows.
 */
export const cacheApi = {
  async clear(): Promise<void> {
    const r = await apiWrite('/api/cache', 'DELETE')
    if (!r.ok) {
      let problem = null
      try { problem = await r.json() } catch { /* noop */ }
      throw new ApiError(r.status, problem)
    }
  },
}
