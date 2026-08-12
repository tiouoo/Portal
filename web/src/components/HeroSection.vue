<script setup>
import logoUrl from "../assets/portal-logo.svg";
</script>

<template>
  <section class="hero container">
    <div class="hero-copy">
      <div class="eyebrow"><span></span> 开源 · 跨平台 · 为 Minecraft 而生</div>
      <h1>你的 Minecraft，<br /><em>从这里出发</em></h1>
      <p>
        Portal 把启动、实例、资源与记录收进一个工作区。<br />少一点配置，多一点游戏。
      </p>
      <div class="hero-actions">
        <a class="button primary" href="#download">
          立即下载
          <svg viewBox="0 0 24 24" aria-hidden="true">
            <path d="M12 3v12m0 0 5-5m-5 5-5-5M5 21h14" />
          </svg>
        </a>
        <a
          class="button secondary"
          href="https://github.com/tiouoo/Portal"
          target="_blank"
          rel="noreferrer"
        >
          查看源代码

          <svg
            color="#182033"
            fill="#182033"
            xmlns="http://www.w3.org/2000/svg"
            viewBox="0 0 640 640"
          >
            <path
              d="M566.6 342.6C579.1 330.1 579.1 309.8 566.6 297.3L406.6 137.3C394.1 124.8 373.8 124.8 361.3 137.3C348.8 149.8 348.8 170.1 361.3 182.6L466.7 288L96 288C78.3 288 64 302.3 64 320C64 337.7 78.3 352 96 352L466.7 352L361.3 457.4C348.8 469.9 348.8 490.2 361.3 502.7C373.8 515.2 394.1 515.2 406.6 502.7L566.6 342.7z"
            />
          </svg>
        </a>
      </div>
      <div class="hero-meta">
        <span><i class="status-dot"></i> 持续更新</span>
        <span>Windows · macOS · Linux</span>
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
                d="M560.3 301.2C570.7 313 588.6 315.6 602.1 306.7C616.8 296.9 620.8 277 611 262.3L563 190.3C560.2 186.1 556.4 182.6 551.9 180.1L351.4 68.7C332.1 58 308.6 58 289.2 68.7L88.8 180C83.4 183 79.1 187.4 76.2 192.8L27.7 282.7C15.1 306.1 23.9 335.2 47.3 347.8L80.3 365.5L80.3 418.8C80.3 441.8 92.7 463.1 112.7 474.5L288.7 574.2C308.3 585.3 332.2 585.3 351.8 574.2L527.8 474.5C547.9 463.1 560.2 441.9 560.2 418.8L560.2 301.3zM320.3 291.4L170.2 208L320.3 124.6L470.4 208L320.3 291.4zM278.8 341.6L257.5 387.8L91.7 299L117.1 251.8L278.8 341.6z"
              />
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
                      "
                    >
                      <img
                        width="40"
                        height="40"
                        src="../assets/fabrici.png"
                        alt="Fabric"
                      />
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
                  viewBox="0 0 640 640"
                >
                  <path
                    d="M64 320C64 178.6 178.6 64 320 64C461.4 64 576 178.6 576 320C576 461.4 461.4 576 320 576C178.6 576 64 461.4 64 320zM252.3 211.1C244.7 215.3 240 223.4 240 232L240 408C240 416.7 244.7 424.7 252.3 428.9C259.9 433.1 269.1 433 276.6 428.4L420.6 340.4C427.7 336 432.1 328.3 432.1 319.9C432.1 311.5 427.7 303.8 420.6 299.4L276.6 211.4C269.2 206.9 259.9 206.7 252.3 210.9z"
                  />
                </svg>
              </div>
              <div class="stat-card">
                <small>本周游戏时长</small
                ><strong style="margin-top: 2px">12.6 <i>小时</i></strong>
                <div class="chart">
                  <span></span><span></span><span></span><span></span
                  ><span></span><span></span><span></span>
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
                      class="instance-icon"
                    >
                      <img
                        width="36"
                        height="36"
                        src="../assets/fabrici.png"
                        alt="Fabric"
                      />
                    </div>
                    <div>
                      <strong>生电整合包</strong><span>Fabric · 1.21.1</span>
                    </div>
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
                      class="instance-icon"
                    >
                      <img
                        width="36"
                        height="36"
                        src="../assets/grass.png"
                        alt="grass"
                      />
                    </div>
                    <div>
                      <strong>原版生存</strong><span>原版 · 1.20.1</span>
                    </div>
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
                      class="instance-icon"
                    >
                      <img
                        width="36"
                        height="36"
                        src="../assets/create.jpg"
                        alt="create"
                      />
                    </div>
                    <div>
                      <strong>机械动力</strong><span>Forge · 1.19.2</span>
                    </div>
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
    <img class="hero-shot" src="../assets/pic.png" alt="Portal 应用界面" />
  </section>
