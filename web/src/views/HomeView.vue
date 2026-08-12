<script setup>
import DownloadSection from '../components/DownloadSection.vue'
import FeatureSection from '../components/FeatureSection.vue'
import HeroSection from '../components/HeroSection.vue'
import OpenSourceSection from '../components/OpenSourceSection.vue'
import ProtocolSection from '../components/ProtocolSection.vue'
</script>

<template>
  <main id="top">
    <HeroSection />
    <FeatureSection />
    <DownloadSection />
    <ProtocolSection v-if="false" />
    <OpenSourceSection />
  </main>
</template>
