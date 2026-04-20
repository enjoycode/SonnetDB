import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import { fileURLToPath, URL } from 'node:url';

// TSLite Admin UI 由 ASP.NET Core 嵌入资源托管在 /admin/ 前缀下。
export default defineConfig({
  base: '/admin/',
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  build: {
    target: 'es2022',
    outDir: 'dist',
    emptyOutDir: true,
    sourcemap: false,
    rollupOptions: {
      output: {
        manualChunks: {
          vue: ['vue', 'vue-router', 'pinia'],
          naive: ['naive-ui'],
        },
      },
    },
  },
  server: {
    port: 5173,
    proxy: {
      '/v1': 'http://localhost:5000',
      '/healthz': 'http://localhost:5000',
      '/metrics': 'http://localhost:5000',
    },
  },
});
