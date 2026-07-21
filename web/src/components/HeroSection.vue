<script setup>
import logoUrl from '../assets/portal-logo.svg'
</script>

<template>
  <section class="hero container">
    <div class="hero-copy">
      <div class="eyebrow"><span></span> 开源 · 跨平台 · 为 Minecraft 而生</div>
      <h1>你的 Minecraft，<br /><em>从这里出发。</em></h1>
      <p>Portal 把启动、实例、资源与记录收进一个轻盈的工作区。少一点配置，多一点游戏。</p>
      <div class="hero-actions">
        <a class="button primary" href="#download">
          立即下载
          <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 3v12m0 0 5-5m-5 5-5-5M5 21h14" /></svg>
        </a>
        <a class="button secondary" href="https://github.com/tiouoo/Portal" target="_blank" rel="noreferrer">
          查看源代码
          <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M7 17 17 7M8 7h9v9" /></svg>
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
          <span class="window-plus">+</span>
        </div>
        <div class="window-body">
          <aside class="app-sidebar">
            <img :src="logoUrl" alt="Portal" class="sidebar-logo" />
            <div class="side-icon active"><svg viewBox="0 0 24 24"><path d="M4 10.5 12 4l8 6.5V20H4zM9 20v-6h6v6" /></svg></div>
            <div class="side-icon"><svg viewBox="0 0 24 24"><rect x="4" y="4" width="16" height="16" rx="3"/><path d="M8 9h8M8 13h5"/></svg></div>
            <div class="side-icon"><svg viewBox="0 0 24 24"><path d="M12 3v12m0 0 4-4m-4 4-4-4M5 20h14"/></svg></div>
            <div class="side-icon bottom"><svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="3"/><path d="M19 12a7 7 0 0 0-.1-1l2-1.5-2-3.4-2.4 1a8 8 0 0 0-1.8-1L14.4 3h-4.8l-.4 3a8 8 0 0 0-1.8 1L5 6.1 3 9.5 5.1 11a7 7 0 0 0 0 2L3 14.5 5 18l2.4-1a8 8 0 0 0 1.8 1l.4 3h4.8l.4-3a8 8 0 0 0 1.8-1l2.4 1 2-3.5-2.1-1.5a7 7 0 0 0 .1-1Z"/></svg></div>
          </aside>
          <div class="app-content">
            <div class="app-heading"><span>下午好</span><b>准备好开始冒险了吗？</b></div>
            <div class="app-grid">
              <div class="continue-card">
                <div class="game-art"><span></span><span></span><span></span><span></span></div>
                <div><small>继续游戏</small><strong>创造世界</strong><span>Fabric · 1.21.5</span></div>
                <button aria-label="启动">▶</button>
              </div>
              <div class="stat-card"><small>本周游戏时长</small><strong>12.6 <i>小时</i></strong><div class="chart"><span></span><span></span><span></span><span></span><span></span><span></span><span></span></div></div>
              <div class="library-card">
                <div class="card-title"><b>我的实例</b><span>查看全部 →</span></div>
                <div class="instance-row">
                  <div class="instance-icon grass"></div><div><strong>创造世界</strong><span>Fabric · 1.21.5</span></div>
                  <div class="instance-icon stone"></div><div><strong>原版生存</strong><span>Vanilla · 1.20.1</span></div>
                  <div class="instance-icon nether"></div><div><strong>机械动力</strong><span>Forge · 1.19.2</span></div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
      <div class="floating-pill pill-top"><span>✓</span> 资源安装完成</div>
      <div class="floating-pill pill-bottom"><span>↗</span><div><small>游戏时长</small><b>+ 2.4 小时</b></div></div>
    </div>
  </section>
</template>

<style scoped src="../styles/hero-section.css"></style>
