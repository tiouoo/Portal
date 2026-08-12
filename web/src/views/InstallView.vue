<template>
  <main class="install-page">
    <section class="install-hero container">
      <h1>安装说明</h1>
      <p class="intro">根据您的操作系统选择对应的安装指南，快速开始使用 Portal。</p>
    </section>

    <section class="platform-tabs container" aria-labelledby="platform-title">
      <h2 id="platform-title" class="sr-only">选择操作系统</h2>
      <div class="tabs">
        <button
          class="tab-button"
          :class="{ active: activeTab === 'macos' }"
          @click="activeTab = 'macos'"
          aria-controls="macos-content"
          :aria-selected="activeTab === 'macos'">
          <svg
            class="tab-icon"
            xmlns="http://www.w3.org/2000/svg"
            viewBox="0 0 24 24"
            fill="currentColor">
            <path
              d="M17.05 20.28c-.98.95-2.05.8-3.08.35-1.09-.46-2.09-.48-3.24 0-1.44.62-2.2.44-3.06-.35C2.79 15.25 3.51 7.59 9.05 7.31c1.35.07 2.29.74 3.08.8 1.18-.24 2.31-.93 3.57-.84 1.51.12 2.65.72 3.4 1.8-3.12 1.87-2.38 5.98.48 7.13-.57 1.5-1.31 2.99-2.54 4.09l.01-.01zM12.03 7.25c-.15-2.23 1.66-4.07 3.74-4.25.29 2.58-2.34 4.5-3.74 4.25z" />
          </svg>
          macOS
        </button>
        <button
          class="tab-button"
          :class="{ active: activeTab === 'linux' }"
          @click="activeTab = 'linux'"
          aria-controls="linux-content"
          :aria-selected="activeTab === 'linux'">
          <svg
            class="tab-icon"
            xmlns="http://www.w3.org/2000/svg"
            viewBox="0 0 24 24"
            fill="currentColor">
            <path
              d="M12 2c-2.4 0-4.2 2.3-4.2 5.3v2.2c-1.4 1-2.4 2.8-2.8 4.8l-1.8 3.2c-.5.9.2 2 1.2 2H8c1 1.6 2.3 2.5 4 2.5s3-.9 4-2.5h3.6c1 0 1.7-1.1 1.2-2L19 14.3c-.4-2-1.4-3.8-2.8-4.8V7.3C16.2 4.3 14.4 2 12 2Zm-1.7 5.2c-.5 0-.8-.5-.8-1s.3-1 .8-1 .8.5.8 1-.3 1-.8 1Zm3.4 0c-.5 0-.8-.5-.8-1s.3-1 .8-1 .8.5.8 1-.3 1-.8 1Z" />
          </svg>
          Linux
        </button>
      </div>
    </section>

    <div class="tab-content container">
      <!-- macOS 内容 -->
      <div
        v-show="activeTab === 'macos'"
        id="macos-content"
        role="tabpanel"
        aria-labelledby="macos-tab"
        class="platform-content">
        <section class="install-content" aria-labelledby="macos-install-title">
          <h2 id="macos-install-title">安装前确认</h2>
          <p class="section-intro">
            如果 macOS 提示"Portal 已损坏，无法打开"或文件损坏，请按照以下说明安装并解除系统限制。
          </p>

          <div style="border-radius: 16px" class="architecture-note">
            <div>
              <h3>选择正确的安装包</h3>
              <p>
                <b>Apple 芯片（M1/M2/M3/M4 等）</b>请下载带 <code>arm64</code> 的文件。<b
                  >Intel 芯片</b
                >请下载带 <code>x64</code> 的文件。
              </p>
            </div>
          </div>

          <div class="install-grid install-grid-2">
            <article style="border-radius: 16px" class="install-card">
              <p class="card-label">dmg 安装包</p>
              <h3>打开并拖入应用程序</h3>
              <ol>
                <li>双击打开下载的 dmg 镜像文件。</li>
                <li>将 Portal 图标拖动到右侧的"应用程序"文件夹。</li>
                <li>打开"终端"：应用程序 - 实用工具 - 终端，或按 Command + 空格搜索"终端"。</li>
              </ol>
            </article>
            <article style="border-radius: 16px" class="install-card">
              <p class="card-label">app.zip 压缩包</p>
              <h3>解压后移入应用程序</h3>
              <ol>
                <li>双击解压 <code>app.zip</code> 文件。</li>
                <li>将解压出来的 Portal.app 移动到"应用程序"文件夹。</li>
                <li>打开"终端"：应用程序 - 实用工具 - 终端，或按 Command + 空格搜索"终端"。</li>
              </ol>
            </article>
          </div>

          <section
            style="border-radius: 16px"
            class="command-panel"
            aria-labelledby="command-title">
            <div class="command-copy">
              <h2 id="command-title">在终端执行命令</h2>
              <p>
                复制下面的命令到终端并按回车。系统会要求输入 Mac
                开机密码；输入时屏幕不会显示字符或星号，输入完成后直接按回车即可。
              </p>
            </div>
            <code style="user-select: text; border-radius: 16px"
              >sudo xattr -rd com.apple.quarantine /Applications/Portal.app</code
            >
          </section>

          <aside style="border-radius: 16px" class="fallback-note">
            <strong>仍然无法打开？</strong>
            <p>
              打开"系统设置 - 隐私与安全性"，滚动到"安全性"部分，点击"仍要打开"，然后重新打开
              Portal。
            </p>
            <hr class="divider" />
            <strong>遇到问题？</strong>
            <p>
              如果安装过程中遇到任何问题，请访问 <a
                href="https://github.com/tiouoo/Portal/issues"
                target="_blank"
                rel="noopener noreferrer"
                >GitHub Issues</a> 寻求帮助，或 <a href="/#open-source">加入官方 QQ 群</a> 与其他用户交流。
            </p>
          </aside>
        </section>
      </div>

      <!-- Linux 内容 -->
      <div
        v-show="activeTab === 'linux'"
        id="linux-content"
        role="tabpanel"
        aria-labelledby="linux-tab"
        class="platform-content">
        <section class="install-content" aria-labelledby="linux-install-title">
          <h2 id="linux-install-title">选择安装方式</h2>
          <p class="section-intro">
            Portal 为 Linux 提供了多种安装包格式，包括 AppImage、Rrm 和 Deb
            包。请根据您的发行版选择合适的安装方式。
          </p>

          <div class="install-grid install-grid-3">
            <article style="border-radius: 16px" class="install-card">
              <p class="card-label">AppImage 通用格式</p>
              <h3>适用于所有发行版</h3>
              <ol>
                <li>下载 <code>.AppImage</code> 文件到您选择的目录。</li>
                <li>打开终端，进入文件所在目录。</li>
                <li>赋予执行权限并运行：</li>
              </ol>
              <div class="sub-commands">
                <div>
                  <strong>赋予执行权限：</strong>
                  <pre
                    style="
                      border-radius: 16px;
                    "><code style="user-select: text">chmod +x Portal.AppImage</code></pre>
                </div>
                <div>
                  <strong>双击运行或在终端执行：</strong>
                  <pre
                    style="
                      border-radius: 16px;
                    "><code style="user-select: text">./Portal.AppImage</code></pre>
                </div>
              </div>
            </article>

            <article style="border-radius: 16px" class="install-card">
              <p class="card-label">Deb 包</p>
              <h3>适用于 Debian/Ubuntu 系</h3>
              <ol>
                <li>下载 <code>.deb</code> 文件。</li>
                <li>双击安装或使用终端命令：</li>
              </ol>
              <div class="sub-commands">
                <div>
                  <strong>安装命令：</strong>
                  <pre
                    style="
                      border-radius: 16px;
                    "><code style="user-select: text">sudo dpkg -i Portal.deb</code></pre>
                </div>
                <div>
                  <strong>如果出现依赖问题，执行：</strong>
                  <pre
                    style="
                      border-radius: 16px;
                    "><code style="user-select: text">sudo apt-get install -f</code></pre>
                </div>
              </div>
            </article>

            <article style="border-radius: 16px" class="install-card">
              <p class="card-label">Rpm 包</p>
              <h3>适用于 Fedora/RHEL/openSUSE</h3>
              <ol>
                <li>下载 <code>.rpm</code> 文件。</li>
                <li>使用对应的包管理器安装：</li>
              </ol>
              <div class="sub-commands">
                <div>
                  <strong>Fedora/RHEL:</strong>
                  <pre
                    style="
                      border-radius: 16px;
                    "><code style="user-select: text">sudo dnf install Portal.rpm</code></pre>
                </div>
                <div>
                  <strong>openSUSE:</strong>
                  <pre
                    style="
                      border-radius: 16px;
                    "><code style="user-select: text">sudo zypper install Portal.rpm</code></pre>
                </div>
              </div>
            </article>
          </div>

          <section
            style="border-radius: 16px"
            class="dependencies-panel"
            aria-labelledby="deps-title">
            <h2 id="deps-title">Ubuntu/Debian 系统依赖库</h2>
            <p>
              在某些 Ubuntu 系统上运行 AppImage
              可能需要安装额外的依赖库。如果遇到启动问题，请执行以下命令安装所需依赖：
            </p>
            <code style="user-select: text; border-radius: 16px"
              >sudo apt-get update && sudo apt-get install -y libfuse2 libgtk-3-0 libnotify4 libnss3
              libxss1 libxtst6 xdg-utils libatspi2.0-0 libdrm2 libgbm1 libxcb-dri3-0</code
            >
            <p  style="border-radius: 16px;" class="note">
              <strong>提示：</strong>如果您使用的是较新的 Ubuntu 22.04+ 版本，可能只需要安装
              <code>libfuse2</code>：
            </p>
            <code style="user-select: text; border-radius: 16px"
              >sudo apt-get install libfuse2</code
            >
          </section>

          <aside style="border-radius: 16px" class="fallback-note">
            <strong>遇到问题？</strong>
            <p>
              如果安装过程中遇到任何问题，请访问 <a
                href="https://github.com/tiouoo/Portal/issues"
                target="_blank"
                rel="noopener noreferrer"
                >GitHub Issues</a> 寻求帮助，或 <a href="/#open-source">加入官方 QQ 群</a> 与其他用户交流。
            </p>
          </aside>
        </section>
      </div>
    </div>
  </main>
