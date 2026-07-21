<script setup>
import logoUrl from '../assets/portal-logo.svg'
</script>

<template>
  <footer>
    <div class="container footer-inner">
      <a class="brand" href="#top"><img :src="logoUrl" alt="" /><span>Portal</span></a>
      <p>Copyright © Portal 2026. 基于 GPL-3.0-or-later 开源许可发布。</p>
      <div class="footer-links">
        <a href="https://github.com/tiouoo/Portal" target="_blank" rel="noreferrer">GitHub</a>
        <a href="https://github.com/tiouoo/Portal/releases" target="_blank" rel="noreferrer">发行版本</a>
        <a href="mailto:tiouo@qq.com">联系</a>
      </div>
    </div>
    <p class="disclaimer container">Portal 是独立开源项目，与 Mojang Studios 或 Microsoft 没有从属关系。Minecraft 是 Microsoft 旗下商标。</p>
  </footer>
</template>

<style scoped src="../styles/site-footer.css"></style>
