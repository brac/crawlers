import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    host: true,
    proxy: {
      '/game': {
        target: 'http://localhost:5238',
        ws: true,
        changeOrigin: true,
      },
      '/lobby': {
        target: 'http://localhost:5238',
        ws: true,
        changeOrigin: true,
      },
      '/api': {
        target: 'http://localhost:5238',
        changeOrigin: true,
      },
    },
  },
})
