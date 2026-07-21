<script setup>
import logoUrl from '../assets/portal-logo.svg'
</script>

<template>
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
</template>

<style scoped src="../styles/open-source-section.css"></style>
