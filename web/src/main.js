import { createApp as createVueApp } from 'vue'
import App from './App.vue'
import { createRouter } from './router'
import './style.css'

export async function createApp() {
  const router = createRouter()
  const app = createVueApp(App)
  app.use(router)
  return { app, router }
}

if (typeof window !== 'undefined') {
  createApp().then(({ app, router }) => {
    router.isReady().then(() => app.mount('#app'))
  })
}
