import { createRouter as createVueRouter, createWebHistory, createMemoryHistory } from 'vue-router';
import HomeView from './views/HomeView.vue';
import InstallView from './views/InstallView.vue';
import MacOsInstallView from './views/MacOsInstallView.vue';
import LegalView from './views/LegalView.vue';

export function createRouter() {
  return createVueRouter({
    history: typeof window !== 'undefined' ? createWebHistory() : createMemoryHistory(),
    routes: [
      {
        path: '/',
        component: HomeView,
        meta: {
          title: 'Portal - 你的 Minecraft，从这里出发：开源跨平台 Minecraft 启动器',
          description: 'Portal - 简洁、现代的跨平台 Minecraft 启动器与实例管理器。',
        },
      },
      {
        path: '/install',
        component: InstallView,
        meta: {
          title: 'Portal 安装说明 - macOS 和 Linux 安装指南',
          description:
            'Portal 启动器多平台安装指南。macOS 解除"已损坏"限制，Linux AppImage、RPM、DEB 安装教程及依赖配置说明。',
        },
      },
      {
        path: '/install/macos',
        component: MacOsInstallView,
        meta: {
          title: 'Portal macOS 安装说明 - 解除"已损坏"限制',
          description:
            'macOS 安装 Portal 启动器时遇到"已损坏，无法打开"或文件损坏？按此说明选择正确的安装包并解除系统限制。',
        },
      },
      {
        path: '/policy',
        component: LegalView,
        meta: {
          title: 'Portal 隐私协议与使用条款',
          description:
            'Portal 隐私协议与使用条款，包含遥测信息、数据保护、软件许可、禁止盗版用途、免责声明和生效规则。',
        },
      },
    ],
    scrollBehavior(to, from, savedPosition) {
      if (savedPosition) return savedPosition;
      if (to.hash) return { el: to.hash, top: 88 };
      return { top: 0 };
    },
  });
}
