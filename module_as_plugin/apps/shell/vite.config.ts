import path from 'node:path';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import federation from '@originjs/vite-plugin-federation';
import { defineConfig } from 'vite';

// Module Federation host. Remotes are registered at runtime from
// GET /api/v1/tenant/frontend-bootstrap (never manifest.yml — R10).
// Static remotes below are empty; RemoteLoader injects version-pinned
// scope/remoteUrl entries per bootstrap payload.
export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
    federation({
      name: 'shell',
      filename: 'remoteEntry.js',
      exposes: {},
      // Shared singletons (react/react-dom): one copy between host + remotes.
      shared: ['react', 'react-dom', 'react-router-dom'],
    }),
  ],
  resolve: {
    alias: { '@': path.resolve(__dirname, 'src') },
  },
  server: {
    port: 5173,
    proxy: {
      '/api/v1': { target: 'http://localhost:5100', changeOrigin: true },
    },
  },
  build: { target: 'esnext', cssCodeSplit: false, minify: true },
});
