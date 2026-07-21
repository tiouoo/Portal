<script setup>
import { ref } from 'vue'
import logoUrl from './assets/portal-logo.svg'

const menuOpen = ref(false)

const releaseBase = 'https://github.com/tiouoo/Portal/releases/download/publish-nightly'

const platforms = [
  {
    id: 'windows',
    name: 'Windows',
    detail: 'Windows 10 / 11',
    icon: 'windows',
    primary: 'x64 安装程序',
    primaryUrl: `${releaseBase}/Portal.win.x64.installer.exe`,
    links: [
      { label: 'x64 安装程序', meta: '.exe', url: `${releaseBase}/Portal.win.x64.installer.exe` },
      { label: 'x64 便携版', meta: '.zip', url: `${releaseBase}/Portal.win.x64.portable.zip` }
    ]
  },
  {
    id: 'macos',
    name: 'macOS',
    detail: 'Intel 与 Apple 芯片',
    icon: 'apple',
    primary: 'Apple 芯片版',
    primaryUrl: `${releaseBase}/Portal.osx.mac.arm64.dmg`,
    links: [
      { label: 'Apple 芯片', meta: 'ARM64 · .dmg', url: `${releaseBase}/Portal.osx.mac.arm64.dmg` },
      { label: 'Intel 芯片', meta: 'x64 · .dmg', url: `${releaseBase}/Portal.osx.mac.x64.dmg` },
      { label: 'Apple 芯片应用包', meta: 'ARM64 · .zip', url: `${releaseBase}/Portal.osx.mac.arm64.app.zip` },
      { label: 'Intel 芯片应用包', meta: 'x64 · .zip', url: `${releaseBase}/Portal.osx.mac.x64.app.zip` }
    ]
  },
  {
    id: 'linux',
    name: 'Linux',
    detail: 'AppImage 免安装运行',
    icon: 'linux',
    primary: 'x64 AppImage',
    primaryUrl: `${releaseBase}/Portal.linux.x64.AppImage`,
    links: [
      { label: '通用桌面版', meta: 'x64 · AppImage', url: `${releaseBase}/Portal.linux.x64.AppImage` },
      { label: 'ARM64 版', meta: 'ARM64 · AppImage', url: `${releaseBase}/Portal.linux.arm64.AppImage` },
      { label: 'ARM 版', meta: '32-bit ARM · AppImage', url: `${releaseBase}/Portal.linux.arm.AppImage` }
    ]
  }
]

const features = [
  {
    index: '01',
    title: '一处管理全部实例',
    text: '搜索、排序、收藏与快速启动。Java 与基岩版实例保持清晰有序，最近游玩和时长统计一目了然。',
    tone: 'blue'
  },
  {
    index: '02',
    title: '资源发现无需绕路',
    text: '连接 Modrinth 与 CurseForge，在启动器内查找并安装模组、整合包、资源包、光影、数据包与地图。',
    tone: 'purple'
  },
  {
    index: '03',
    title: '不止是启动游戏',
    text: '查看存档信息、筛选实时日志、分析投影材料。游戏之外的琐事，也交给 Portal 处理。',
    tone: 'mint'
  }
]

function closeMenu() {
  menuOpen.value = false
}
</script>

