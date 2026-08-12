<script setup>
import { ref, computed, onMounted } from 'vue';
import logoUrl from '../assets/portal-logo.svg';
import { cnbBase } from '../data/downloads.js';

const detectedOS = ref('windows');
const detectedArch = ref('x64');

// 检测操作系统和芯片架构
function detectPlatform() {
  const ua = navigator.userAgent.toLowerCase();
  const platform = navigator.platform.toLowerCase();

  // 检测操作系统
  if (ua.includes('mac') || platform.includes('mac')) {
    detectedOS.value = 'macos';
    // 检测 macOS 芯片架构：Apple Silicon (ARM) vs Intel (x64)
    if (ua.includes('arm') || navigator.maxTouchPoints > 1) {
      detectedArch.value = 'arm64';
    } else {
      detectedArch.value = 'x64';
    }
  } else if (ua.includes('linux') || platform.includes('linux')) {
    detectedOS.value = 'linux';
    detectedArch.value = 'x64'; // Linux 只有 64 位
  } else {
    detectedOS.value = 'windows';
    detectedArch.value = 'x64'; // Windows 只有 64 位
  }
}

// 获取下载文件名
const downloadFileName = computed(() => {
  if (detectedOS.value === 'windows') {
    return 'Portal.win.x64.installer.zip';
  } else if (detectedOS.value === 'macos') {
    if (detectedArch.value === 'arm64') {
      return 'Portal.osx.mac.arm64.dmg';
    } else {
      return 'Portal.osx.mac.x64.dmg';
    }
  } else if (detectedOS.value === 'linux') {
    return 'Portal.linux.x64.AppImage';
  }
  return 'Portal.win.x64.installer.zip';
});

// 获取下载链接（默认使用 CNB 源，最新版）
const downloadUrl = computed(() => {
  return `${cnbBase('release')}/${downloadFileName.value}`;
});

onMounted(() => {
  detectPlatform();
});
</script>

