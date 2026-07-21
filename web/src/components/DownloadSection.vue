<script setup>
import { platforms } from "../data/downloads.js";
</script>

<template>
  <section id="download" class="download-section section">
    <div class="container">
      <div class="download-heading">
        <div>
          <span class="section-kicker">现在开始</span>
          <h2>选择你的平台</h2>
        </div>
        <p>Nightly 版本在北京时间凌晨5点左右自动发布一个新版本</p>
      </div>
      <div class="platform-grid">
        <article
          v-for="platform in platforms"
          :key="platform.id"
          class="platform-card"
        >
          <div class="platform-head">
            <div class="platform-icon">
              <svg v-if="platform.icon === 'windows'" viewBox="0 0 24 24">
                <path
                  d="M3 5.2 10.5 4v7.2H3V5.2Zm8.5-1.4L21 2.5v8.7h-9.5V3.8ZM3 12.3h7.5v7.2L3 18.4v-6.1Zm8.5 0H21V21l-9.5-1.3v-7.4Z"
                />
              </svg>
              <svg v-else-if="platform.icon === 'apple'" viewBox="0 0 24 24">
                <path
                  d="M16.7 12.7c0-2.4 2-3.6 2.1-3.7a4.6 4.6 0 0 0-3.6-2c-1.5-.2-3 1-3.8 1s-2-1-3.3-1C6.4 7 4.8 8 3.9 9.5c-1.9 3.3-.5 8.1 1.3 10.7.9 1.3 2 2.7 3.4 2.6 1.3-.1 1.8-.9 3.5-.9 1.6 0 2.1.9 3.5.9s2.4-1.3 3.3-2.6c1-1.5 1.5-3 1.5-3.1-.1 0-3.7-1.4-3.7-4.4ZM14.2 5.4A4.4 4.4 0 0 0 15.3 2a4.7 4.7 0 0 0-3.1 1.6A4.1 4.1 0 0 0 11 6.8c1.2.1 2.4-.5 3.2-1.4Z"
                />
              </svg>
              <svg v-else viewBox="0 0 24 24">
                <path
                  d="M12 2c-2.4 0-4.2 2.3-4.2 5.3v2.2c-1.4 1-2.4 2.8-2.8 4.8l-1.8 3.2c-.5.9.2 2 1.2 2H8c1 1.6 2.3 2.5 4 2.5s3-.9 4-2.5h3.6c1 0 1.7-1.1 1.2-2L19 14.3c-.4-2-1.4-3.8-2.8-4.8V7.3C16.2 4.3 14.4 2 12 2Zm-1.7 5.2c-.5 0-.8-.5-.8-1s.3-1 .8-1 .8.5.8 1-.3 1-.8 1Zm3.4 0c-.5 0-.8-.5-.8-1s.3-1 .8-1 .8.5.8 1-.3 1-.8 1Z"
                />
              </svg>
            </div>
            <div>
              <h3>{{ platform.name }}</h3>
              <p>{{ platform.detail }}</p>
            </div>
          </div>
          <a class="platform-primary" :href="platform.primaryUrl"
            >{{ platform.primary }}
            <span
              ><svg
                style="height: 20px; position: relative; top: 2px; left: 5px"
                fill="white"
                color="white"
                xmlns="http://www.w3.org/2000/svg"
                viewBox="0 0 640 640"
              >
                <path
                  d="M297.4 566.6C309.9 579.1 330.2 579.1 342.7 566.6L502.7 406.6C515.2 394.1 515.2 373.8 502.7 361.3C490.2 348.8 469.9 348.8 457.4 361.3L352 466.7L352 96C352 78.3 337.7 64 320 64C302.3 64 288 78.3 288 96L288 466.7L182.6 361.3C170.1 348.8 149.8 348.8 137.3 361.3C124.8 373.8 124.8 394.1 137.3 406.6L297.3 566.6z"
                /></svg></span
          ></a>
          <div class="platform-links">
            <a v-for="link in platform.links" :key="link.url" :href="link.url">
              <span
                >{{ link.label }}<small>{{ link.meta }}</small></span
              >
              <svg viewBox="0 0 24 24">
                <path d="M12 4v11m0 0 4-4m-4 4-4-4M5 20h14" />
              </svg>
            </a>
          </div>
        </article>
      </div>
    </div>
    <p class="download-note">
      基岩版游戏的安装与启动功能目前仅在 Windows 上提供。macOS
      如遇安全提示或文件损坏，请参考<RouterLink
        to="/macos-install"
        style="white-space: nowrap; margin: 0 5px"
      >
        安装说明 </RouterLink
      >。
    </p>
  </section>
</template>

<style scoped src="../styles/download-section.css"></style>
