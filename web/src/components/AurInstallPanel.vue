<script setup lang="ts">
import { onBeforeUnmount, ref } from 'vue';

const packages = [
  { id: 'cnb', label: 'CNB 正式版', note: '国内推荐', command: 'yay -S portal-mc-cnb-bin' },
  { id: 'release', label: 'GitHub 正式版', note: '稳定', command: 'yay -S portal-mc-bin' },
  { id: 'commit', label: 'Commit', note: '随提交更新', command: 'yay -S portal-mc-commit-bin' },
  { id: 'nightly', label: 'Nightly', note: '每日构建', command: 'yay -S portal-mc-nightly-bin' },
];

const copied = ref('');
let resetTimer: ReturnType<typeof setTimeout> | undefined;

async function copyCommand(id: string, command: string) {
  await navigator.clipboard.writeText(command);
  copied.value = id;
  clearTimeout(resetTimer);
  resetTimer = setTimeout(() => {
    copied.value = '';
  }, 1800);
}

onBeforeUnmount(() => clearTimeout(resetTimer));
</script>

<template>
  <section class="aur-panel" aria-labelledby="aur-title">
    <div class="aur-heading">
      <div class="title">
        <h3 id="aur-title">Arch Linux·Aur</h3>
        <p>点击对应版本复制安装命令</p>
      </div>
    </div>
    <div class="aur-options">
      <button
        v-for="item in packages"
        :key="item.id"
        type="button"
        :class="{ copied: copied === item.id }"
        :aria-label="`复制 ${item.label} 安装命令`"
        @click="copyCommand(item.id, item.command)">
        <span class="aur-label">
          <b>{{ item.label }}</b>
          <small>{{ item.note }}</small>
        </span>
        <span :key="copied === item.id ? 'copied' : 'command'" class="aur-command">
          {{ copied === item.id ? '复制成功' : item.command }}
        </span>
      </button>
    </div>
  </section>
</template>

<style scoped>
.aur-panel {
  margin-top: 14px;
  padding: 13px 15px;
  display: grid;
  grid-template-columns: auto 1fr;
  align-items: center;
  gap: 15px;
  border: 1px solid #dbe3ef;
  border-radius: 14px;
  background: #ffffff;
}
.aur-heading {
  min-width: 175px;
  display: flex;
  align-items: center;
  gap: 10px;
}
.aur-mark {
  width: 34px;
  height: 34px;
  display: grid;
  flex: 0 0 auto;
  place-items: center;
  border-radius: 10px;
  background: #182033;
  color: #fff;
  font-size: 15px;
  font-weight: 900;
}
.aur-heading h3 {
  margin: 0;
  color: #26324a;
  font-size: 13px;
}
.aur-heading p {
  margin: 3px 0 0;
  color: #7b8598;
  font-size: 10px;
}
.aur-options {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 7px;
}
.aur-options button {
  min-width: 0;
  padding: 8px 10px;
  overflow: hidden;
  border: 1px solid #dfe5ee;
  border-radius: 10px;
  background: rgba(255, 255, 255, 0.9);
  color: #35405a;
  text-align: left;
  cursor: pointer;
}
.aur-options button:hover {
  border-color: rgba(42, 112, 245, 0.45);
}
.aur-options button.copied {
  border-color: rgba(42, 112, 245, 0.5);
  background: #edf4ff;
}
.aur-label {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 6px;
}
.aur-label b {
  overflow: hidden;
  font-size: 10px;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.aur-label small {
  flex: 0 0 auto;
  color: var(--blue);
  font-size: 8px;
  font-weight: 800;
}
.aur-command {
  display: block;
  height: 14px;
  margin-top: 5px;
  overflow: hidden;
  color: #748096;
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
  font-size: 9px;
  line-height: 14px;
  text-overflow: ellipsis;
  white-space: nowrap;
  animation: command-change 0.22s ease-out;
}
.copied .aur-command {
  color: var(--blue);
  font-family: inherit;
  font-weight: 800;
}
@keyframes command-change {
  from {
    opacity: 0;
    transform: translateY(3px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}
@media (max-width: 1000px) {
  .aur-panel {
    grid-template-columns: 1fr;
  }
  .aur-options {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}
@media (min-width: 1001px) {
  .title {
    margin-left: 5px;
  }
}
@media (max-width: 520px) {
  .aur-options {
    grid-template-columns: 1fr;
  }
}
@media (prefers-reduced-motion: reduce) {
  .aur-command {
    animation: none;
  }
}
</style>
