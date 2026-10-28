import react from '@vitejs/plugin-react';
import federation from '@originjs/vite-plugin-federation';
import { defineConfig } from 'vite';

// Module Federation remote for Reports. Exposes ./Page (frontend.routes[].exposes).
// Packaged into the plugin and served by the Module Host from
// /plugin-assets/reports/<version>/, so the build uses a relative base (no absolute /assets/ URLs).
// originjs remotes only work from a build: `npm run serve` = build + preview on :5180.
export default defineConfig({
  // originjs prefixes exposes with base + assetsDir, so './' resolves to assets/assets/... (404).
  base: '',
  plugins: [
    react(),
    federation({
      name: 'reports_remote',
      filename: 'remoteEntry.js',
      exposes: { './Page': './src/Page.tsx' },
      shared: ['react', 'react-dom'],
    }),
  ],
  server: { port: 5180 },
  preview: { port: 5180, strictPort: true, cors: true },
  build: { target: 'esnext', cssCodeSplit: false, minify: true },
});
