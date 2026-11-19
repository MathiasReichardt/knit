import './lib/zodConfig'
import { StrictMode, useEffect } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider, useQueryClient } from '@tanstack/react-query'
import { RouterProvider, createRouter } from '@tanstack/react-router'
import { routeTree } from './routeTree.gen'
import { connectEvents } from './api/sse'
import { applyThemeClass, useTheme } from './stores/theme'
import './index.css'

applyThemeClass(useTheme.getState().theme)

const router = createRouter({ routeTree })
const queryClient = new QueryClient()

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}

function SseConnector() {
  const client = useQueryClient()
  useEffect(() => connectEvents({ client }), [client])
  return null
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <SseConnector />
      <RouterProvider router={router} />
    </QueryClientProvider>
  </StrictMode>,
)
