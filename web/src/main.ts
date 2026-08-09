import './assets/css/main.css';

import { createApp } from './app';

const { app } = createApp(false);
app.mount('#app');

const updateSW = async () => {
  const registration = await navigator.serviceWorker?.ready;
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible') {
      registration.update();
    }
  });
  if (registration) {
    setInterval(
      () => {
        registration.update();
      },
      60 * 60 * 1000
    );
    registration.update();
  }
};

if ('serviceWorker' in navigator) {
  updateSW();
}
