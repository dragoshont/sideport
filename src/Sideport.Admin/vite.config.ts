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
        target: 'http://127.0.0.1:5173',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/sideport-api/, ''),
      },
    },
  },
})
