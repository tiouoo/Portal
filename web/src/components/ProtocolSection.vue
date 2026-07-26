<script setup>
import { computed, ref } from "vue";

const modes = [
  {
    id: "launch",
    label: "启动游戏",
    action: "启动",
    placeholder: "实例 ID 或名称，如 1.20.1-forge",
  },
  {
    id: "vanilla",
    label: "安装原版",
    action: "安装",
    placeholder: "Minecraft 版本，如 1.21.8",
  },
  {
    id: "loader",
    label: "安装加载器",
    action: "安装",
    placeholder: "Minecraft 版本，如 1.21.8",
  },
  {
    id: "modpack",
    label: "安装整合包",
    action: "安装",
    placeholder: "整合包名称、项目 ID 或链接",
  },
];

const modeId = ref("launch");
const input = ref("");
const loaderKind = ref("fabric");
const loaderVersion = ref("");
const packFrom = ref("");
const packVersion = ref("");

const mode = computed(() => modes.find((m) => m.id === modeId.value));

const uri = computed(() => {
  const value = input.value.trim();
  if (!value) return "";
  const enc = encodeURIComponent;
  switch (modeId.value) {
    case "launch":
      return `portal://launch?id=${enc(value)}`;
    case "vanilla":
      return `portal://install/vanilla?version=${enc(value)}`;
    case "loader": {
      const loader = loaderVersion.value.trim()
        ? `${loaderKind.value}@${loaderVersion.value.trim()}`
        : loaderKind.value;
      return `portal://install/loader?version=${enc(value)}&loader=${enc(loader)}`;
    }
    case "modpack": {
      let link = `portal://install/modpack?source=${enc(value)}`;
      if (packFrom.value) link += `&from=${packFrom.value}`;
      if (packVersion.value.trim())
        link += `&version=${enc(packVersion.value.trim())}`;
      return link;
    }
    default:
      return "";
  }
});

const example = computed(() => {
  switch (modeId.value) {
    case "launch":
      return "portal://launch?id=1.20.1-forge";
    case "vanilla":
      return "portal://install/vanilla?version=1.21.8";
    case "loader":
      return "portal://install/loader?version=1.21.8&loader=fabric";
    default:
      return "portal://install/modpack?source=Fabulously%20Optimized";
  }
});

function invoke() {
  if (uri.value) window.location.href = uri.value;
}
</script>

<template>
  <section id="protocol" class="protocol-section section">
    <div class="container">
      <div class="section-heading">
      <span class="section-kicker">浏览器直达</span>
      <h2>在浏览器中唤起 Portal</h2>
      <p>
        Portal 支持通过命令行参数或 portal://
        链接调用安装与启动功能，在下方选择功能并填写参数，即可从浏览器直接调起启动器
      </p>
    </div>
    <div class="protocol-card">
      <div class="protocol-tabs" role="tablist">
        <button
          v-for="m in modes"
          :key="m.id"
          type="button"
          role="tab"
          :aria-selected="modeId === m.id"
          :class="{ active: modeId === m.id }"
          @click="modeId = m.id"
        >
          {{ m.label }}
        </button>
      </div>
      <div class="protocol-form">
        <div v-if="modeId === 'loader'" class="protocol-controls">
          <label>
            加载器
            <select v-model="loaderKind">
              <option value="fabric">Fabric</option>
              <option value="forge">Forge</option>
              <option value="neoforge">NeoForge</option>
              <option value="quilt">Quilt</option>
              <option value="optifine">OptiFine</option>
            </select>
          </label>
          <label>
            加载器版本
            <input
              v-model="loaderVersion"
              type="text"
              placeholder="可选，留空安装最新版"
            />
          </label>
        </div>
        <div v-if="modeId === 'modpack'" class="protocol-controls">
          <label>
            平台
            <select v-model="packFrom">
              <option value="">自动识别</option>
              <option value="modrinth">Modrinth</option>
              <option value="curseforge">CurseForge</option>
            </select>
          </label>
          <label>
            整合包版本
            <input
              v-model="packVersion"
              type="text"
              placeholder="可选，版本号或 fileId"
            />
          </label>
        </div>
        <div class="protocol-input-row">
          <input
            v-model="input"
            type="text"
            :placeholder="mode.placeholder"
            @keyup.enter="invoke"
          />
          <button
            type="button"
            class="protocol-submit"
            :disabled="!uri"
            @click="invoke"
          >
            {{ mode.action }}
          </button>
        </div>
      </div>
      <div class="protocol-preview">
        <a v-if="uri" :href="uri">{{ uri }}</a>
        <span v-else class="placeholder">{{ example }}</span>
      </div>
      <p class="protocol-note">
        首次使用前，请在 Portal 的「设置 → 通知与可选项」中开启 Portal
        协议；macOS 应用包已内置声明，Linux
        桌面集成安装时会自动注册。文件夹、自定义实例 ID
        等更多参数与命令行用法，参见
        <a
          href="https://github.com/tiouoo/Portal/blob/main/docs/command-line.md"
          target="_blank"
          rel="noreferrer"
          >命令行与 portal:// 协议文档</a
        >
        </p>
      </div>
    </div>
  </section>
</template>

<style scoped src="../styles/protocol-section.css"></style>
