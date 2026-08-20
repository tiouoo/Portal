import { VitePWAOptions } from 'vite-plugin-pwa';

const pwaConfig = {
  registerType: 'autoUpdate',
  devOptions: {
    enabled: true,
    type: 'module',
  },
  includeAssets: ['favicon.ico', 'icon-192.png', 'icon-512.png'],
  manifest: {
    name: 'portal.web',
    short_name: 'portal.web',
    description: 'portal.web',
    theme_color: '#ffffff',
    background_color: '#ffffff',
    display: 'standalone',
    start_url: '/',
    scope: '/',
    icons: [
      { src: '/icon-192.png', sizes: '192x192', type: 'image/png', purpose: 'any' },
      { src: '/icon-512.png', sizes: '512x512', type: 'image/png', purpose: 'any' },
      { src: '/icon-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
    ],
  },
  workbox: {
    navigateFallbackAllowlist: [/^\/$/],
    globPatterns: ['**/*.{js,css,html,ico,svg,txt,xml,png}'],
    maximumFileSizeToCacheInBytes: 5 * 1024 * 1024,
    runtimeCaching: [
      {
        urlPattern: /\.ttf$/i,
        handler: 'CacheFirst',
        options: {
          cacheName: 'font-cache',
          expiration: { maxEntries: 20, maxAgeSeconds: 60 * 60 * 24 * 365 },
          cacheableResponse: { statuses: [0, 200] },
        },
      },
      {
        urlPattern: /\.woff2$/i,
        handler: 'CacheFirst',
        options: {
          cacheName: 'font-cache',
          expiration: { maxEntries: 10, maxAgeSeconds: 60 * 60 * 24 * 365 },
          cacheableResponse: { statuses: [0, 200] },
        },
      },
      {
        urlPattern: /^https:\/\/(cdn|sdk|cloud)\..*/i,
        handler: 'NetworkFirst',
        options: {
          cacheName: 'cdn-cache',
          expiration: { maxEntries: 50, maxAgeSeconds: 60 * 60 * 24 },
        },
      },
    ],
  },
};

export default pwaConfig as VitePWAOptions;
