<script setup>
import logoUrl from "../assets/portal-logo.svg";
</script>

<template>
  <section class="open-source container section">
    <div class="oss-mark"><img :src="logoUrl" alt="" /></div>
    <div class="oss-copy">
      <span class="section-kicker">自由且透明</span>
      <h2>由社区共同塑造</h2>
      <p>
        Portal 以 GPL-3.0-or-later
        许可开源。你可以检查每一行代码、提交问题，或让自己的想法成为下一次更新的一部分。
      </p>
      <div class="oss-actions">
        <a
          style="border-radius: 14px"
          class="button dark"
          href="https://github.com/tiouoo/Portal"
          target="_blank"
          rel="noreferrer"
        >
          <svg viewBox="0 0 24 24">
            <path
              d="M12 2a10 10 0 0 0-3.2 19.5c.5.1.7-.2.7-.5v-2c-2.8.6-3.4-1.2-3.4-1.2-.5-1.2-1.1-1.5-1.1-1.5-.9-.6.1-.6.1-.6 1 0 1.5 1 1.5 1 .9 1.5 2.3 1.1 2.9.8.1-.6.3-1.1.6-1.3-2.2-.3-4.6-1.1-4.6-5A3.9 3.9 0 0 1 5.4 9c-.1-.3-.5-1.3.1-2.7 0 0 .9-.3 2.8 1a9.7 9.7 0 0 1 5.1 0c2-1.3 2.8-1 2.8-1 .6 1.4.2 2.4.1 2.7a3.9 3.9 0 0 1 1.1 2.7c0 3.9-2.4 4.7-4.6 5 .4.3.7.9.7 1.8V21c0 .3.2.6.7.5A10 10 0 0 0 12 2Z"
            />
          </svg>
          GitHub 仓库
        </a>
        <a
          style="display: flex; align-items: center"
          class="text-link"
          href="https://github.com/tiouoo/Portal/issues"
          target="_blank"
          rel="noreferrer"
        >
          <span
            style="display: flex; align-items: center; justify-content: center"
          >
            <svg
              style="width: 20px; margin-right: 6px"
              xmlns="http://www.w3.org/2000/svg"
              viewBox="0 0 640 640"
            >
              <path
                d="M224 160C224 107 267 64 320 64C373 64 416 107 416 160L416 163.6C416 179.3 403.3 192 387.6 192L252.5 192C236.8 192 224.1 179.3 224.1 163.6L224.1 160zM569.6 172.8C580.2 186.9 577.3 207 563.2 217.6L465.4 290.9C470.7 299.8 474.7 309.6 477.2 320L576 320C593.7 320 608 334.3 608 352C608 369.7 593.7 384 576 384L480 384L480 416C480 418.6 479.9 421.3 479.8 423.9L563.2 486.4C577.3 497 580.2 517.1 569.6 531.2C559 545.3 538.9 548.2 524.8 537.6L461.7 490.3C438.5 534.5 395.2 566.5 344 574.2L344 344C344 330.7 333.3 320 320 320C306.7 320 296 330.7 296 344L296 574.2C244.8 566.5 201.5 534.5 178.3 490.3L115.2 537.6C101.1 548.2 81 545.3 70.4 531.2C59.8 517.1 62.7 497 76.8 486.4L160.2 423.9C160.1 421.3 160 418.7 160 416L160 384L64 384C46.3 384 32 369.7 32 352C32 334.3 46.3 320 64 320L162.8 320C165.3 309.6 169.3 299.8 174.6 290.9L76.8 217.6C62.7 207 59.8 186.9 70.4 172.8C81 158.7 101.1 155.8 115.2 166.4L224 248C236.3 242.9 249.8 240 264 240L376 240C390.2 240 403.7 242.8 416 248L524.8 166.4C538.9 155.8 559 158.7 569.6 172.8z"
              /></svg></span
          >反馈问题
        </a>
      </div>
    </div>
    <div class="repo-card">
      <div class="repo-title">
        <img :src="logoUrl" alt="" /><span
          ><small>github.com/tiouoo</small><b>Portal</b></span
        >
      </div>
      <div class="code-lines"><i></i><i></i><i></i><i></i><i></i></div>
      <div class="repo-footer">
        <span><i></i> C#</span><b>GPL-3.0</b>
      </div>
    </div>
  </section>
</template>

<style scoped src="../styles/open-source-section.css"></style>
