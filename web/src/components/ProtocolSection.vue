<script setup>
import { computed, ref, watch } from "vue";

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

watch(modeId, () => {
  input.value = "";
  loaderVersion.value = "";
  packVersion.value = "";
  loaderKind.value = "fabric";
  packFrom.value = "";
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
          首次使用前，请在 Portal 的「设置 → 其他设置」中开启 Portal 协议；macOS
          应用包已内置声明，Linux 桌面集成安装时会自动注册。文件夹、自定义实例
          ID 等更多参数与命令行用法，参见
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

<style scoped>
.section {
  padding-top: 60px;
  padding-bottom: 60px;
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
  font-size: 14px;
  margin: 12px 0 0;
  line-height: 1.6;
}
.protocol-card {
  max-width: 860px;
  margin: 0 auto;
  padding: 26px;
  border: 1px solid #e2e6ee;
  border-radius: 16px;
  background: white;
  box-shadow: 0 12px 28px rgba(60, 75, 105, 0.05);
}
.protocol-tabs {
  display: inline-flex;
  padding: 3px;
  gap: 3px;
  border: 1px solid #e2e6ee;
  border-radius: 12px;
  background: #f2f5fa;
}
.protocol-tabs button {
  height: 36px;
  padding: 0 16px;
  border: 0;
  border-radius: 10px;
  background: transparent;
  color: #5a6478;
  font-size: 12px;
  font-weight: 600;
  cursor: pointer;
}
.protocol-tabs button.active {
  background: white;
  color: var(--ink);
  box-shadow: 0 4px 12px rgba(60, 75, 105, 0.08);
}
.protocol-form {
  margin-top: 18px;
}
.protocol-controls {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 280px));
  gap: 12px;
  margin-bottom: 12px;
}
.protocol-controls label {
  display: block;
  color: #5a6478;
  font-size: 11px;
  font-weight: 700;
}
.protocol-controls select,
.protocol-controls input {
  width: 100%;
  height: 38px;
  margin-top: 5px;
  padding: 0 12px;
  border: 1px solid #d5deeb;
  border-radius: 12px;
  background: white;
  color: var(--ink);
  font-size: 12px;
  font-family: inherit;
  font-weight: 600;
}
.protocol-controls select {
  padding-right: 32px;
  cursor: pointer;
  appearance: none;
  -webkit-appearance: none;
  -moz-appearance: none;
  background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='8' viewBox='0 0 12 8'%3E%3Cpath d='M1 1.5 6 6.5 11 1.5' fill='none' stroke='%238090a9' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'/%3E%3C/svg%3E");
  background-repeat: no-repeat;
  background-position: right 12px center;
  background-size: 12px auto;
}
.protocol-input-row {
  display: flex;
  gap: 10px;
}
.protocol-input-row input {
  flex: 1;
  min-width: 0;
  height: 46px;
  padding: 0 16px;
  border: 1px solid #d5deeb;
  border-radius: 12px;
  background: white;
  color: var(--ink);
  font-size: 13px;
  font-family: inherit;
}
.protocol-card input {
  user-select: text;
}
.protocol-card input::placeholder {
  color: #9aa3b5;
}
.protocol-card input:focus,
.protocol-card select:focus {
  outline: none;
  border-color: rgba(42, 112, 245, 0.55);
}
.protocol-submit {
  height: 46px;
  padding: 0 28px;
  border: 0;
  border-radius: 12px;
  background: var(--blue);
  color: white;
  font-size: 13px;
  font-weight: 700;
  cursor: pointer;
  box-shadow: 0 12px 28px rgba(42, 112, 245, 0.23);
}
.protocol-submit:hover {
  background: #1f62df;
  box-shadow: 0 12px 32px rgba(42, 112, 245, 0.34);
}
.protocol-submit:disabled {
  background: #b9cdf3;
  box-shadow: none;
  cursor: not-allowed;
}
.protocol-preview {
  margin-top: 14px;
  padding: 11px 14px;
  border: 1px dashed #d5ddeb;
  border-radius: 10px;
  background: #f2f5fa;
  font-family: ui-monospace, "Cascadia Mono", Consolas, monospace;
  font-size: 11px;
  line-height: 1.5;
  word-break: break-all;
  user-select: text;
}
.protocol-preview a {
  color: var(--blue);
}
.protocol-preview a:hover {
  text-decoration: underline;
  text-underline-offset: 2px;
}
.protocol-preview .placeholder {
  color: #9aa3b5;
}
.protocol-note {
  margin: 16px 0 0;
  color: var(--muted);
  font-size: 12px;
  line-height: 1.7;
}
.protocol-note a {
  color: var(--blue);
  font-weight: 700;
}
.protocol-note a:hover {
  color: #1f62df;
  text-decoration: underline;
  text-underline-offset: 2px;
}

@media (max-width: 820px) {
  .section {
    padding-top: 80px;
    padding-bottom: 80px;
  }
  .protocol-tabs {
    display: flex;
    flex-wrap: wrap;
  }
  .protocol-tabs button {
    flex: 1 1 auto;
  }
  .protocol-controls {
    grid-template-columns: 1fr;
  }
  .protocol-input-row {
    flex-direction: column;
  }
}

@media (max-width: 520px) {
  .section-heading {
    text-align: left;
  }
  .section-heading h2 {
    font-size: 36px;
  }
  .protocol-card {
    padding: 22px;
  }
}
</style>