</template>

<style scoped>
.hero {
  min-height: 780px;
  padding-top: 150px;
  display: grid;
  grid-template-columns: 0.8fr 1.2fr;
  align-items: center;
  gap: 52px;
}
.hero-copy {
  position: relative;
  z-index: 2;
  padding-bottom: 55px;
}
.eyebrow {
  color: var(--blue);
  font-size: 13px;
  letter-spacing: 0.12em;
  font-weight: 700;
  display: flex;
  align-items: center;
  gap: 9px;
  margin-bottom: 24px;
}
.eyebrow span {
  display: block;
  width: 24px;
  height: 2px;
  background: var(--blue);
}
h1 {
  margin: 0;
  font-size: clamp(52px, 5.2vw, 76px);
  line-height: 1.08;
  letter-spacing: -0.055em;
  color: #182033;
}
h1 em {
  color: var(--blue);
  font-style: normal;
}
.hero-copy > p {
  max-width: 500px;
  margin: 26px 0 32px;
  color: var(--muted);
  font-size: 18px;
  line-height: 1.85;
}
.hero-actions {
  display: flex;
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
.hero-shot {
  position: relative;
  z-index: 2;
  display: block;
  box-sizing: border-box;
  width: 100%;
  height: auto;
  border: 1px solid rgba(215, 220, 231, 0.9);
  border-radius: 10px;
  background: #fff;
  box-shadow:
    0 40px 80px rgba(49, 62, 93, 0.18),
    0 10px 25px rgba(49, 62, 93, 0.08);
}
.hero-visual::before {
  content: "";
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
    "library continue"
    "library stat";
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

@media (max-width: 1100px) {
  .hero {
    grid-template-columns: 1fr;
    padding-top: 140px;
    padding-bottom: 50px;
    text-align: center;
  }
  .hero-copy {
    padding-bottom: 0;
  }
  .eyebrow,
  .hero-actions,
  .hero-meta {
    justify-content: center;
  }
  .hero-copy > p {
    margin-left: auto;
    margin-right: auto;
  }
  .hero-visual {
    width: min(720px, 100%);
    margin: 0 auto;
  }
  .hero-shot {
    width: min(720px, 100%);
    margin: 0 auto;
  }
  .app-window {
    left: 50%;
    transform: translateX(-50%);
  }
}

@media (max-width: 820px) {
  .hero {
    min-height: auto;
    padding-top: 125px;
  }
  .hero-visual {
    height: 440px;
  }
  .app-window {
    width: 600px;
    transform: translateX(-50%) scale(0.78);
    transform-origin: top center;
  }
  .floating-pill {
    display: none;
  }
}

@media (max-width: 520px) {
  .hero {
    text-align: left;
    gap: 15px;
  }
  .eyebrow,
  .hero-actions,
  .hero-meta {
    justify-content: flex-start;
  }
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
  .hero-visual {
    height: 355px;
    margin-top: 18px;
  }
  .app-window {
    transform: translateX(-50%) scale(0.61);
  }
}

@media (max-width: 520px) {
  .hero-visual {
    display: none;
  }
  .hero-shot {
    width: 100%;
  }
}
</style>
