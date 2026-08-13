<script setup>
import { computed, ref } from 'vue';
import { platforms, channelBase, cnbBase } from '../data/downloads.js';

const route = ref('direct');
const channel = ref('release');
const source = ref('cnb');

const downloadUrl = (file) => {
  if (source.value === 'cnb') {
    return `${cnbBase('release')}/${file}`;
  }

  const releaseUrl = `${channelBase(channel.value)}/${file}`;

  if (route.value === 'bgithub') {
    return releaseUrl.replace('https://github.com/', 'https://bgithub.xyz/');
  }
  if (route.value === 'gh.tiouo') {
    return releaseUrl.replace('https://github.com/', 'https://gh.tiouo.cc/');
  }

  if (route.value === 'ghproxy') {
    return `https://ghproxy.net/${releaseUrl}`;
  }

  return releaseUrl;
};

const channelHint = computed(() => {
  return source.value === 'cnb'
    ? 'Cnb 为国内源，仅提供正式版，国内可优先选用'
    : 'Nightly 版本在北京时间凌晨 5 点左右自动发布；Commit 版本随代码更改而更新';
});
</script>

<template>
  <section class="download-section section">
    <div class="container">
      <div id="download" class="download-heading">
        <span class="section-kicker">现在开始</span>
        <h2>选择你的平台</h2>
        <p>{{ channelHint }}</p>
        <div class="download-options">
          <label class="download-route">
            <span>下载源</span>
            <select v-model="source" aria-label="选择下载源">
              <option value="cnb">Cnb</option>
              <option value="github">GitHub</option>
            </select>
          </label>
          <template v-if="source === 'github'">
            <label class="download-route">
              <span>版本</span>
              <select v-model="channel" aria-label="选择下载版本">
                <option value="release">正式版</option>
                <option value="nightly">Nightly</option>
                <option value="commit">Commit</option>
              </select>
            </label>
            <label class="download-route">
              <span>镜像</span>
              <select v-model="route" aria-label="选择下载镜像">
                <option value="direct">直接下载</option>
                <option value="gh.tiouo">gh.tiouo.cc</option>
                <option value="bgithub">bgithub.xyz</option>
                <option value="ghproxy">ghproxy.net</option>
              </select>
            </label>
          </template>
        </div>
      </div>
      <div class="platform-grid">
        <article v-for="platform in platforms" :key="platform.id" class="platform-card">
          <div class="platform-head">
            <div class="platform-icon">
              <svg v-if="platform.icon === 'windows'" viewBox="0 0 24 24">
                <path
                  d="M3 5.2 10.5 4v7.2H3V5.2Zm8.5-1.4L21 2.5v8.7h-9.5V3.8ZM3 12.3h7.5v7.2L3 18.4v-6.1Zm8.5 0H21V21l-9.5-1.3v-7.4Z" />
              </svg>
              <svg v-else-if="platform.icon === 'apple'" viewBox="0 0 24 24">
                <path
                  d="M16.7 12.7c0-2.4 2-3.6 2.1-3.7a4.6 4.6 0 0 0-3.6-2c-1.5-.2-3 1-3.8 1s-2-1-3.3-1C6.4 7 4.8 8 3.9 9.5c-1.9 3.3-.5 8.1 1.3 10.7.9 1.3 2 2.7 3.4 2.6 1.3-.1 1.8-.9 3.5-.9 1.6 0 2.1.9 3.5.9s2.4-1.3 3.3-2.6c1-1.5 1.5-3 1.5-3.1-.1 0-3.7-1.4-3.7-4.4ZM14.2 5.4A4.4 4.4 0 0 0 15.3 2a4.7 4.7 0 0 0-3.1 1.6A4.1 4.1 0 0 0 11 6.8c1.2.1 2.4-.5 3.2-1.4Z" />
              </svg>
              <svg v-else viewBox="-2 -1 18 18">
                <path d="M5 8V7H6V8H5Z" fill="currentColor"/>
                <path d="M9 8V7H10V8H9Z" fill="currentColor"/>
                <path clip-rule="evenodd" d="M1.00001 6.5C1.00001 2.91015 3.91016 0 7.50001 0C11.0899 0 14 2.91015 14 6.5V13.014C14 13.1926 14.0709 13.3638 14.1972 13.4901L14.8536 14.1464C14.9966 14.2894 15.0393 14.5045 14.962 14.6913C14.8846 14.8782 14.7022 15 14.5 15H0.500015C0.297783 15 0.115465 14.8782 0.0380749 14.6913C-0.0393156 14.5045 0.00346218 14.2894 0.146461 14.1464L0.802841 13.4901C0.929089 13.3638 1.00001 13.1926 1.00001 13.014V6.5ZM4 6.5C4 5.67157 4.67157 5 5.5 5C6.32843 5 7 5.67157 7 6.5V7.5C7 8.32843 6.32843 9 5.5 9C4.67157 9 4 8.32843 4 7.5V6.5ZM8 6.5C8 5.67157 8.67157 5 9.5 5C10.3284 5 11 5.67157 11 6.5V7.5C11 8.32843 10.3284 9 9.5 9C8.67157 9 8 8.32843 8 7.5V6.5ZM4.59302 10.5125C6.42295 9.59755 8.57687 9.59755 10.4068 10.5125L10.6558 10.637L10.5606 10.7323C9.74883 11.544 8.64788 12.0001 7.49991 12.0001C6.35194 12.0001 5.25099 11.544 4.43925 10.7323L4.34399 10.637L4.59302 10.5125Z" fill="currentColor" fill-rule="evenodd"/>
              </svg>
            </div>
            <div>
              <h3>{{ platform.name }}</h3>
              <p>{{ platform.detail }}</p>
            </div>
          </div>
          <a class="platform-primary" :href="downloadUrl(platform.primary.file)"
            >{{ platform.primary.label }}
            <span
              ><svg
                style="height: 20px; position: relative; top: 2px; left: 5px"
                fill="white"
                color="white"
                xmlns="http://www.w3.org/2000/svg"
                viewBox="0 0 640 640">
                <path
                  d="M297.4 566.6C309.9 579.1 330.2 579.1 342.7 566.6L502.7 406.6C515.2 394.1 515.2 373.8 502.7 361.3C490.2 348.8 469.9 348.8 457.4 361.3L352 466.7L352 96C352 78.3 337.7 64 320 64C302.3 64 288 78.3 288 96L288 466.7L182.6 361.3C170.1 348.8 149.8 348.8 137.3 361.3C124.8 373.8 124.8 394.1 137.3 406.6L297.3 566.6z" /></svg></span
          ></a>
          <div class="platform-links">
            <a v-for="link in platform.links" :key="link.file" :href="downloadUrl(link.file)">
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
      基岩版 UWP (版本 ≤ 1.12.11401) 与版本隔离仅支持 Windows x64。
      如遇无法打开、安全提示或文件损坏，请参考<RouterLink
        to="/install"
        style="white-space: nowrap; margin: 0 5px">
        安装说明 </RouterLink
      >。
    </p>
  </section>
