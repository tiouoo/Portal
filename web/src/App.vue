<script setup>
import SiteFooter from './components/SiteFooter.vue'
import SiteHeader from './components/SiteHeader.vue'
import { RouterView } from 'vue-router'
</script>

<template>
  <div class="site-shell">
    <SiteHeader />
    <RouterView />
    <SiteFooter />
  </div>
</template>
