import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import './index.css'
import App from './App.tsx'
import { WorkspaceHandoff } from './WorkspaceHandoff.tsx'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      refetchOnWindowFocus: false,
    },
  },
})

export function entrySurface(pathname: string): 'owner-claim' | 'invitation' | 'app' {
  const path = pathname.replace(/\/+$/, '') || '/'
  return path === '/owner-claim' ? 'owner-claim' : path === '/invite' ? 'invitation' : 'app'
}

const surface = entrySurface(window.location.pathname)
const content = surface === 'owner-claim'
  ? <WorkspaceHandoff kind="owner-claim" />
  : surface === 'invitation'
    ? <WorkspaceHandoff kind="invitation" />
    : <App />

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      {content}
    </QueryClientProvider>
  </StrictMode>,
)
