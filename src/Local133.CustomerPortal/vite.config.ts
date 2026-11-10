import { fileURLToPath, URL } from 'node:url'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// En dev la SPA corre en :5173 y reenvía /api y /hubs a la API (perfil "http" de launchSettings).
const apiTarget = process.env.API_URL ?? 'http://localhost:5231'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': { target: apiTarget, changeOrigin: true },
      '/hubs': { target: apiTarget, changeOrigin: true, ws: true },
    },
  },
  build: {
    // La API sirve la SPA (mismo host ⇒ cookie de Identity).
    outDir: '../Local133.Api/wwwroot',
    emptyOutDir: true,
  },
})
