import path from 'node:path';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import federation from '@originjs/vite-plugin-federation';
import { defineConfig } from 'vite';

// Tenant Admin: per-tenant backoffice (users, roles, permissions, assignments).
// Same stack as the platform shell (React + Vite + Tailwind + shadcn).
// Also a Module Federation host: remotes are registered at runtime from
// GET /api/v1/tenant/frontend-bootstrap (src/lib/remotes.ts).
export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
    federation({
      name: 'tenant_admin',
      // Placeholder only so the plugin generates `virtual:__federation__`.
      remotes: { placeholder: 'http://localhost:0/placeholder.js' },
      shared: ['react', 'react-dom'],
    }),
  ],
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
