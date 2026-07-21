import { createRouter, createWebHistory } from 'vue-router'
import HomeView from './views/HomeView.vue'
import MacOSInstallView from './views/MacOSInstallView.vue'

export default createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', component: HomeView },
    { path: '/macos-install', component: MacOSInstallView }
  ],
  scrollBehavior(to, from, savedPosition) {
    if (savedPosition) return savedPosition
    if (to.hash) return { el: to.hash, top: 88 }
    return { top: 0 }
  }
})
