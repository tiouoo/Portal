<script setup>
import qrCodeUrl from '../assets/qr.png'
</script>

<template>
  <section id="qq-group" class="qq-group section">
    <div class="container">
      <div class="qq-card">
        <div class="qq-copy">
          <span class="section-kicker">官方社区</span>
          <h2>加入官方 QQ 群</h2>
          <p>和其他 Portal 玩家交流使用经验、反馈问题，并第一时间获取项目动态。</p>
          <div class="qq-number">
            <span>QQ群号</span>
            <b>475032328</b>
          </div>
        </div>
        <div class="qq-code">
          <img :src="qrCodeUrl" alt="Portal 官方 QQ 群二维码" />
          <span>扫码加入群聊</span>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped src="../styles/qq-group-section.css"></style>
