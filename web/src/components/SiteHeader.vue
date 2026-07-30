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
      <RouterLink
        class="brand"
        to="/"
        aria-label="Portal 首页"
        @click="closeMenu"
      >
        <img :src="logoUrl" alt="" />
        <span>Portal</span>
      </RouterLink>
      <button
        class="menu-button"
        type="button"
        style="cursor: pointer"
        aria-label="切换导航菜单"
        :aria-expanded="menuOpen"
        @click="menuOpen = !menuOpen"
      >
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 640 640">
          <path
            d="M297.4 438.6C309.9 451.1 330.2 451.1 342.7 438.6L502.7 278.6C515.2 266.1 515.2 245.8 502.7 233.3C490.2 220.8 469.9 220.8 457.4 233.3L320 370.7L182.6 233.4C170.1 220.9 149.8 220.9 137.3 233.4C124.8 245.9 124.8 266.2 137.3 278.7L297.3 438.7z"
          />
        </svg>
      </button>
      <div class="nav-links" :class="{ open: menuOpen }">
        <RouterLink :to="{ path: '/', hash: '#features' }" @click="closeMenu"
          >功能</RouterLink
        >
        <RouterLink :to="{ path: '/', hash: '#download' }" @click="closeMenu"
          >下载</RouterLink
        >
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
