import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Aspire sets services__api__https__0 or services__api__http__0 for service discovery
const apiUrl = process.env.services__api__https__0 
  || process.env.services__api__http__0
  || 'https://localhost:7001'

console.log('Vite proxy target URL:', apiUrl)

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
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
    },
  },
})
