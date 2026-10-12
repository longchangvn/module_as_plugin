import path from 'node:path';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import { defineConfig } from 'vite';

// Tenant Admin: per-tenant backoffice (users, roles, permissions, assignments).
// Same stack as the platform shell (React + Vite + Tailwind + shadcn).
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': path.resolve(__dirname, 'src') },
  },
  server: {
    port: 5175,
    proxy: {
      '/api/v1': { target: 'http://localhost:5100', changeOrigin: true },
    },
  },
  build: { target: 'esnext', cssCodeSplit: false, minify: true },
});
