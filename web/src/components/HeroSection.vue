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

    <div class="hero-visual" aria-label="Portal 应用界面示意">
      <div class="visual-orbit orbit-one"></div>
      <div class="visual-orbit orbit-two"></div>
      <div class="app-window">
        <div class="window-topbar">
          <div class="traffic"><i></i><i></i><i></i></div>
          <div class="window-tab"><img :src="logoUrl" alt="" /> 新标签页</div>
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
            <div class="app-heading">
              <span>下午好</span><b>准备好开始冒险了吗？</b>
            </div>
            <div class="app-grid">
              <div class="continue-card">
                <div class="game-art">
                  <span></span><span></span><span></span><span></span>
                </div>
                <div>
                  <small>继续游戏</small><strong>创造世界</strong
                  ><span>Fabric · 1.21.5</span>
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
                <small>本周游戏时长</small><strong>12.6 <i>小时</i></strong>
                <div class="chart">
                  <span></span><span></span><span></span><span></span
                  ><span></span><span></span><span></span>
                </div>
              </div>
              <div class="library-card">
                <div class="card-title">
                  <b>我的实例</b><span>查看全部 →</span>
                </div>
                <div class="instance-row">
                  <div class="instance-icon grass"></div>
                  <div>
                    <strong>创造世界</strong><span>Fabric · 1.21.5</span>
                  </div>
                  <div class="instance-icon grass"></div>
                  <div>
                    <strong>原版生存</strong><span>Vanilla · 1.20.1</span>
                  </div>
                  <div class="instance-icon grass"></div>
                  <div>
                    <strong>机械动力</strong><span>Forge · 1.19.2</span>
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
  </section>
</template>

<style scoped src="../styles/hero-section.css"></style>
