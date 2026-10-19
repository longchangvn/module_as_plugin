import react from '@vitejs/plugin-react';
import federation from '@originjs/vite-plugin-federation';
import { defineConfig } from 'vite';

// Module Federation remote for Sample Module 2. Exposes ./Page (frontend.routes[].exposes).
// originjs remotes only work from a build: `npm run serve` = build + preview on :5177.
export default defineConfig({
  plugins: [
    react(),
    federation({
      name: 'sample2_remote',
      filename: 'remoteEntry.js',
      exposes: { './Page': './src/Page.tsx' },
      shared: ['react', 'react-dom'],
    }),
  ],
  server: { port: 5177 },
  preview: { port: 5177, strictPort: true, cors: true },
  build: { target: 'esnext', cssCodeSplit: false, minify: true },
});
