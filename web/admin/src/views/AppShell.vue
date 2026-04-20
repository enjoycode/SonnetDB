<template>
  <n-layout has-sider style="height: 100vh">
    <n-layout-sider
      bordered
      collapse-mode="width"
      :collapsed-width="64"
      :width="220"
      :native-scrollbar="false"
    >
      <div class="brand">TSLite</div>
      <n-menu :options="menuOptions" :value="activeKey" @update:value="onMenu" />
    </n-layout-sider>
    <n-layout>
      <n-layout-header bordered class="header">
        <span class="title">{{ activeTitle }}</span>
        <n-space>
          <n-tag :type="liveTagType" size="small">
            <template #icon>
              <span class="dot" :class="liveDotClass" />
            </template>
            {{ liveLabel }}
          </n-tag>
          <n-tag :type="auth.isSuperuser ? 'success' : 'info'" size="small">
            {{ auth.username }}{{ auth.isSuperuser ? ' · admin' : '' }}
          </n-tag>
          <n-button text type="error" @click="onLogout">退出</n-button>
        </n-space>
      </n-layout-header>
      <n-layout-content content-style="padding:24px;">
        <router-view />
      </n-layout-content>
    </n-layout>
  </n-layout>
</template>

<script setup lang="ts">
import { computed, onMounted, onBeforeUnmount } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import {
  NLayout, NLayoutSider, NLayoutHeader, NLayoutContent, NMenu,
  NSpace, NTag, NButton, type MenuOption,
} from 'naive-ui';
import { useAuthStore } from '@/stores/auth';
import { useEventsStore } from '@/stores/events';

const auth = useAuthStore();
const events = useEventsStore();
const router = useRouter();
const route = useRoute();

const baseMenu: MenuOption[] = [
  { label: '概览', key: 'dashboard' },
  { label: 'SQL Console', key: 'sql' },
  { label: '数据库', key: 'databases' },
  { label: '事件流', key: 'events' },
];
const adminMenu: MenuOption[] = [
  { label: '用户', key: 'users' },
  { label: '权限', key: 'grants' },
  { label: 'Token', key: 'tokens' },
];

const menuOptions = computed<MenuOption[]>(() => auth.isSuperuser ? [...baseMenu, ...adminMenu] : baseMenu);

const titleByKey: Record<string, string> = {
  dashboard: '概览',
  sql: 'SQL Console',
  databases: '数据库',
  events: '事件流',
  users: '用户',
  grants: '权限',
  tokens: 'Token',
};

const activeKey = computed(() => (route.name as string | undefined) ?? 'dashboard');
const activeTitle = computed(() => titleByKey[activeKey.value] ?? '');

const liveLabel = computed(() => {
  switch (events.status) {
    case 'open': return '实时';
    case 'connecting': return '连接中…';
    case 'error': return '断线重连…';
    case 'unauthorized': return 'SSE 未授权';
    default: return '未连接';
  }
});
const liveTagType = computed(() => {
  switch (events.status) {
    case 'open': return 'success' as const;
    case 'connecting': return 'info' as const;
    case 'error':
    case 'unauthorized': return 'warning' as const;
    default: return 'default' as const;
  }
});
const liveDotClass = computed(() => `dot-${events.status}`);

function onMenu(key: string): void {
  router.push({ name: key });
}

function onLogout(): void {
  events.disconnect();
  auth.logout();
  router.replace({ name: 'login' });
}

// 已登录进入 AppShell：建立 SSE 连接；退出 AppShell：保持连接（store 内部受 auth 控制）
onMounted(() => {
  if (auth.isAuthenticated) events.connect();
});
onBeforeUnmount(() => {
  // 不在此 disconnect，logout 时显式断开即可，避免 SPA 路由切换误关
});
</script>

<style scoped>
.brand {
  font-weight: bold;
  font-size: 18px;
  text-align: center;
  padding: 16px 0;
  color: #18a058;
}
.header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 24px;
  height: 56px;
  background: #fff;
}
.title {
  font-weight: 500;
}
.dot {
  display: inline-block;
  width: 8px;
  height: 8px;
  border-radius: 50%;
  margin-right: 4px;
  vertical-align: middle;
  background: #c0c0c0;
}
.dot-open { background: #18a058; box-shadow: 0 0 0 2px rgba(24,160,88,.18); }
.dot-connecting { background: #2080f0; }
.dot-error, .dot-unauthorized { background: #f0a020; }
</style>