<template>
  <div class="site-shell">
    <header class="nav-wrap">
      <nav class="nav container" aria-label="主导航">
        <a class="brand" href="#top" aria-label="Portal 首页" @click="closeMenu">
          <img :src="logoUrl" alt="" />
          <span>Portal</span>
        </a>
        <button class="menu-button" type="button" aria-label="切换导航菜单" :aria-expanded="menuOpen" @click="menuOpen = !menuOpen">
          <span></span><span></span>
        </button>
        <div class="nav-links" :class="{ open: menuOpen }">
          <a href="#features" @click="closeMenu">功能</a>
          <a href="#download" @click="closeMenu">下载</a>
          <a href="https://github.com/tiouoo/Portal" target="_blank" rel="noreferrer">GitHub</a>
          <a class="nav-cta" href="#download" @click="closeMenu">获取 Portal</a>
        </div>
      </nav>
    </header>

    <main id="top">
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

      <section id="features" class="features section container">
        <div class="section-heading">
          <span class="section-kicker">更好的游戏入口</span>
          <h2>把复杂留在幕后</h2>
          <p>每一项功能都围绕同一件事：让你更快抵达想去的世界。</p>
        </div>
        <div class="feature-grid">
          <article v-for="feature in features" :key="feature.index" class="feature-card" :class="feature.tone">
            <div class="feature-index">{{ feature.index }}</div>
            <div class="feature-graphic" aria-hidden="true">
              <template v-if="feature.index === '01'">
                <div class="mini-stack"><i></i><i></i><i></i></div>
                <div class="mini-window"><span></span><b></b><b></b><b></b></div>
              </template>
              <template v-else-if="feature.index === '02'">
                <div class="resource-cube cube-a">M</div><div class="resource-cube cube-b">C</div><div class="resource-line"></div>
              </template>
              <template v-else>
                <div class="log-panel"><i></i><i></i><i></i><i></i></div><div class="pulse-ring"><span>✓</span></div>
              </template>
            </div>
            <h3>{{ feature.title }}</h3>
            <p>{{ feature.text }}</p>
          </article>
        </div>

        <div class="capability-strip">
          <span>还支持</span>
          <div><b>多标签工作区</b><i></i><b>Microsoft / 离线账户</b><i></i><b>实时日志</b><i></i><b>存档信息</b><i></i><b>Litematica 分析</b></div>
        </div>
      </section>

      <section id="download" class="download-section section">
        <div class="container">
          <div class="download-heading">
            <div>
              <span class="section-kicker">现在开始</span>
              <h2>选择你的平台</h2>
            </div>
            <p>Nightly 版本随项目持续更新。所有安装包均由 GitHub Actions 从公开源代码构建并发布。</p>
          </div>
          <div class="platform-grid">
            <article v-for="platform in platforms" :key="platform.id" class="platform-card">
              <div class="platform-head">
                <div class="platform-icon">
                  <svg v-if="platform.icon === 'windows'" viewBox="0 0 24 24"><path d="M3 5.2 10.5 4v7.2H3V5.2Zm8.5-1.4L21 2.5v8.7h-9.5V3.8ZM3 12.3h7.5v7.2L3 18.4v-6.1Zm8.5 0H21V21l-9.5-1.3v-7.4Z" /></svg>
                  <svg v-else-if="platform.icon === 'apple'" viewBox="0 0 24 24"><path d="M16.7 12.7c0-2.4 2-3.6 2.1-3.7a4.6 4.6 0 0 0-3.6-2c-1.5-.2-3 1-3.8 1s-2-1-3.3-1C6.4 7 4.8 8 3.9 9.5c-1.9 3.3-.5 8.1 1.3 10.7.9 1.3 2 2.7 3.4 2.6 1.3-.1 1.8-.9 3.5-.9 1.6 0 2.1.9 3.5.9s2.4-1.3 3.3-2.6c1-1.5 1.5-3 1.5-3.1-.1 0-3.7-1.4-3.7-4.4ZM14.2 5.4A4.4 4.4 0 0 0 15.3 2a4.7 4.7 0 0 0-3.1 1.6A4.1 4.1 0 0 0 11 6.8c1.2.1 2.4-.5 3.2-1.4Z" /></svg>
                  <svg v-else viewBox="0 0 24 24"><path d="M12 2c-2.4 0-4.2 2.3-4.2 5.3v2.2c-1.4 1-2.4 2.8-2.8 4.8l-1.8 3.2c-.5.9.2 2 1.2 2H8c1 1.6 2.3 2.5 4 2.5s3-.9 4-2.5h3.6c1 0 1.7-1.1 1.2-2L19 14.3c-.4-2-1.4-3.8-2.8-4.8V7.3C16.2 4.3 14.4 2 12 2Zm-1.7 5.2c-.5 0-.8-.5-.8-1s.3-1 .8-1 .8.5.8 1-.3 1-.8 1Zm3.4 0c-.5 0-.8-.5-.8-1s.3-1 .8-1 .8.5.8 1-.3 1-.8 1Z" /></svg>
                </div>
                <div><h3>{{ platform.name }}</h3><p>{{ platform.detail }}</p></div>
              </div>
              <a class="platform-primary" :href="platform.primaryUrl">{{ platform.primary }} <span>↓</span></a>
              <div class="platform-links">
                <a v-for="link in platform.links" :key="link.url" :href="link.url">
                  <span>{{ link.label }}<small>{{ link.meta }}</small></span>
                  <svg viewBox="0 0 24 24"><path d="M12 4v11m0 0 4-4m-4 4-4-4M5 20h14" /></svg>
                </a>
              </div>
            </article>
          </div>
          <p class="download-note"><span>i</span> 基岩版游戏的安装与启动功能目前仅在 Windows 上提供。macOS 如遇安全提示，请参考 GitHub 仓库说明。</p>
        </div>
      </section>

      <section class="open-source container section">
        <div class="oss-mark"><img :src="logoUrl" alt="" /></div>
        <div class="oss-copy">
          <span class="section-kicker">自由且透明</span>
          <h2>由社区共同塑造</h2>
          <p>Portal 以 GPL-3.0-or-later 许可开源。你可以检查每一行代码、提交问题，或让自己的想法成为下一次更新的一部分。</p>
          <div class="oss-actions">
            <a class="button dark" href="https://github.com/tiouoo/Portal" target="_blank" rel="noreferrer">
              <svg viewBox="0 0 24 24"><path d="M12 2a10 10 0 0 0-3.2 19.5c.5.1.7-.2.7-.5v-2c-2.8.6-3.4-1.2-3.4-1.2-.5-1.2-1.1-1.5-1.1-1.5-.9-.6.1-.6.1-.6 1 0 1.5 1 1.5 1 .9 1.5 2.3 1.1 2.9.8.1-.6.3-1.1.6-1.3-2.2-.3-4.6-1.1-4.6-5A3.9 3.9 0 0 1 5.4 9c-.1-.3-.5-1.3.1-2.7 0 0 .9-.3 2.8 1a9.7 9.7 0 0 1 5.1 0c2-1.3 2.8-1 2.8-1 .6 1.4.2 2.4.1 2.7a3.9 3.9 0 0 1 1.1 2.7c0 3.9-2.4 4.7-4.6 5 .4.3.7.9.7 1.8V21c0 .3.2.6.7.5A10 10 0 0 0 12 2Z" /></svg>
              GitHub 仓库
            </a>
            <a class="text-link" href="https://github.com/tiouoo/Portal/issues" target="_blank" rel="noreferrer">反馈问题 <span>↗</span></a>
          </div>
        </div>
        <div class="repo-card">
          <div class="repo-title"><img :src="logoUrl" alt="" /><span><small>github.com/tiouoo</small><b>Portal</b></span></div>
          <div class="code-lines"><i></i><i></i><i></i><i></i><i></i></div>
          <div class="repo-footer"><span><i></i> C#</span><b>GPL-3.0</b></div>
        </div>
      </section>
    </main>

    <footer>
      <div class="container footer-inner">
        <a class="brand" href="#top"><img :src="logoUrl" alt="" /><span>Portal</span></a>
        <p>Copyright © Portal 2026. 基于 GPL-3.0-or-later 开源许可发布。</p>
        <div class="footer-links">
          <a href="https://github.com/tiouoo/Portal" target="_blank" rel="noreferrer">GitHub</a>
          <a href="https://github.com/tiouoo/Portal/releases" target="_blank" rel="noreferrer">发行版本</a>
          <a href="mailto:tiouo@qq.com">联系</a>
        </div>
      </div>
      <p class="disclaimer container">Portal 是独立开源项目，与 Mojang Studios 或 Microsoft 没有从属关系。Minecraft 是 Microsoft 旗下商标。</p>
    </footer>
  </div>
</template>
