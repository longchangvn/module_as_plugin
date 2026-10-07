import react from '@vitejs/plugin-react';
import federation from '@originjs/vite-plugin-federation';
import { defineConfig } from 'vite';

// Module Federation remote. Exposes ./MembersPage (frontend.routes[].exposes)
// and ./menus (frontend.menus). Shell lazy-loads via bootstrap scope/remoteUrl.
export default defineConfig({
  plugins: [
    react(),
    federation({
      name: 'loyalty_remote',
      filename: 'remoteEntry.js',
      exposes: {
        './MembersPage': './src/MembersPage.tsx',
        './menus': './src/menus.ts',
      },
      // Shared singletons (react/react-dom): one copy between host + remotes.
      shared: ['react', 'react-dom'],
    }),
  ],
  server: { port: 5174 },
  build: { target: 'esnext', cssCodeSplit: false, minify: true },
});
