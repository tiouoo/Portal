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
        <b>多标签工作区</b><i></i><b>基岩版启动（Windows / Linux x64）</b><i></i
        ><b>实时日志</b><i></i><b>存档信息</b><i></i><b>Litematica 分析</b>
      </div>
    </div>
  </section>
</template>

<style scoped>
.section {
  padding-top: 112px;
  padding-bottom: 112px;
}
.section-heading {
  text-align: center;
  max-width: 660px;
  margin: 0 auto 52px;
}
.section-kicker {
  color: var(--blue);
  font-size: 13px;
  letter-spacing: 0.12em;
  font-weight: 700;
  text-transform: uppercase;
}
.section-heading h2 {
  margin: 10px 0 0;
  font-size: clamp(36px, 4vw, 50px);
  letter-spacing: -0.045em;
  line-height: 1.15;
}
.section-heading p {
  color: var(--muted);
  font-size: 16px;
  margin: 17px 0 0;
}
.feature-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 18px;
}
.feature-card {
  position: relative;
  min-height: 440px;
  overflow: hidden;
  padding: 30px;
  border: 1px solid #e2e6ee;
  border-radius: 18px;
  background: #fff;
}
.feature-index {
  position: absolute;
  top: 25px;
  right: 27px;
  color: #b5bbc6;
  font-size: 12px;
  letter-spacing: 0.1em;
  z-index: 999;
}
.feature-graphic {
  position: relative;
  height: 230px;
  margin-bottom: 25px;
  border-radius: 13px;
  overflow: hidden;
  background: #f1f5fd;
}
.feature-card.purple .feature-graphic {
  background: #f5f1fd;
}
.feature-card.mint .feature-graphic {
  background: #eff8f6;
}
.feature-card h3 {
  margin: 0 0 12px;
  font-size: 22px;
  letter-spacing: -0.025em;
}
.feature-card > p {
  margin: 0;
  color: var(--muted);
  font-size: 14px;
  line-height: 1.8;
}
.mini-stack {
  position: absolute;
  left: 45px;
  top: 52px;
}
.mini-stack i {
  position: absolute;
  display: block;
  width: 155px;
  height: 100px;
  border-radius: 10px;
  border: 1px solid #bcd0fa;
  background: white;
  box-shadow: 0 12px 30px rgba(60, 90, 150, 0.08);
}
.mini-stack i:nth-child(1) {
  transform: rotate(-8deg) translate(-12px, 7px);
  opacity: 0.55;
}
.mini-stack i:nth-child(2) {
  transform: rotate(5deg) translate(20px, -3px);
  opacity: 0.75;
}
.mini-window {
  position: absolute;
  left: 70px;
  top: 68px;
  width: 155px;
  height: 106px;
  border: 1px solid #cbd8f3;
  border-radius: 10px;
  background: white;
  box-shadow: 0 17px 35px rgba(51, 86, 150, 0.15);
  padding: 17px;
}
.mini-window span {
  display: block;
  width: 28px;
  height: 28px;
  border-radius: 7px;
  background: linear-gradient(135deg, var(--blue), #83a9fa);
  margin-bottom: 12px;
}
.mini-window b {
  display: inline-block;
  width: 31px;
  height: 5px;
  border-radius: 4px;
  background: #dce5f6;
  margin-right: 5px;
}
.mini-window b:nth-of-type(1) {
  width: 60px;
  background: #a9c2f7;
}
.resource-cube {
  position: absolute;
  display: grid;
  place-items: center;
  width: 84px;
  height: 84px;
  border-radius: 18px;
  color: white;
  font-weight: 800;
  font-size: 25px;
  box-shadow: 0 18px 30px rgba(95, 57, 150, 0.16);
}
.cube-a {
  left: 55px;
  top: 50px;
  /* background: linear-gradient(145deg, #87aaf8, #3974ee); */
  transform: rotate(-8deg);
  background-color: white;
}
.cube-b {
  right: 55px;
  bottom: 42px;
  /* background: linear-gradient(145deg, #ae8de8, #7752ca); */
  transform: rotate(8deg);
  background-color: white;
}
.resource-line {
  position: absolute;
  width: 115px;
  display: none;
  height: 2px;
  left: 110px;
  top: 121px;
  background: linear-gradient(90deg, #6e9bf5, #9871dd);
  transform: rotate(24deg);
}
.resource-line::after {
  content: "+";
  position: absolute;
  left: 52px;
  top: -12px;
  display: grid;
  place-items: center;
  width: 25px;
  height: 25px;
  border-radius: 50%;
  background: white;
  color: var(--purple);
  font-weight: 700;
  box-shadow: 0 5px 15px rgba(60, 50, 100, 0.15);
}
.log-panel {
  position: absolute;
  left: 40px;
  right: 40px;
  top: 42px;
  height: 145px;
  padding: 35px 24px 20px;
  border-radius: 12px;
  background: #fff;
  box-shadow: 0 15px 35px rgba(40, 100, 85, 0.11);
}
.log-panel::before {
  content: "";
  position: absolute;
  top: 17px;
  left: 20px;
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: #79d4ba;
  box-shadow:
    13px 0 #dbe6e3,
    26px 0 #dbe6e3;
}
.log-panel i {
  display: block;
  height: 6px;
  border-radius: 5px;
  background: #dfece9;
  margin-bottom: 12px;
}
.log-panel i:nth-child(1) {
  width: 75%;
}
.log-panel i:nth-child(2) {
  width: 90%;
  background: #b9ded5;
}
.log-panel i:nth-child(3) {
  width: 62%;
}
.log-panel i:nth-child(4) {
  width: 82%;
}
.pulse-ring {
  position: absolute;
  right: 33px;
  bottom: 25px;
  display: grid;
  place-items: center;
  width: 52px;
  height: 52px;
  border-radius: 50%;
  border: 5px solid #d6f1e9;
  background: white;
  box-shadow: 0 7px 18px rgba(40, 120, 90, 0.15);
}
.pulse-ring span {
  display: grid;
  place-items: center;
  width: 32px;
  height: 32px;
  border-radius: 50%;
  background: #52bea0;
  color: white;
  font-size: 16px;
}
.capability-strip {
  margin-top: 18px;
  min-height: 70px;
  padding: 15px 24px;
  display: flex;
  align-items: center;
  gap: 25px;
  border: 1px solid #e3e7ee;
  border-radius: 14px;
  background: rgba(255, 255, 255, 0.65);
}
.capability-strip > span {
  color: #949baa;
  font-size: 12px;
  flex: none;
}
.capability-strip > div {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  font-size: 12px;
  font-weight: 500;
}
.capability-strip i {
  width: 3px;
  height: 3px;
  border-radius: 50%;
  background: #c7ccd6;
}

@media (max-width: 820px) {
  .section {
    padding-top: 80px;
    padding-bottom: 80px;
  }
  .feature-grid {
    grid-template-columns: 1fr;
  }
  .feature-card {
    min-height: 410px;
  }
  .feature-graphic {
    height: 220px;
  }
  .capability-strip {
    align-items: center;
  }
  .capability-strip > div {
    flex-wrap: wrap;
    justify-content: flex-start;
    gap: 16px;
  }
  .capability-strip i {
    display: none;
  }
}

@media (max-width: 520px) {
  .section-heading {
    text-align: left;
  }
  .section-heading h2 {
    font-size: 36px;
  }
  .feature-card {
    padding: 22px;
    min-height: 390px;
  }
  .feature-graphic {
    height: 215px;
  }
  .mini-window {
    left: 57px;
  }
  .resource-cube.cube-a {
    left: 35px;
  }
  .resource-cube.cube-b {
    right: 35px;
  }
  .resource-line {
    left: 85px;
  }
  .log-panel {
    left: 22px;
    right: 22px;
  }
  .capability-strip {
    display: block;
  }
  .capability-strip > span {
    display: block;
    margin-bottom: 15px;
  }
}
</style>