<template>
  <section class="hero container">
    <div class="hero-copy">
      <div class="eyebrow"><span></span> 开源 · 跨平台 · 为 Minecraft 而生</div>
      <p class="brand-title">Portal Launcher</p>
      <h1>你的 Minecraft，<br class="hero-break" /><em>从这里出发</em></h1>
      <p>
        <span class="desc-desktop"
          >Portal 启动实例资源与记录收进一个工作区 —— 少一点配置，多一点游戏。</span
        >
        <span class="desc-mobile"
          >Portal 把启动、实例、资源与记录收进一个工作区。<br />少一点配置，多一点游戏。</span
        >
      </p>
      <div class="hero-actions">
        <a class="button primary" :href="downloadUrl">
          <svg class="button-icon" viewBox="0 0 24 24" aria-hidden="true">
            <path
              v-if="detectedOS === 'windows'"
              d="M3 5.2 10.5 4v7.2H3V5.2Zm8.5-1.4L21 2.5v8.7h-9.5V3.8ZM3 12.3h7.5v7.2L3 18.4v-6.1Zm8.5 0H21V21l-9.5-1.3v-7.4Z" />
            <path
              v-else-if="detectedOS === 'macos'"
              d="M16.7 12.7c0-2.4 2-3.6 2.1-3.7a4.6 4.6 0 0 0-3.6-2c-1.5-.2-3 1-3.8 1s-2-1-3.3-1C6.4 7 4.8 8 3.9 9.5c-1.9 3.3-.5 8.1 1.3 10.7.9 1.3 2 2.7 3.4 2.6 1.3-.1 1.8-.9 3.5-.9 1.6 0 2.1.9 3.5.9s2.4-1.3 3.3-2.6c1-1.5 1.5-3 1.5-3.1-.1 0-3.7-1.4-3.7-4.4ZM14.2 5.4A4.4 4.4 0 0 0 15.3 2a4.7 4.7 0 0 0-3.1 1.6A4.1 4.1 0 0 0 11 6.8c1.2.1 2.4-.5 3.2-1.4Z" />
            <g v-else transform="translate(4.5, 4) scale(1.6)">
              <path d="M5 8V7H6V8H5Z"/>
              <path d="M9 8V7H10V8H9Z"/>
              <path clip-rule="evenodd" d="M1.00001 6.5C1.00001 2.91015 3.91016 0 7.50001 0C11.0899 0 14 2.91015 14 6.5V13.014C14 13.1926 14.0709 13.3638 14.1972 13.4901L14.8536 14.1464C14.9966 14.2894 15.0393 14.5045 14.962 14.6913C14.8846 14.8782 14.7022 15 14.5 15H0.500015C0.297783 15 0.115465 14.8782 0.0380749 14.6913C-0.0393156 14.5045 0.00346218 14.2894 0.146461 14.1464L0.802841 13.4901C0.929089 13.3638 1.00001 13.1926 1.00001 13.014V6.5ZM4 6.5C4 5.67157 4.67157 5 5.5 5C6.32843 5 7 5.67157 7 6.5V7.5C7 8.32843 6.32843 9 5.5 9C4.67157 9 4 8.32843 4 7.5V6.5ZM8 6.5C8 5.67157 8.67157 5 9.5 5C10.3284 5 11 5.67157 11 6.5V7.5C11 8.32843 10.3284 9 9.5 9C8.67157 9 8 8.32843 8 7.5V6.5ZM4.59302 10.5125C6.42295 9.59755 8.57687 9.59755 10.4068 10.5125L10.6558 10.637L10.5606 10.7323C9.74883 11.544 8.64788 12.0001 7.49991 12.0001C6.35194 12.0001 5.25099 11.544 4.43925 10.7323L4.34399 10.637L4.59302 10.5125Z" fill-rule="evenodd"/>
            </g>
          </svg>
          下载 Portal
        </a>
        <a class="button secondary" href="#download">
          更多下载
          <svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M12 3v12m0 0 5-5m-5 5-5-5M5 21h14" />
          </svg>
        </a>
        <a
          class="button secondary"
          href="https://github.com/tiouoo/Portal"
          target="_blank"
          rel="noreferrer">
          查看源代码
          <svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M5 12h14m0 0l-6-6m6 6l-6 6" />
          </svg>
        </a>
      </div>
      <div class="hero-meta">
        <span><i class="status-dot"></i> 持续更新</span>
        <span>windows · macOs · linux</span>
      </div>
    </div>

    <div class="hero-visual" aria-label="Portal 应用界面示意" v-if="false">
      <div class="visual-orbit orbit-one"></div>
      <div class="visual-orbit orbit-two"></div>
      <div class="app-window">
        <div class="window-topbar">
          <div class="traffic"><i></i><i></i><i></i></div>
          <div class="window-tab">
            <img :src="logoUrl" alt="" width="15" height="15" /> 新标签页
          </div>
          <div class="window-tab">
            <svg class="tab-icon" viewBox="0 0 640 640" aria-hidden="true">
              <path
                d="M560.3 301.2C570.7 313 588.6 315.6 602.1 306.7C616.8 296.9 620.8 277 611 262.3L563 190.3C560.2 186.1 556.4 182.6 551.9 180.1L351.4 68.7C332.1 58 308.6 58 289.2 68.7L88.8 180C83.4 183 79.1 187.4 76.2 192.8L27.7 282.7C15.1 306.1 23.9 335.2 47.3 347.8L80.3 365.5L80.3 418.8C80.3 441.8 92.7 463.1 112.7 474.5L288.7 574.2C308.3 585.3 332.2 585.3 351.8 574.2L527.8 474.5C547.9 463.1 560.2 441.9 560.2 418.8L560.2 301.3zM320.3 291.4L170.2 208L320.3 124.6L470.4 208L320.3 291.4zM278.8 341.6L257.5 387.8L91.7 299L117.1 251.8L278.8 341.6z" />
            </svg>
            机械动力
          </div>
          <span class="window-plus">+</span>
        </div>
        <div class="window-body">
          <div class="app-content">
            <!-- <div class="app-heading">
              <span>下午好</span><b>准备好开始冒险了吗？</b>
            </div> -->
            <div class="app-grid">
              <div class="continue-card">
                <small class="card-label">继续游戏</small>
                <div class="continue-content">
                  <div class="game-art">
                    <div
                      style="
                        background-color: white;
                        display: flex;
                        align-items: center;
                        justify-content: center;
                        overflow: hidden;
                      ">
                      <img width="40" height="40" src="../assets/fabrici.png" alt="Fabric" />
                    </div>
                  </div>
                  <div>
                    <strong>生电整合包</strong>
                    <span>Fabric · 1.21.1</span>
                  </div>
                </div>

                <svg
                  width="36px"
                  fill="#2a70f5"
                  xmlns="http://www.w3.org/2000/svg"
                  viewBox="0 0 640 640">
                  <path
                    d="M64 320C64 178.6 178.6 64 320 64C461.4 64 576 178.6 576 320C576 461.4 461.4 576 320 576C178.6 576 64 461.4 64 320zM252.3 211.1C244.7 215.3 240 223.4 240 232L240 408C240 416.7 244.7 424.7 252.3 428.9C259.9 433.1 269.1 433 276.6 428.4L420.6 340.4C427.7 336 432.1 328.3 432.1 319.9C432.1 311.5 427.7 303.8 420.6 299.4L276.6 211.4C269.2 206.9 259.9 206.7 252.3 210.9z" />
                </svg>
              </div>
              <div class="stat-card">
                <small>本周游戏时长</small><strong style="margin-top: 2px">12.6 <i>小时</i></strong>
                <div class="chart">
                  <span></span><span></span><span></span><span></span><span></span><span></span
                  ><span></span>
                </div>
              </div>
              <div class="library-card">
                <div class="card-title"><b>实例</b></div>
                <div class="instance-row">
                  <div class="instance-card">
                    <div
                      style="
                        background-color: white;
                        display: flex;
                        align-items: center;
                        justify-content: center;
                        overflow: hidden;
                      "
                      class="instance-icon">
                      <img width="36" height="36" src="../assets/fabrici.png" alt="Fabric" />
                    </div>
                    <div><strong>生电整合包</strong><span>Fabric · 1.21.1</span></div>
                  </div>
                  <div class="instance-card">
                    <div
                      style="
                        background-color: white;
                        display: flex;
                        align-items: center;
                        justify-content: center;
                        overflow: hidden;
                      "
                      class="instance-icon">
                      <img width="36" height="36" src="../assets/grass.png" alt="grass" />
                    </div>
                    <div><strong>原版生存</strong><span>原版 · 1.20.1</span></div>
                  </div>
                  <div class="instance-card">
                    <div
                      style="
                        background-color: white;
                        display: flex;
                        align-items: center;
                        justify-content: center;
                        overflow: hidden;
                      "
                      class="instance-icon">
                      <img width="36" height="36" src="../assets/create.jpg" alt="create" />
                    </div>
                    <div><strong>机械动力</strong><span>Forge · 1.19.2</span></div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
      <div class="floating-pill pill-top"><span>✓</span> 资源安装完成</div>
      <div class="floating-pill pill-bottom">
        <span>↗</span>
        <div><small>游戏时长</small><b>+ 2.4 小时</b></div>
      </div>
    </div>
    <div class="hero-product">
      <img class="hero-shot" src="../assets/pic.png" alt="Portal 应用界面" />
    </div>
  </section>