</template>

<script setup>
import { ref, onMounted } from 'vue';
import { useRoute } from 'vue-router';

const route = useRoute();
const activeTab = ref('macos');

onMounted(() => {
  // 根据 URL 参数切换标签页
  if (route.query.platform === 'linux') {
    activeTab.value = 'linux';
  }
});
</script>

<style scoped>
.install-page {
  min-height: calc(100vh - 166px);
  padding: 148px 0 108px;
  background: linear-gradient(180deg, #f4f7fd 0, #f8f9fc 420px);
}
.install-hero {
  padding-left: 12px;
}
.install-hero h1 {
  margin: 0 0 16px;
  color: #182033;
  font-size: clamp(36px, 5vw, 54px);
  letter-spacing: 0;
  line-height: 1.2;
}
.intro {
  margin: 0;
  color: #657086;
  font-size: 17px;
  line-height: 1.85;
}
.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border-width: 0;
}
.platform-tabs {
  margin-top: 10px;
}
.tabs {
  display: flex;
  gap: 12px;
  border-bottom: 2px solid #e5e8ef;
}
.tab-button {
  position: relative;
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 14px 24px;
  border: none;
  background: transparent;
  color: #657086;
  font-size: 16px;
  font-weight: 600;
  cursor: pointer;
  transition: color 0.2s;
}
.tab-button:hover {
  color: var(--blue);
}
.tab-button.active {
  color: var(--blue);
}
.tab-button.active::after {
  content: '';
  position: absolute;
  left: 0;
  right: 0;
  bottom: -2px;
  height: 2px;
  background: var(--blue);
}
.tab-icon {
  width: 20px;
  height: 20px;
}
.tab-content {
  animation: fadeIn 0.3s ease-in-out;
}
.platform-content {
  animation: fadeIn 0.3s ease-in-out;
}

