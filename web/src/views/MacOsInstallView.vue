<template>
  <main class="macos-page">
    <section class="macos-hero container">
      <h1>macOS 安装说明</h1>
      <p class="intro">
        如果 macOS 提示“Portal
        已损坏，无法打开”或文件损坏，请按照以下说明安装并解除系统限制。
      </p>
    </section>

    <section class="install-content container" aria-labelledby="install-title">
      <h2 id="install-title">安装前确认</h2>
      <div style="border-radius: 16px" class="architecture-note">
        <div>
          <h3>选择正确的安装包</h3>
          <p>
            <b>Apple 芯片（M1/M2/M3/M4 等）</b>请下载带
            <code>arm64</code> 的文件。<b>Intel 芯片</b>请下载带
            <code>x64</code> 的文件。
          </p>
        </div>
      </div>

      <div class="install-grid">
        <article style="border-radius: 16px" class="install-card">
          <p class="card-label">dmg 安装包</p>
          <h3>打开并拖入应用程序</h3>
          <ol>
            <li>双击打开下载的 dmg 镜像文件。</li>
            <li>将 Portal 图标拖动到右侧的“应用程序”文件夹。</li>
            <li>
              打开“终端”：应用程序 - 实用工具 - 终端，或按 Command +
              空格搜索“终端”。
            </li>
          </ol>
        </article>
        <article style="border-radius: 16px" class="install-card">
          <p class="card-label">app.zip 压缩包</p>
          <h3>解压后移入应用程序</h3>
          <ol>
            <li>双击解压 <code>app.zip</code> 文件。</li>
            <li>将解压出来的 Portal.app 移动到“应用程序”文件夹。</li>
            <li>
              打开“终端”：应用程序 - 实用工具 - 终端，或按 Command +
              空格搜索“终端”。
            </li>
          </ol>
        </article>
      </div>

      <section
        style="border-radius: 16px"
        class="command-panel"
        aria-labelledby="command-title"
      >
        <div class="command-copy">
          <h2 id="command-title">在终端执行命令</h2>
          <p>
            复制下面的命令到终端并按回车。系统会要求输入 Mac
            开机密码；输入时屏幕不会显示字符或星号，输入完成后直接按回车即可。
          </p>
        </div>
        <code style="user-select: text;border-radius: 16px;"
          >sudo xattr -rd com.apple.quarantine /Applications/Portal.app</code
        >
      </section>

      <aside style="border-radius: 16px" class="fallback-note">
        <strong>仍然无法打开？</strong>
        <p>
          打开“系统设置 -
          隐私与安全性”，滚动到“安全性”部分，点击“仍要打开”，然后重新打开
          Portal。
        </p>
      </aside>
    </section>
  </main>
</template>

<style scoped>
.macos-page {
  min-height: calc(100vh - 166px);
  padding: 148px 0 108px;
  background: linear-gradient(180deg, #f4f7fd 0, #f8f9fc 420px);
}
.macos-hero {
  padding-left: 12px;
}
.card-label {
  margin: 0;
  color: var(--blue);
  font-size: 12px;
  font-weight: 800;
  letter-spacing: 0.1em;
  /* text-transform: uppercase; */
}
.macos-hero h1 {
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
.install-content {
  margin-top: 62px;
}
.install-content > h2 {
  margin: 0 0 18px;
  color: #202b40;
  font-size: 22px;
}
.architecture-note {
  padding: 22px 24px;
  border: 1px solid #d7e1f3;
  /* border-left: 4px solid var(--blue); */
  border-radius: 6px;
  background: #f3f7ff;
}
.architecture-note h3,
.install-card h3,
.command-copy h2 {
  margin: 0;
  color: #26324a;
}
.architecture-note h3 {
  font-size: 15px;
}
.architecture-note p,
.command-copy p,
.fallback-note p {
  margin: 7px 0 0;
  color: #667187;
  font-size: 14px;
  line-height: 1.8;
}
.architecture-note b {
  color: #33405a;
}
.macos-page code {
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
}
.install-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 18px;
  margin-top: 18px;
}
.install-card {
  padding: 28px;
  border: 1px solid #e0e5ee;
  border-radius: 7px;
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
.command-panel {
  margin-top: 18px;
  padding: 30px;
  border: 1px solid #ccd9ef;
  border-radius: 7px;
  background: #fff;
}
.command-copy h2 {
  margin-top: 9px;
  font-size: 21px;
}
.command-panel > code {
  display: block;
  margin-top: 22px;
  padding: 17px 19px;
  overflow-x: auto;
  border-radius: 5px;
  background: #182033;
  color: #f2f6ff;
  font-size: 14px;
  line-height: 1.5;
  white-space: nowrap;
}
.fallback-note {
  margin-top: 18px;
  padding: 20px 24px;
  border: 1px solid #e5e7ec;
  border-radius: 6px;
  background: #f4f5f7;
}
.fallback-note strong {
  color: #35405a;
  font-size: 14px;
}

@media (max-width: 820px) {
  .macos-page {
    padding-top: 124px;
  }
  .install-grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 520px) {
  .macos-page {
    padding-top: 112px;
    padding-bottom: 72px;
  }
  .intro {
    font-size: 16px;
  }
  .install-content {
    margin-top: 44px;
  }
  .architecture-note,
  .install-card,
  .command-panel,
  .fallback-note {
    padding: 20px;
  }
}
</style>
