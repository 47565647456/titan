import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  
  // Aspire sets services__api__https__0 or services__api__http__0 for service discovery
  const apiUrl = process.env.services__api__https__0 
    || process.env.services__api__http__0
    || 'https://localhost:7001'

  console.log('Vite proxy target URL:', apiUrl)
  console.log('Vite server port:', env.VITE_PORT || '5173 (default)')

  return {
    plugins: [react()],
    server: {
      port: parseInt(env.VITE_PORT) || 5173,
      proxy: {
        // Proxy API requests to the backend
        '/api': {
          target: apiUrl,
          changeOrigin: true,
          secure: false,
        },
        // Proxy health check endpoint
        '/health': {
          target: apiUrl,
          changeOrigin: true,
          secure: false,
        },
        // Proxy SignalR hub connections to the backend
        '/hubs': {
          target: apiUrl,
          changeOrigin: true,
          secure: false,
          ws: true, // Enable WebSocket proxying
        },
        // Proxy encryptionHub (at root level, not under /hubs)
        '/encryptionHub': {
          target: apiUrl,
          changeOrigin: true,
          secure: false,
          ws: true,
        },
      },
    },
  }
})
