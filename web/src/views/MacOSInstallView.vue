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
      <div class="architecture-note">
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
        <article class="install-card">
          <p class="card-label">DMG 安装包</p>
          <h3>打开并拖入应用程序</h3>
          <ol>
            <li>双击打开下载的 DMG 镜像文件。</li>
            <li>将 Portal 图标拖动到右侧的“应用程序”文件夹。</li>
            <li>
              打开“终端”：应用程序 - 实用工具 - 终端，或按 Command +
              空格搜索“终端”。
            </li>
          </ol>
        </article>
        <article class="install-card">
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

      <section class="command-panel" aria-labelledby="command-title">
        <div class="command-copy">
          <h2 id="command-title">在终端执行命令</h2>
          <p>
            复制下面的命令到终端并按回车。系统会要求输入 Mac
            开机密码；输入时屏幕不会显示字符或星号，输入完成后直接按回车即可。
          </p>
        </div>
        <code style="user-select: text"
          >sudo xattr -rd com.apple.quarantine /Applications/Portal.app</code
        >
      </section>

      <aside class="fallback-note">
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

<style scoped src="../styles/macos-install.css"></style>