@keyframes fadeIn {
  from {
    opacity: 0;
    transform: translateY(10px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

/* 内容样式 */
.install-content {
  margin-top: 40px;
}
.install-content > h2 {
  margin: 0 0 12px;
  color: #202b40;
  font-size: 26px;
}
.section-intro {
  margin: 0 0 24px;
  color: #657086;
  font-size: 15px;
  line-height: 1.75;
}
.card-label {
  margin: 0;
  color: var(--blue);
  font-size: 12px;
  font-weight: 800;
  letter-spacing: 0.1em;
}
.architecture-note {
  padding: 22px 24px;
  border: 1px solid #d7e1f3;
  background: #f3f7ff;
}
.architecture-note h3,
.install-card h3,
.command-copy h2,
.dependencies-panel h2 {
  margin: 0;
  color: #26324a;
}
.architecture-note h3 {
  font-size: 15px;
}
.architecture-note p,
.command-copy p {
  margin: 7px 0 0;
  color: #667187;
  font-size: 14px;
  line-height: 1.8;
}
.architecture-note b {
  color: #33405a;
}
.install-page code {
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
}
.install-grid {
  display: grid;
  gap: 18px;
  margin-top: 18px;
}
.install-grid-2 {
  grid-template-columns: repeat(2, 1fr);
}
.install-grid-3 {
  grid-template-columns: repeat(3, 1fr);
}

@media (max-width: 1024px) {
  .install-grid-2,
  .install-grid-3 {
    grid-template-columns: 1fr;
  }
}
.install-card {
  padding: 28px;
  border: 1px solid #e0e5ee;
  background: #fff;
}
.install-card h3 {
  margin-top: 10px;
  font-size: 19px;
}
.install-card ol {
  margin: 18px 0 0;
  padding-left: 21px;
  color: #657086;
  font-size: 14px;
  line-height: 1.85;
}
.install-card li + li {
  margin-top: 9px;
}
.install-card pre {
  margin: 6px 0;
  padding: 10px 12px;
  overflow-x: auto;
  border-radius: 5px;
  background: #f4f5f7;
}
.install-card code {
  font-size: 13px;
  color: #2c3e50;
  cursor: text;
}
.sub-commands {
  margin-top: 12px;
  padding-left: 21px;
}
.sub-commands > div + div {
  margin-top: 12px;
}
.sub-commands strong {
  display: block;
  margin-bottom: 6px;
  color: #26324a;
  font-size: 13px;
}
.command-panel,
.dependencies-panel {
  margin-top: 18px;
  padding: 30px;
  border: 1px solid #ccd9ef;
  background: #fff;
}
.command-copy h2,
.dependencies-panel h2 {
  font-size: 21px;
}
.dependencies-panel > p {
  margin: 12px 0 0;
  color: #667187;
  font-size: 14px;
  line-height: 1.8;
}
.command-panel > code,
.dependencies-panel > code {
  display: block;
  margin-top: 16px;
  padding: 17px 19px;
  overflow-x: auto;
  background: #182033;
  color: #f2f6ff;
  font-size: 13px;
  line-height: 1.6;
  cursor: text;
}
.command-panel > code {
  white-space: nowrap;
  font-size: 14px;
}
.dependencies-panel > code {
  white-space: pre-wrap;
  word-break: break-all;
}
.dependencies-panel .note {
  margin-top: 18px;
  padding: 14px 18px;
  border-radius: 6px;
  background: #f3f7ff;
  color: #667187;
  font-size: 14px;
  line-height: 1.7;
}
.dependencies-panel .note strong {
  color: #26324a;
}
.dependencies-panel .note code {
  padding: 2px 6px;
  border-radius: 3px;
  background: #e6eef9;
  color: #2c3e50;
  font-size: 13px;
  cursor: text;
}
.tip-note,
.fallback-note {
  margin-top: 18px;
  padding: 20px 24px;
  border: 1px solid #e5e7ec;
  background: #f4f5f7;
}
.tip-note {
  border-left: 4px solid var(--blue);
  background: #f3f7ff;
}
.tip-note strong,
.fallback-note strong {
  color: #35405a;
  font-size: 14px;
}
.tip-note p,
.fallback-note p {
  margin: 7px 0 0;
  color: #667187;
  font-size: 14px;
  line-height: 1.8;
}
.fallback-note .divider {
  margin: 16px 0;
  border: none;
  border-top: 1px solid #d5d8df;
}
.tip-note a,
.fallback-note a {
  color: var(--blue);
  text-decoration: underline;
}
.tip-note a:hover,
.fallback-note a:hover {
  color: #1e5cd6;
}

@media (max-width: 820px) {
  .install-page {
    padding-top: 124px;
  }
}

@media (max-width: 520px) {
  .install-page {
    padding-top: 112px;
    padding-bottom: 72px;
  }
  .intro {
    font-size: 16px;
  }
  .platform-tabs {
    margin-top: 10px;
  }
  .tabs {
    gap: 6px;
  }
  .tab-button {
    padding: 12px 16px;
    font-size: 14px;
  }
  .tab-icon {
    width: 18px;
    height: 18px;
  }
  .install-content {
    margin-top: 32px;
  }
  .install-card,
  .command-panel,
  .dependencies-panel,
  .tip-note,
  .fallback-note {
    padding: 20px;
  }
}
</style>