</template>

<style scoped>
.hero {
  padding-top: 160px;
  padding-bottom: 52px;
  text-align: center;
}
.hero-copy {
  position: relative;
  z-index: 2;
  max-width: 780px;
  margin: 0 auto;
}
.eyebrow {
  color: var(--blue);
  font-size: 13px;
  letter-spacing: 0.12em;
  font-weight: 700;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 9px;
  margin-bottom: 24px;
}
.eyebrow span {
  display: block;
  width: 24px;
  height: 2px;
  background: var(--blue);
}
.hero-copy > p.brand-title {
  margin: 0 0 4px;
  max-width: none;
  font-size: clamp(56px, 5.6vw, 84px);
  font-weight: 800;
  line-height: 1.08;
  letter-spacing: -0.03em;
  color: #303b53;
}
.hero-break {
  display: none;
}
h1 {
  margin: 0;
  font-size: clamp(34px, 5.2vw, 76px);
  line-height: 1.08;
  letter-spacing: -0.055em;
  color: #182033;
}
h1 em {
  color: var(--blue);
  font-style: normal;
  white-space: nowrap;
}
.hero-copy > p {
  max-width: 640px;
  margin: 24px auto 34px;
  color: var(--muted);
  font-size: 18px;
  line-height: 1.85;
}
.desc-mobile {
  display: none;
}
.desc-desktop {
  display: inline;
}
.hero-actions {
  display: flex;
  justify-content: center;
  align-items: center;
  gap: 12px;
}
.button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 12px;
  height: 50px;
  padding: 0 22px;
  border-radius: 12px;
  font-size: 15px;
  font-weight: 700;
  transition:
    background 0.2s,
    border-color 0.2s,
    box-shadow 0.2s;
}
.button-icon {
  width: 20px;
  height: 20px;
  fill: currentColor;
  stroke: none;
}
.button svg {
  width: 19px;
  height: 19px;
}
.button svg {
  width: 19px;
  height: 19px;
}
.button.primary {
  color: white;
  background: var(--blue);
  box-shadow: 0 12px 28px rgba(42, 112, 245, 0.23);
}
.button.primary:hover {
  background: #1f62df;
  box-shadow: 0 12px 32px rgba(42, 112, 245, 0.34);
}
.button.secondary {
  border: 1px solid #dfe3eb;
  background: rgba(255, 255, 255, 0.8);
}
.button.secondary:hover {
  border-color: #c8d5ed;
  background: #fff;
  box-shadow: 0 8px 22px rgba(49, 62, 93, 0.09);
}
.hero-meta {
  display: flex;
  justify-content: center;
  gap: 25px;
  margin-top: 30px;
  color: #8a92a1;
  font-size: 12px;
}
.hero-meta span {
  display: flex;
  align-items: center;
  gap: 7px;
}
.status-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: #42b883;
  box-shadow: 0 0 0 4px rgba(66, 184, 131, 0.12);
}
.hero-visual {
  position: relative;
  height: 580px;
}
.hero-product {
  position: relative;
  margin-top: 52px;
  filter: drop-shadow(0 60px 100px rgba(42, 112, 245, 0.12));
}

