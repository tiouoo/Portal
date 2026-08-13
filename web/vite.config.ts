import { fileURLToPath, URL } from 'node:url';

import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import vueJsx from '@vitejs/plugin-vue-jsx';
import { VitePWA } from 'vite-plugin-pwa';
import pwaConfig from './pwa.config.js';

export default defineConfig(({ isSsrBuild }) => ({
  plugins: isSsrBuild
    ? [vue(), vueJsx()]
    : [vue(), vueJsx(), VitePWA(pwaConfig)],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  build: {
    rollupOptions: {
      ...(isSsrBuild
        ? {}
        : {
            output: {
              manualChunks: {
                'vue-vendor': ['vue', 'vue-router'],
              },
            },
          }),
    },
    chunkSizeWarningLimit: 2000,
  },
  server: {
    host: '0.0.0.0',
    port: 5173,
    strictPort: false,
    cors: true,
  },
}));
