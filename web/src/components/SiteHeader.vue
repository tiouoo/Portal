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
        <img :src="logoUrl" alt="" width="28" height="28" />
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
        <RouterLink :to="{ path: '/', hash: '#qq-group' }" @click="closeMenu"
          >官方 QQ 群</RouterLink
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

<style scoped>
.nav-wrap {
  position: fixed;
  z-index: 50;
  inset: 0 0 auto;
  border-bottom: 1px solid rgba(223, 227, 235, 0.75);
  background: rgba(248, 249, 252, 0.88);
  backdrop-filter: blur(18px);
}
.nav {
  height: 72px;
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.brand {
  display: inline-flex;
  align-items: center;
  gap: 10px;
  font-weight: 800;
  font-size: 21px;
  letter-spacing: -0.5px;
}
.brand img {
  width: 28px;
  height: 28px;
}
.nav-links {
  display: flex;
  align-items: center;
  gap: 34px;
  color: #5f6879;
  font-size: 14px;
}
.nav-links > a:not(.nav-cta):hover {
  color: var(--blue);
}
.nav-cta {
  background: #20283a;
  color: white;
  border-radius: 9px;
  padding: 11px 18px;
  transition:
    background 0.2s,
    box-shadow 0.2s;
}
.nav-cta:hover {
  background: var(--blue);
  box-shadow: 0 7px 18px rgba(42, 112, 245, 0.22);
}
.menu-button {
  display: none;
  width: 40px;
  height: 40px;
  border: 0;
  background: transparent;
}
.menu-button span {
  display: block;
  width: 20px;
  height: 2px;
  background: var(--ink);
  margin: 5px auto;
}

@media (max-width: 820px) {
  .menu-button {
    display: block;
  }
  .nav-links {
    position: absolute;
    left: 16px;
    right: 16px;
    top: 64px;
    padding: 18px;
    flex-direction: column;
    gap: 18px;
    border: 1px solid var(--line);
    border-radius: 14px;
    background: white;
    box-shadow: 0 18px 40px rgba(40, 50, 75, 0.12);
    opacity: 0;
    transition: all 0.2s;
    pointer-events: none;
  }
  .nav-links.open {
    opacity: 1;
    pointer-events: all;
  }
  .nav-cta {
    width: 100%;
    text-align: center;
  }
}
</style>