.hero-product::before {
  content: '';
  position: absolute;
  left: 0;
  top: 0;
  width: 100%;
  height: 100%;
  background: inherit;
  pointer-events: none;
  z-index: 1;
  mask-image: linear-gradient(to bottom, transparent 0%, transparent 50%, black 100%);
  -webkit-mask-image: linear-gradient(to bottom, transparent 0%, transparent 50%, black 100%);
}

.hero-shot {
  display: block;
  box-sizing: border-box;
  width: 100%;
  max-width: 1175px;
  height: auto;
  margin: 0 auto;
  border: 1px solid rgba(215, 220, 231, 0.9);
  border-radius: 10px;
  background: #fff;
  box-shadow:
    0 40px 80px rgba(49, 62, 93, 0.18),
    0 10px 25px rgba(49, 62, 93, 0.08);
  position: relative;
  mask-image: linear-gradient(
    to bottom,
    black 0%,
    black 20%,
    rgba(0, 0, 0, 0.7) 45%,
    rgba(0, 0, 0, 0.3) 70%,
    transparent 100%
  );
  -webkit-mask-image: linear-gradient(
    to bottom,
    black 0%,
    black 20%,
    rgba(0, 0, 0, 0.7) 45%,
    rgba(0, 0, 0, 0.3) 70%,
    transparent 100%
  );
}

