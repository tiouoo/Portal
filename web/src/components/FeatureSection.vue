<script setup>
import { features } from "../data/features.js";
</script>

<template>
  <section id="features" class="features section container">
    <div class="section-heading">
      <span class="section-kicker">更好的游戏入口</span>
      <h2>把复杂留在幕后</h2>
      <p>每一项功能都围绕同一件事：让你更快抵达想去的世界。</p>
    </div>
    <div class="feature-grid">
      <article
        v-for="feature in features"
        :key="feature.index"
        class="feature-card"
        :class="feature.tone"
      >
        <div class="feature-index">{{ feature.index }}</div>
        <div class="feature-graphic" aria-hidden="true">
          <template v-if="feature.index === '01'">
            <div class="mini-stack"><i></i><i></i><i></i></div>
            <div class="mini-window"><span></span><b></b><b></b><b></b></div>
          </template>
          <template v-else-if="feature.index === '02'">
            <div class="resource-cube cube-a">
              <svg
                style="margin: 12px"
                xmlns="http://www.w3.org/2000/svg"
                viewBox="0 0 514 514"
              >
                <path
                  d="M504.16 323.56c11.39-42.09 12.16-87.65.04-132.8C467.57 54.23 327.04-26.8 190.33 9.78 84.81 38.02 12.39 128.07 1.69 230.47h43.3c10.3-83.14 69.75-155.74 155.76-178.76 106.3-28.45 215.38 28.96 253.42 129.67l-42.14 11.27c-19.39-46.85-58.46-81.2-104.73-95.83l-7.74 43.84c36.53 13.47 66.16 43.84 77 84.25 15.8 58.89-13.62 119.23-67 144.26l11.53 42.99c70.16-28.95 112.31-101.86 102.34-177.02l41.98-11.23a210.2 210.2 0 0 1-3.86 84.16z"
                  style="fill: #1bd96a; fill-rule: evenodd"
                />
                <path
                  d="M322.99 504.22C186.27 540.8 45.75 459.77 9.11 323.24A257.6 257.6 0 0 1 1 275.46h43.27c1.09 11.91 3.2 23.89 6.41 35.83 3.36 12.51 7.77 24.46 13.11 35.78l38.59-23.15a169 169 0 0 1-8.17-23.45c-24.04-89.6 29.2-181.7 118.92-205.71 17-4.55 34.1-6.32 50.8-5.61L256.19 133c-10.46.05-21.08 1.42-31.66 4.25-66.22 17.73-105.52 85.7-87.78 151.84 1.1 4.07 2.38 8.04 3.84 11.9l49.35-29.61-14.87-39.43 46.6-47.87 58.9-12.69 17.05 20.99-27.15 27.5-23.68 7.45-16.92 17.39 8.29 23.07s16.79 17.84 16.82 17.85l23.72-6.31 16.88-18.54 36.86-11.67 10.98 24.7-38.03 46.63-63.73 20.18-28.58-31.82-49.82 29.89c25.54 29.08 63.94 45.23 103.75 41.86l11.53 42.99c-59.41 7.86-117.44-16.73-153.49-61.91l-38.41 23.04c50.61 66.49 138.2 99.43 223.97 76.48 61.74-16.52 109.79-58.6 135.81-111.78l42.64 15.5c-30.89 66.28-89.84 118.94-166.07 139.34"
                  style="fill: #1bd96a"
                />
              </svg>
            </div>
            <div class="resource-cube cube-b">
              <svg
                style="margin: 12px"
                xmlns="http://www.w3.org/2000/svg"
                viewBox="0 0 260 256"
              >
                <path
                  fill="#121417"
                  d="M196.422 98.77S247.874 90.623 256 66.858h-78.819V48H4l21.334 24.862v25.473s53.83-2.811 74.653 13.047c28.502 26.532-32.058 62.397-32.058 62.397l-10.384 34.512c16.239-15.529 47.188-35.618 103.933-34.65-21.594 6.854-43.307 17.56-60.211 34.65h114.71l-10.802-34.512s-83.139-49.235-8.753-75.005v-.004Z"
                />
              </svg>
            </div>
            <div class="resource-line"></div>
          </template>
          <template v-else>
            <div class="log-panel"><i></i><i></i><i></i><i></i></div>
            <div class="pulse-ring"><span>✓</span></div>
          </template>
        </div>
        <h3>{{ feature.title }}</h3>
        <p>{{ feature.text }}</p>
      </article>
    </div>
    <div class="capability-strip">
      <span>还支持</span>
      <div>
        <b>多标签工作区</b><i></i><b>基岩版启动/版本隔离</b><i></i
        ><b>实时日志</b><i></i><b>存档信息</b><i></i><b>Litematica 分析</b>
      </div>
    </div>
  </section>
</template>

<style scoped src="../styles/feature-section.css"></style>
