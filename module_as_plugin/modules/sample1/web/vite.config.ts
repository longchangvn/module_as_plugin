import react from '@vitejs/plugin-react';
import federation from '@originjs/vite-plugin-federation';
import { defineConfig } from 'vite';

// Module Federation remote for Sample Module 1. Exposes ./Page (frontend.routes[].exposes).
// originjs remotes only work from a build: `npm run serve` = build + preview on :5176.
export default defineConfig({
  plugins: [
    react(),
    federation({
      name: 'sample1_remote',
      filename: 'remoteEntry.js',
      exposes: { './Page': './src/Page.tsx' },
      shared: ['react', 'react-dom'],
    }),
  ],
  server: { port: 5176 },
  preview: { port: 5176, strictPort: true, cors: true },
  build: { target: 'esnext', cssCodeSplit: false, minify: true },
});
