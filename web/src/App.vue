<script setup>
import DownloadSection from './components/DownloadSection.vue'
import FeatureSection from './components/FeatureSection.vue'
import HeroSection from './components/HeroSection.vue'
import OpenSourceSection from './components/OpenSourceSection.vue'
import SiteFooter from './components/SiteFooter.vue'
import SiteHeader from './components/SiteHeader.vue'
</script>

<template>
  <div class="site-shell">
    <SiteHeader />
    <main id="top">
      <HeroSection />
      <FeatureSection />
      <DownloadSection />
      <OpenSourceSection />
    </main>
    <SiteFooter />
  </div>
</template>