</template>

<style scoped>
.section {
  padding-top: 100px;
  padding-bottom: 100px;
}
#download {
  scroll-margin-top: 80px;
}
.section-kicker {
  color: var(--blue);
  font-size: 13px;
  letter-spacing: 0.12em;
  font-weight: 700;
  text-transform: uppercase;
}
.download-heading {
  margin-bottom: 45px;
}
.download-heading h2 {
  margin: 10px 0 0;
  font-size: clamp(36px, 4vw, 50px);
  letter-spacing: -0.045em;
  line-height: 1.15;
}
.download-heading > p {
  margin: 14px 0 0;
  color: var(--muted);
  font-size: 13px;
  line-height: 1.8;
}
.download-options {
  margin-top: 22px;
  display: flex;
  flex-wrap: nowrap;
  gap: 16px;
}
.download-route {
  display: flex;
  align-items: center;
  gap: 10px;
  flex: 1 1 0;
  min-width: 180px;
  max-width: 210px;
  color: #4e5b70;
  font-size: 12px;
  font-weight: 700;
}
.download-route span {
  white-space: nowrap;
  flex-shrink: 0;
}
.download-route select {
  height: 36px;
  width: 100%;
  padding: 0 32px 0 12px;
  border: 1px solid #d5deeb;
  border-radius: 12px;
  background: #fff;
  color: #20283a;
  font: inherit;
  font-weight: 600;
  cursor: pointer;
  appearance: none;
  -webkit-appearance: none;
  -moz-appearance: none;
  background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='8' viewBox='0 0 12 8'%3E%3Cpath d='M1 1.5 6 6.5 11 1.5' fill='none' stroke='%238090a9' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'/%3E%3C/svg%3E");
  background-repeat: no-repeat;
  background-position: right 12px center;
  background-size: 12px auto;
}
.download-route select:focus-visible {
  outline: none;
  border-color: rgba(42, 112, 245, 0.55);
}
.platform-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 18px;
}
.platform-card {
  padding: 27px;
  border: 1px solid #dfe5ef;
  border-radius: 17px;
  background: white;
  transition:
    border-color 0.2s,
    box-shadow 0.2s;
}
.platform-card:hover {
  border-color: #ccd8ea;
  box-shadow: 0 16px 35px rgba(60, 75, 105, 0.08);
}
.platform-head {
  display: flex;
  align-items: center;
  gap: 15px;
}
.platform-icon {
  width: 50px;
  height: 50px;
  display: grid;
  place-items: center;
  border-radius: 12px;
  background: #f2f5fa;
  color: #20283a;
}
.platform-icon svg {
  width: 25px;
  height: 25px;
  fill: currentColor;
  stroke: none;
}
.platform-head h3 {
  margin: 0 0 4px;
  font-size: 20px;
}
.platform-head p {
  margin: 0;
  color: #9097a5;
  font-size: 11px;
}
.platform-primary {
  height: 46px;
  margin-top: 23px;
  padding: 0 16px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-radius: 14px;
  background: var(--blue);
  color: white;
  font-size: 13px;
  font-weight: 700;
}
.platform-primary:hover {
  background: #1f62df;
}
.platform-primary span {
  font-size: 18px;
}
.platform-links {
  margin-top: 14px;
}
.platform-links a {
  min-height: 48px;
  padding: 9px 4px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-bottom: 1px solid #edf0f4;
  font-size: 12px;
}
.platform-links a:last-child {
  border: 0;
}
.platform-links a:hover {
  color: var(--blue);
}
.platform-links small {
  display: block;
  margin-top: 3px;
  color: #a0a6b1;
  font-size: 9px;
}
.platform-links svg {
  width: 17px;
  height: 17px;
  color: #a7aeba;
}
.download-note {
  width: calc(100% - 48px);
  margin: 25px auto 0;
  color: #4e5b70;
  font-size: 14px;
  font-weight: 600;
  line-height: 1.6;
  text-align: center;
}
.download-note > span {
  display: grid;
  place-items: center;
  width: 20px;
  height: 20px;
  border: 1px solid #8090a9;
  border-radius: 50%;
  font-size: 12px;
  font-weight: 700;
}
.download-note a {
  color: var(--blue);
  font-weight: 800;
  text-decoration: underline;
  text-underline-offset: 2px;
}
.download-note a:hover {
  color: #1f62df;
}

@media (max-width: 820px) {
  .section {
    padding-top: 40px;
    padding-bottom: 40px;
  }
  .platform-grid {
    grid-template-columns: 1fr;
  }
  .download-note {
    width: calc(100% - 32px);
  }
}

@media (max-width: 630px) {
  .download-route span {
    width: 48px;
  }
  .download-heading h2 {
    font-size: 36px;
  }
  .platform-card {
    padding: 22px;
  }
  .download-options {
    flex-direction: column;
    align-items: stretch;
  }
  .download-route {
    flex: 1 1 auto;
    min-width: 0;
    max-width: none;
    width: 100%;
  }
  .download-route select {
    width: 100%;
    flex: 1;
  }
  .download-note {
    width: calc(100% - 28px);
  }
}
</style>
