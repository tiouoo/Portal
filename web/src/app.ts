import { createRouter } from './router';
import { createSSRApp, createApp as createClientApp } from 'vue';
import App from './App.vue';

export function createApp(ssr = false) {
  const app = ssr ? createSSRApp(App) : createClientApp(App);
  const router = createRouter();
  app.use(router);
  return { app, router };
}
