<script setup>
import { features } from '../data/features.js'
</script>

<template>
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
</template>

<style scoped src="../styles/feature-section.css"></style>
