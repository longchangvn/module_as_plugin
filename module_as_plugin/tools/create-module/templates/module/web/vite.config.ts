import react from '@vitejs/plugin-react';
import federation from '@originjs/vite-plugin-federation';
import { defineConfig } from 'vite';

// Module Federation remote for __DISPLAY_NAME__. Exposes ./Page (frontend.routes[].exposes).
// originjs remotes only work from a build: `npm run serve` = build + preview on :__WEB_PORT__.
export default defineConfig({
  plugins: [
    react(),
    federation({
      name: '__SCOPE__',
      filename: 'remoteEntry.js',
      exposes: { './Page': './src/Page.tsx' },
      shared: ['react', 'react-dom'],
    }),
  ],
  server: { port: __WEB_PORT__ },
  preview: { port: __WEB_PORT__, strictPort: true, cors: true },
  build: { target: 'esnext', cssCodeSplit: false, minify: true },
});
