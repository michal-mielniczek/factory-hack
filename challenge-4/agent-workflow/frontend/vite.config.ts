import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      // Route analyze_machine to the .NET FactoryWorkflow service
      '/api/analyze_machine': {
        target: process.env.services__dotnetworkflow__https__0 || process.env.services__dotnetworkflow__http__0 || 'https://localhost:44833',
        changeOrigin: true,
        secure: false
      },
      // All other API calls go to the Python app service
      '/api': {
        target: process.env.APP_API || process.env.APP_HTTPS || process.env.APP_HTTP || 'https://localhost:8000',
        changeOrigin: true,
        secure: false
      }
    }
  }
})
