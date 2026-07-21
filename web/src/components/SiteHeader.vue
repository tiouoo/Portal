<script setup>
import { ref } from "vue";
import { RouterLink } from "vue-router";
import logoUrl from "../assets/portal-logo.svg";

const menuOpen = ref(false);

function closeMenu() {
  menuOpen.value = false;
}
</script>

<template>
  <header class="nav-wrap">
    <nav class="nav container" aria-label="主导航">
      <RouterLink class="brand" to="/" aria-label="Portal 首页" @click="closeMenu">
        <img :src="logoUrl" alt="" />
        <span>Portal</span>
      </RouterLink>
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
        <RouterLink :to="{ path: '/', hash: '#features' }" @click="closeMenu">功能</RouterLink>
        <RouterLink :to="{ path: '/', hash: '#download' }" @click="closeMenu">下载</RouterLink>
        <a
          href="https://github.com/tiouoo/Portal"
          target="_blank"
          rel="noreferrer"
          >GitHub</a
        >
        <RouterLink
          class="nav-cta"
          style="border-radius: 12px"
          :to="{ path: '/', hash: '#download' }"
          @click="closeMenu"
          >获取 Portal</RouterLink
        >
      </div>
    </nav>
  </header>
</template>

<style scoped src="../styles/site-header.css"></style>
