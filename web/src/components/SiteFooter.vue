<script setup lang="ts">
import logoUrl from '../assets/portal-logo.svg';
import { RouterLink } from 'vue-router';
</script>

<template>
  <footer>
    <div class="container footer-inner">
      <RouterLink class="brand" to="/"
        ><img :src="logoUrl" alt="" width="23" height="23" /><span
          style="line-height: 1; margin-top: 4px"
          >Portal</span
        ></RouterLink
      >
      <div class="footer-links">
        <a href="https://github.com/tiouoo/Portal" target="_blank" rel="noreferrer">GitHub</a>
        <a href="https://github.com/tiouoo/Portal/releases" target="_blank" rel="noreferrer"
          >发行版本</a
        >
        <RouterLink to="/policy">隐私协议与使用条款</RouterLink>
        <a href="mailto:portal@tiouo.cc">联系</a>
      </div>
    </div>
    <p class="disclaimer container">
      Copyright © Portal 2026. 基于 AGPL-3.0-or-later 开源许可发布。
    </p>
    <p class="disclaimer-text container">
      Portal 是独立开源项目，与 Mojang Studios 或 Microsoft 没有从属关系。Minecraft 是 Microsoft
      旗下商标。
    </p>
  </footer>
</template>

<style scoped>
footer {
  padding: 32px 0 22px;
}
.footer-inner {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.brand {
  display: inline-flex;
  align-items: center;
  gap: 10px;
  font-weight: 800;
  letter-spacing: -0.5px;
}
.footer-inner .brand {
  font-size: 17px;
}
.footer-inner .brand img {
  width: 23px;
  height: 23px;
}
.footer-inner p {
  color: #8b92a0;
  font-size: 12px;
}
.footer-links {
  display: flex;
  gap: 24px;
  color: #70798a;
  font-size: 14px;
}
.footer-links a:hover {
  color: var(--blue);
}
.disclaimer {
  margin-top: 22px;
  padding-top: 18px;
  border-top: 1px solid #e0e4ea;
  color: #a0a6b1;
  font-size: 12px;
  text-align: center;
}
.disclaimer-text {
  padding-top: 10px;
  color: #a0a6b1;
  font-size: 12px;
  text-align: center;
}

@media (max-width: 820px) {
  .footer-inner {
    flex-direction: column;
    gap: 18px;
  }
  .footer-inner p {
    text-align: center;
  }
}

@media (max-width: 520px) {
  .footer-links {
    gap: 18px;
  }
}
</style>
