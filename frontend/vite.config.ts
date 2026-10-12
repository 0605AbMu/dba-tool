import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'
import { fileURLToPath, URL } from 'node:url'

const devApiTarget = process.env.VITE_DEV_API_TARGET || 'http://localhost:5150'

export default defineConfig({
  plugins: [
    vue(),
    tailwindcss(),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    }
  },
  server: {
    port: 5173,
    host: '0.0.0.0',
    strictPort: false,
    proxy: {
      '/api': {
        target: devApiTarget,
        changeOrigin: true,
      },
      '/hubs': {
        target: devApiTarget,
        ws: true,
        changeOrigin: true,
      },
      '/hangfire': {
        target: devApiTarget,
        changeOrigin: true,
      }
    }
  },
  build: {
    outDir: 'dist',
    sourcemap: false,
    chunkSizeWarningLimit: 1200
  }
})
