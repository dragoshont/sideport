import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    tsconfigPaths: true,
  },
  server: {
    proxy: {
      '/sideport-api': {
        // Local dev: forward API calls to the .NET Sideport.Api (default :8080).
        // Override with VITE_SIDEPORT_API_TARGET if the API runs elsewhere.
        target: process.env.VITE_SIDEPORT_API_TARGET ?? 'http://127.0.0.1:8080',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/sideport-api/, ''),
      },
    },
  },
})