.hero-shot::after {
  content: '';
  position: absolute;
  left: 0;
  bottom: 0;
  width: 100%;
  height: 60%;
  background: inherit;
  filter: blur(12px);
  opacity: 0.6;
  pointer-events: none;
  z-index: -1;
  border-radius: 0 0 10px 10px;
  mask-image: linear-gradient(to bottom, transparent 0%, black 40%, black 100%);
  -webkit-mask-image: linear-gradient(to bottom, transparent 0%, black 40%, black 100%);
}
.hero-visual::before {
  content: '';
  position: absolute;
  width: 520px;
  height: 520px;
  right: -60px;
  top: -45px;
  border-radius: 50%;
  background: radial-gradient(
    circle,
    rgba(42, 112, 245, 0.16),
    rgba(133, 94, 234, 0.05) 55%,
    transparent 70%
  );
  filter: blur(2px);
}
.visual-orbit {
  position: absolute;
  border: 1px solid rgba(42, 112, 245, 0.11);
  border-radius: 50%;
}
.orbit-one {
  width: 500px;
  height: 500px;
  right: -50px;
  top: -15px;
}
.orbit-two {
  width: 380px;
  height: 380px;
  right: 10px;
  top: 45px;
}
.app-window {
  position: absolute;
  z-index: 2;
  width: 650px;
  height: 442px;
  top: 45px;
  left: 0;
  overflow: hidden;
  border: 1px solid rgba(215, 220, 231, 0.9);
  border-radius: 18px;
  background: #fff;
  box-shadow:
    0 40px 80px rgba(49, 62, 93, 0.18),
    0 10px 25px rgba(49, 62, 93, 0.08);
}
.window-topbar {
  height: 48px;
  padding: 0 16px;
  display: flex;
  align-items: center;
  border-bottom: 1px solid #eceef3;
  background: #fbfcfe;
}
.traffic {
  display: flex;
  gap: 6px;
  width: 50px;
}
.traffic i {
  width: 8px;
  height: 8px;
  border-radius: 50%;
}
.traffic i:nth-child(1) {
  background: #ff5f57;
  box-shadow: inset 0 0 0 1px rgba(180, 40, 35, 0.18);
}
.traffic i:nth-child(2) {
  background: #febc2e;
  box-shadow: inset 0 0 0 1px rgba(181, 122, 12, 0.15);
}
.traffic i:nth-child(3) {
  background: #28c840;
  box-shadow: inset 0 0 0 1px rgba(25, 128, 42, 0.15);
}
.window-tab {
  height: 30px;
  min-width: 180px;
  padding: 0 15px;
  display: flex;
  align-items: center;
  gap: 8px;
  background: white;
  border: 1px solid #e8eaf0;
  border-radius: 12px;
  font-size: 11px;
  padding-left: 8px;
}
.window-tab img {
  width: 15px;
  height: 15px;
}
.window-tab + .window-tab {
  margin-left: 6px;
  background: #f6f8fb;
}
.tab-icon {
  width: 15px;
  height: 15px;
  color: #617088;
  fill: currentColor;
  stroke: none;
}
.window-plus {
  color: #9198a7;
  padding-left: 12px;
}
.window-body {
  display: flex;
  height: calc(100% - 48px);
}
.app-content {
  flex: 1;
  padding: 27px 25px;
  background: #f6f8fb;
}
.app-heading span,
.app-heading b {
  display: block;
}
.app-heading span {
  font-size: 10px;
  color: #9199a8;
  margin-bottom: 4px;
}
.app-heading b {
  font-size: 17px;
}
.app-grid {
  margin-top: 0px;
  display: grid;
  grid-template-columns: 1.25fr 0.75fr;
  grid-template-rows: repeat(2, minmax(0, 1fr));
  grid-template-areas:
    'library continue'
    'library stat';
  height: 186px;
  gap: 12px;
}
.continue-card,
.stat-card,
.library-card {
  border: 1px solid #e4e8ef;
  border-radius: 12px;
  background: white;
}
.continue-card {
  grid-area: continue;
  padding: 13px;
  display: flex;
  flex-direction: column;
  align-items: stretch;
  justify-content: space-between;
  gap: 7px;
  padding-top: 12px;
}
.continue-content {
  min-width: 0;
  display: flex;
  align-items: center;
  gap: 11px;
}
.continue-content > div:nth-child(2) {
  min-width: 0;
  flex: 1;
}
.continue-card .card-label,
.continue-content strong,
.continue-content span {
  display: block;
}
.continue-card .card-label {
  color: #969dab;
  font-size: 12px;
}
.continue-content strong {
  margin: 3px 0;
  font-size: 12px;
}
.continue-content span {
  color: #8f96a4;
  font-size: 8px;
}
.continue-card button {
  width: 30px;
  height: 30px;
  border: 0;
  border-radius: 50%;
  background: var(--blue);
  color: white;
  font-size: 9px;
}
.game-art {
  position: relative;
  flex: 0 0 40px;
  width: 40px;
  height: 40px;
  overflow: hidden;
  border-radius: 9px;
  background: linear-gradient(#8fd4f8 0 47%, #70b743 48% 68%, #7c5534 69%);
}
.game-art span {
  position: absolute;
  width: 18px;
  height: 15px;
  background: #54a032;
}
.game-art span:nth-child(1) {
  left: 4px;
  top: 18px;
}
.game-art span:nth-child(2) {
  right: -4px;
  top: 16px;
}
.game-art span:nth-child(3) {
  left: 20px;
  top: 25px;
  background: #8b6139;
}
.game-art span:nth-child(4) {
  width: 8px;
  height: 8px;
  right: 12px;
  top: 7px;
  background: #fff3b0;
}
.stat-card {
  grid-area: stat;
  padding: 13px;
  overflow: hidden;
  padding-top: 8px;
}
.stat-card small {
  color: #9097a4;
  font-size: 12px;
}
.stat-card strong {
  display: block;
  margin-top: 6px;
  font-size: 20px;
}
.stat-card strong i {
  font-size: 13px;
  font-style: normal;
  color: #969daa;
  font-weight: 400;
}
.chart {
  height: 42px;
  margin-top: -8px;
  display: flex;
  align-items: end;
  gap: 5px;
}
.chart span {
  flex: 1;
  border-radius: 2px 2px 0 0;
  background: #dfe9ff;
}
.chart span:nth-child(1) {
  height: 30%;
}
.chart span:nth-child(2) {
  height: 55%;
}
.chart span:nth-child(3) {
  height: 40%;
}
.chart span:nth-child(4) {
  height: 78%;
  background: #8ab0fb;
}
.chart span:nth-child(5) {
  height: 62%;
}
.chart span:nth-child(6) {
  height: 90%;
  background: var(--blue);
}
.chart span:nth-child(7) {
  height: 45%;
}
.library-card {
  grid-area: library;
  padding: 14px;
}
.card-title {
  display: flex;
  justify-content: space-between;
  font-size: 15px;
}
.card-title span {
  color: var(--blue);
  font-size: 8px;
}
.instance-row {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
  margin-top: 10px;
}
.instance-card {
  min-width: 0;
  padding: 10px;
  display: flex;
  align-items: center;
  gap: 7px;
  border: 1px solid #edf0f5;
  border-radius: 12px;
  background: #fbfcfe;
}
.instance-card > div:last-child {
  overflow: hidden;
}
.instance-card strong,
.instance-card span {
  display: block;
  white-space: nowrap;
}
.instance-card strong {
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 11px;
}
.instance-card span {
  margin-top: 3px;
  font-size: 10px;
  color: #969daa;
}
.instance-icon {
  flex: 0 0 36px;
  width: 36px;
  height: 36px;
  border-radius: 6px;
  image-rendering: pixelated;
}
.grass {
  background: linear-gradient(#5fa83c 0 35%, #8b613c 36%);
}
.fabric {
  background: url(../assets/fabric.png);
}
.stone {
  background: linear-gradient(135deg, #89929a, #58636c);
}
.nether {
  background: linear-gradient(135deg, #7d2433, #bc4a45);
}
.floating-pill {
  position: absolute;
  z-index: 4;
  display: flex;
  align-items: center;
  border: 1px solid rgba(222, 226, 235, 0.9);
  background: rgba(255, 255, 255, 0.94);
  box-shadow: 0 14px 35px rgba(50, 64, 94, 0.14);
  backdrop-filter: blur(10px);
}
.pill-top {
  top: 20px;
  right: -40px;
  gap: 9px;
  padding: 11px 15px;
  border-radius: 10px;
  font-size: 11px;
}
.pill-top span {
  display: grid;
  place-items: center;
  width: 20px;
  height: 20px;
  border-radius: 50%;
  background: #e5f8ef;
  color: #2ba875;
}
.pill-bottom {
  left: -30px;
  bottom: 60px;
  gap: 11px;
  padding: 12px 17px;
  border-radius: 12px;
}
.pill-bottom > span {
  display: grid;
  place-items: center;
  width: 30px;
  height: 30px;
  border-radius: 9px;
  background: #edf2ff;
  color: var(--blue);
}
.pill-bottom small,
.pill-bottom b {
  display: block;
}
.pill-bottom small {
  font-size: 8px;
  color: #979eac;
}
.pill-bottom b {
  font-size: 11px;
  margin-top: 2px;
}

@media (min-width: 820px) {
  .hero-copy {
    max-width: none;
  }
  h1 {
    white-space: nowrap;
  }
}

@media (max-width: 1100px) {
  .hero {
    padding-top: 146px;
    padding-bottom: 42px;
  }
}

@media (max-width: 820px) {
  .hero {
    padding-top: 136px;
  }
  .floating-pill {
    display: none;
  }
}

@media (max-width: 718px) {
  .desc-desktop {
    display: none;
  }
  .desc-mobile {
    display: inline;
  }
}

@media (max-width: 520px) {
  .eyebrow {
    font-size: 10px;
  }
  .hero-copy > p {
    font-size: 16px;
  }
  .hero-actions {
    flex-direction: column;
  }
  .button {
    width: 100%;
  }
  .hero-meta {
    flex-wrap: wrap;
    gap: 10px 20px;
  }
  .hero-product {
    margin-top: 36px;
  }
}

@media (max-width: 520px) {
  .hero-visual {
    display: none;
  }
}

@media (max-width: 485px) {
  .hero-copy > p.brand-title {
    font-size: clamp(30px, 8.5vw, 40px);
    text-align: left;
    padding-left: 8px;
  }
  h1 {
    text-align: left;
    padding-left: 8px;
  }
  .hero-break {
    display: inline;
  }
}
</style>
