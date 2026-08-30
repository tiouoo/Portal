import { renderToString } from 'vue/server-renderer';
import { createApp } from './app';

export async function render(url: string) {
  const { app, router } = createApp();

  await router.push(url);
  await router.isReady();

  const appHtml = await renderToString(app);

  return appHtml;
}
