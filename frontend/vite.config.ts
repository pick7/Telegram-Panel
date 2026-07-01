import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import path from 'node:path'

const panelApiTarget = process.env.VITE_PANEL_API_TARGET || 'http://localhost:5000'

export default defineConfig({
  base: '/ui/',
  plugins: [vue()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, 'src'),
    },
  },
  server: {
    proxy: {
      '/api': panelApiTarget,
      '/downloads': panelApiTarget,
    },
  },
  build: {
    outDir: '../src/TelegramPanel.Web/wwwroot/panel-spa',
    emptyOutDir: true,
    rollupOptions: {
      output: {
        manualChunks(id) {
          if (!id.includes('node_modules')) return undefined
          if (id.includes('@element-plus/icons-vue')) return 'vendor-element-icons'
          if (id.includes('element-plus')) return 'vendor-element-plus'
          if (id.includes('vue') || id.includes('pinia')) return 'vendor-vue'
          return 'vendor'
        },
      },
    },
  },
})
