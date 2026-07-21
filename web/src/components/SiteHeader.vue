<script setup>
import { ref } from "vue";
import logoUrl from "../assets/portal-logo.svg";

const menuOpen = ref(false);

function closeMenu() {
  menuOpen.value = false;
}
</script>

<template>
  <header class="nav-wrap">
    <nav class="nav container" aria-label="主导航">
      <a class="brand" href="#top" aria-label="Portal 首页" @click="closeMenu">
        <img :src="logoUrl" alt="" />
        <span>Portal</span>
      </a>
      <button
        class="menu-button"
        type="button"
        aria-label="切换导航菜单"
        :aria-expanded="menuOpen"
        @click="menuOpen = !menuOpen"
      >
        <span></span><span></span>
      </button>
      <div class="nav-links" :class="{ open: menuOpen }">
        <a href="#features" @click="closeMenu">功能</a>
        <a href="#download" @click="closeMenu">下载</a>
        <a
          href="https://github.com/tiouoo/Portal"
          target="_blank"
          rel="noreferrer"
          >GitHub</a
        >
        <a
          class="nav-cta"
          style="border-radius: 12px"
          href="#download"
          @click="closeMenu"
          >获取 Portal</a
        >
      </div>
    </nav>
  </header>
</template>

<style scoped src="../styles/site-header.css"></style>
