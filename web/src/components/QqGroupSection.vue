<script setup>
import qrCodeUrl from "../assets/qr.png";
</script>

<template>
  <section id="qq-group" class="qq-group section">
    <div class="container">
      <div class="qq-card">
        <div class="qq-copy">
          <span class="section-kicker">官方社区</span>
          <h2>加入官方 QQ 群</h2>
          <p>
            和其他 Portal 玩家交流使用经验、反馈问题，并第一时间获取项目动态。
          </p>
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

<style scoped>
.section {
  padding-top: 60px;
  padding-bottom: 60px;
}

.qq-card {
  position: relative;
  display: grid;
  grid-template-columns: 1fr auto;
  gap: 48px;
  align-items: center;
  overflow: hidden;
  padding: 48px 54px;
  border-radius: 20px;
  border: 1px solid #e2e6ee;
  background: #fff;
  color: var(--ink);
  box-shadow: 0 16px 35px rgba(60, 75, 105, 0.08);
}

.qq-card::before,
.qq-card::after {
  position: absolute;
  width: 240px;
  height: 240px;
  border: 1px solid rgba(133, 94, 234, 0.12);
  border-radius: 50%;
  content: "";
}

.qq-card::before {
  top: -150px;
  right: 175px;
}

.qq-card::after {
  right: -95px;
  bottom: -150px;
}

.qq-copy,
.qq-code {
  position: relative;
  z-index: 1;
}

.section-kicker {
  color: var(--blue);
  font-size: 13px;
  font-weight: 700;
  letter-spacing: 0.12em;
  text-transform: uppercase;
}

.qq-copy h2 {
  margin: 10px 0 0;
  font-size: clamp(36px, 4vw, 48px);
  letter-spacing: -0.045em;
  line-height: 1.15;
}

.qq-copy p {
  max-width: 530px;
  margin: 16px 0 0;
  color: var(--muted);
  font-size: 14px;
  line-height: 1.85;
}

.qq-number {
  display: inline-flex;
  gap: 12px;
  align-items: baseline;
  margin-top: 27px;
  padding: 11px 15px;
  border: 1px solid #e1e5ed;
  border-radius: 12px;
  background: #f6f8fb;
}

.qq-number span {
  color: #778195;
  font-size: 12px;
  font-weight: 700;
}

.qq-number b {
  color: var(--ink);
  font-size: 18px;
  letter-spacing: 0.04em;
}

.qq-code {
  display: grid;
  gap: 10px;
  justify-items: center;
  padding: 12px;
  border: 1px solid #e5e8ef;
  border-radius: 16px;
  background: #fbfcfe;
  color: #6e778a;
  font-size: 11px;
  font-weight: 700;
}

.qq-code img {
  display: block;
  width: 148px;
  height: 148px;
  border-radius: 8px;
  object-fit: contain;
}

@media (max-width: 820px) {
  .section {
    padding-top: 80px;
    padding-bottom: 80px;
  }

  .qq-card {
    gap: 34px;
    padding: 38px;
  }
}

@media (max-width: 620px) {
  .qq-card {
    grid-template-columns: 1fr;
    gap: 28px;
    padding: 32px 26px;
  }

  .qq-code {
    justify-self: start;
  }

  .qq-card::before {
    right: -80px;
  }
}
</style>
