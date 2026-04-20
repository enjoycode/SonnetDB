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
import { computed } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import {
  NLayout, NLayoutSider, NLayoutHeader, NLayoutContent, NMenu,
  NSpace, NTag, NButton, type MenuOption,
} from 'naive-ui';
import { useAuthStore } from '@/stores/auth';

const auth = useAuthStore();
const router = useRouter();
const route = useRoute();

const baseMenu: MenuOption[] = [
  { label: '概览', key: 'dashboard' },
  { label: 'SQL Console', key: 'sql' },
  { label: '数据库', key: 'databases' },
];
const adminMenu: MenuOption[] = [
  { label: '用户', key: 'users' },
  { label: '权限', key: 'grants' },
];

const menuOptions = computed<MenuOption[]>(() => auth.isSuperuser ? [...baseMenu, ...adminMenu] : baseMenu);

const titleByKey: Record<string, string> = {
  dashboard: '概览',
  sql: 'SQL Console',
  databases: '数据库',
  users: '用户',
  grants: '权限',
};

const activeKey = computed(() => (route.name as string | undefined) ?? 'dashboard');
const activeTitle = computed(() => titleByKey[activeKey.value] ?? '');

function onMenu(key: string): void {
  router.push({ name: key });
}

function onLogout(): void {
  auth.logout();
  router.replace({ name: 'login' });
}
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
</style>
