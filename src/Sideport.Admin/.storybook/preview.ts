import type { Preview } from '@storybook/react-vite'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement } from 'react'
import type { ReactNode } from 'react'
import '../src/index.css'

const withQueryClient = (Story: () => ReactNode) => createElement(
  QueryClientProvider,
  {
    client: new QueryClient({
      defaultOptions: {
        queries: {
          retry: false,
          refetchOnWindowFocus: false,
        },
      },
    }),
  },
  createElement(Story),
)

const preview: Preview = {
  decorators: [withQueryClient],
  parameters: {
    layout: 'fullscreen',
    a11y: {
      test: 'todo',
    },
  },
}

export default preview
