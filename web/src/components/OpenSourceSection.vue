<script setup>
import logoUrl from "../assets/portal-logo.svg";
import qrCodeUrl from "../assets/qr.png";
</script>

<template>
  <section class="open-source container section">
    <div class="oss-mark">
      <img :src="logoUrl" alt="" width="42" height="42" />
    </div>
    <div id="open-source" class="oss-copy">
      <span class="section-kicker">自由且透明</span>
      <h2>由社区共同塑造</h2>
      <p>
        Portal 以 AGPL-3.0-or-later
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
    <div class="cards-stack">
      <div class="qq-card-small">
        <div class="qq-card-header">
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 448 512" fill="currentColor">
            <path d="M433.754 420.445c-11.526 1.393-44.86-52.741-44.86-52.741c0 31.345-16.136 72.247-51.051 101.786c16.842 5.192 54.843 19.167 45.803 34.421c-7.316 12.343-125.51 7.881-159.632 4.037c-34.122 3.844-152.316 8.306-159.632-4.037c-9.045-15.25 28.918-29.214 45.783-34.415c-34.92-29.539-51.059-70.445-51.059-101.792c0 0-33.334 54.134-44.859 52.741c-5.37-.65-12.424-29.644 9.347-99.704c10.261-33.024 21.995-60.478 40.144-105.779C60.683 98.063 108.982.006 224 0c113.737.006 163.156 96.133 160.264 214.963c18.118 45.223 29.912 72.85 40.144 105.778c21.768 70.06 14.716 99.053 9.346 99.704"/>
          </svg>
          <span>官方 QQ 群</span>
        </div>
        <div class="qq-card-number">475032328</div>
        <div class="qq-card-footer">
          <span>扫码或搜索群号加入</span>
          <img :src="logoUrl" alt="" width="20" height="20" />
        </div>
        <div class="qq-qr">
          <img :src="qrCodeUrl" alt="QQ群二维码" />
        </div>
      </div>
      <div class="repo-card">
        <div class="repo-title">
          <img :src="logoUrl" alt="" width="36" height="36" /><span
            ><small>github.com/tiouoo</small><b>Portal</b></span
          >
        </div>
        <div class="code-lines"><i></i><i></i><i></i><i></i><i></i></div>
        <div class="repo-footer">
          <span>github.com/tiouoo/Portal</span>
          <span><i></i> C#</span>
          <b>AGPL-3.0</b>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.section {
  padding-top: 100px;
  padding-bottom: 100px;
}
#open-source {
  scroll-margin-top: 80px;
}
.open-source {
  display: grid;
  grid-template-columns: 90px 1fr 400px;
  gap: 48px;
  align-items: start;
}
.section-kicker {
  color: var(--blue);
  font-size: 13px;
  letter-spacing: 0.12em;
  font-weight: 700;
  text-transform: uppercase;
}
.oss-mark {
  width: 74px;
  height: 74px;
  display: grid;
  place-items: center;
  border-radius: 18px;
  background: white;
  border: 1px solid #e4e7ed;
  box-shadow: 0 12px 30px rgba(55, 65, 90, 0.08);
}
.oss-mark img {
  width: 42px;
  height: 42px;
  position: relative;
  top: -2px;
  left: -2px;
}
.oss-copy h2 {
  margin: 10px 0 0;
  font-size: 42px;
  letter-spacing: -0.045em;
  line-height: 1.15;
}
.oss-copy > p {
  max-width: 560px;
  color: var(--muted);
  line-height: 1.85;
  font-size: 14px;
}
.oss-actions {
  display: flex;
  align-items: center;
  gap: 25px;
  margin-top: 25px;
}
.button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 12px;
  padding: 0 22px;
  border-radius: 10px;
  font-weight: 700;
  transition:
    background 0.2s,
    box-shadow 0.2s;
}
.button:hover {
  background: #2d374c;
  box-shadow: 0 8px 22px rgba(29, 38, 57, 0.18);
}
.button.dark {
  height: 46px;
  background: #20283a;
  color: white;
  font-size: 13px;
}
.button.dark svg {
  width: 19px;
  height: 19px;
  fill: currentColor;
  stroke: none;
}
.text-link {
  font-size: 13px;
  font-weight: 700;
}
.text-link span {
  color: var(--blue);
}
.cards-stack {
  position: relative;
  width: 400px;
  height: 270px;
}
.qq-card-small {
  position: absolute;
  top: 0;
  right: -15px;
  width: calc(100% - 20px);
  z-index: 2;
  padding: 20px 22px;
  border-radius: 16px;
  background: white;
  border: 1px solid #e2e6ee;
  color: var(--ink);
  box-shadow: 0 20px 45px rgba(60, 75, 105, 0.15);
  transform: rotate(-1.5deg) translateY(15px);
  pointer-events: none;
}
.qq-card-header {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 12px;
  font-weight: 700;
  color: #5f6879;
}
.qq-card-header svg {
  width: 18px;
  height: 18px;
  color: #20283a;
}
.qq-card-number {
  margin: 18px 0 12px;
  font-size: 28px;
  font-weight: 800;
  letter-spacing: 0.03em;
  color: #20283a;
}
.qq-card-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding-top: 12px;
  border-top: 1px solid #e8ecf3;
  font-size: 10px;
  color: #8f96a4;
}
.qq-card-footer img {
  opacity: 0.4;
}
.qq-qr {
    position: absolute;
    top: 8px;
    right: 8px;
    width: 84px;
    height: 84px;
    padding: 2px;
    border-radius: 10px;
    background: white;
    border: 1px solid #e8ecf3;
    margin: 2px;
}
.qq-qr img {
  width: 100%;
  height: 100%;
  border-radius: 6px;
  display: block;
}
.repo-card {
  position: absolute;
  bottom: 0;
  left: -15px;
  width: calc(100% - 20px);
  z-index: 1;
  padding: 25px;
  padding-top: 28px;
  border-radius: 16px;
  background: #20283a;
  color: white;
  box-shadow: 0 25px 50px rgba(29, 38, 57, 0.2);
  transform: rotate(1.5deg);
}
.repo-title {
  display: flex;
  align-items: center;
  gap: 12px;
}
.repo-title img {
  width: 36px;
  height: 36px;
  filter: drop-shadow(0 3px 4px rgba(0, 0, 0, 0.2));
}
.repo-title small,
.repo-title b {
  display: block;
}
.repo-title small {
  font-size: 9px;
  color: #8f99ae;
}
.repo-title b {
  font-size: 15px;
  margin-top: 2px;
}
.code-lines {
  margin: 22px 0;
}
.code-lines i {
  display: block;
  height: 5px;
  margin: 10px 0;
  border-radius: 4px;
  background: #414b60;
}
.code-lines i:nth-child(1) {
  width: 68%;
  background: #58647b;
}
.code-lines i:nth-child(2) {
  width: 88%;
}
.code-lines i:nth-child(3) {
  width: 54%;
}
.code-lines i:nth-child(4) {
  width: 78%;
  background: #4b5770;
}
.code-lines i:nth-child(5) {
  width: 40%;
}
.repo-footer {
  display: flex;
  justify-content: space-between;
  align-items: center;
  color: #a4adbd;
  font-size: 10px;
  gap: 12px;
}
.repo-footer > span:first-child {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.repo-footer > span:nth-child(2) {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-shrink: 0;
}
.repo-footer > span:nth-child(2) {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-shrink: 0;
}
.repo-footer > span:nth-child(2) i {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: #9b6be8;
}

@media (max-width: 1100px) {
  .open-source {
    grid-template-columns: 70px 1fr;
  }
  .cards-stack {
    grid-column: 2;
    width: 100%;
    max-width: 450px;
  }
}

@media (max-width: 820px) {
  .section {
    padding-top: 80px;
    padding-bottom: 80px;
  }
  .open-source {
    grid-template-columns: 1fr;
    gap: 25px;
  }
  .oss-mark {
    align-self: auto;
  }
  .cards-stack {
    grid-column: auto;
  }
}

@media (max-width: 520px) {
  .oss-copy h2 {
    font-size: 36px;
  }
  .oss-actions {
    align-items: stretch;
    flex-direction: column;
    gap: 18px;
  }
  .text-link {
    text-align: center;
  }
}
</style>
