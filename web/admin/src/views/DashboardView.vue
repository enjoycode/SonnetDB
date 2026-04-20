<template>
  <n-layout has-sider style="height:100%">
    <n-layout-sider
      bordered
      collapse-mode="width"
      :collapsed-width="64"
      :width="200"
      :native-scrollbar="false"
    >
      <div class="brand">TSLite</div>
      <n-menu :options="menuOptions" :value="activeKey" />
    </n-layout-sider>
    <n-layout>
      <n-layout-header bordered class="header">
        <span>{{ activeTitle }}</span>
        <n-space>
          <n-tag :type="auth.isSuperuser ? 'success' : 'info'" size="small">
            {{ auth.username }}{{ auth.isSuperuser ? ' · admin' : '' }}
          </n-tag>
          <n-button text type="error" @click="onLogout">退出</n-button>
        </n-space>
      </n-layout-header>
      <n-layout-content content-style="padding:24px;">
        <n-grid :cols="3" :x-gap="16" :y-gap="16">
          <n-gi>
            <n-card title="数据库数量">
              <n-statistic :value="databases.length" />
            </n-card>
          </n-gi>
          <n-gi>
            <n-card title="用户数量">
              <n-statistic :value="users.length" />
            </n-card>
          </n-gi>
          <n-gi>
            <n-card title="授权条目">
              <n-statistic :value="grants.length" />
            </n-card>
          </n-gi>
        </n-grid>

        <n-card title="数据库" style="margin-top:16px;">
          <n-data-table :columns="dbCols" :data="databases.map((d) => ({ name: d }))" :bordered="false" size="small" />
        </n-card>

        <n-card title="用户" style="margin-top:16px;">
          <n-data-table :columns="userCols" :data="users" :bordered="false" size="small" />
        </n-card>
      </n-layout-content>
    </n-layout>
  </n-layout>
</template>

<script setup lang="ts">
import { computed, h, onMounted, ref } from 'vue';
import { useRouter } from 'vue-router';
import {
  NLayout, NLayoutSider, NLayoutHeader, NLayoutContent, NMenu,
  NCard, NGrid, NGi, NStatistic, NDataTable, NSpace, NTag, NButton,
  type DataTableColumns, type MenuOption,
} from 'naive-ui';
import { useAuthStore } from '@/stores/auth';

interface UserRow {
  Name: string;
  IsSuperuser: boolean;
  CreatedUtc: string;
  TokenCount: number;
}

interface GrantRow {
  UserName: string;
  Database: string;
  Permission: string;
}

const auth = useAuthStore();
const router = useRouter();

const databases = ref<string[]>([]);
const users = ref<UserRow[]>([]);
const grants = ref<GrantRow[]>([]);
const activeKey = ref<string>('dashboard');
const activeTitle = computed(() => '概览');

const menuOptions = computed<MenuOption[]>(() => [
  { label: '概览', key: 'dashboard' },
]);

const dbCols: DataTableColumns<{ name: string }> = [
  { title: '数据库', key: 'name' },
];

const userCols: DataTableColumns<UserRow> = [
  { title: '用户名', key: 'Name' },
  { title: '超级用户', key: 'IsSuperuser', render: (r) => h('span', r.IsSuperuser ? '是' : '否') },
  { title: '创建时间', key: 'CreatedUtc' },
  { title: 'Token 数', key: 'TokenCount' },
];

async function runSql<T>(db: string, sql: string): Promise<T[]> {
  const resp = await auth.api.post<string>(`/v1/db/${db}/sql`, { sql }, { responseType: 'text', transformResponse: (v) => v });
  // ndjson：每行一个 JSON 对象，第一行 columns metadata，剩余行为数据
  const lines = (resp.data as string).split(/\r?\n/).filter((l) => l.length > 0);
  if (lines.length === 0) return [];
  const rows: T[] = [];
  // 解析 ndjson：跳过 type=columns，收集 type=row
  for (const line of lines) {
    try {
      const obj = JSON.parse(line);
      if (obj?.type === 'row' && Array.isArray(obj.values) && Array.isArray(obj.columns)) {
        const row: Record<string, unknown> = {};
        obj.columns.forEach((c: string, i: number) => { row[c] = obj.values[i]; });
        rows.push(row as T);
      } else if (obj?.row && Array.isArray(obj.row)) {
        rows.push(obj as unknown as T);
      } else if (Array.isArray(obj)) {
        rows.push(obj as unknown as T);
      } else if (obj && typeof obj === 'object' && !('columns' in obj && 'rowCount' in obj)) {
        rows.push(obj as T);
      }
    } catch {
      // skip
    }
  }
  return rows;
}

async function loadAll(): Promise<void> {
  // 任意数据库即可路由 SHOW DATABASES；为了简单，用 _system 或第一个可用数据库。
  // SHOW DATABASES 在 SqlExecutor 走 IControlPlane，对 db 名实际不依赖；但 endpoint 仍需 db 路径段。
  // 这里先尝试常见的 metrics；若 404，再 fallback。
  const probeDb = 'metrics';
  try {
    const dbs = await runSql<{ name: string }>(probeDb, 'SHOW DATABASES');
    databases.value = dbs.map((r) => (r as unknown as { name?: string; Name?: string }).name ?? (r as unknown as { Name?: string }).Name ?? String(r));
  } catch {
    databases.value = [];
  }
  if (auth.isSuperuser) {
    try {
      users.value = await runSql<UserRow>(probeDb, 'SHOW USERS');
    } catch { users.value = []; }
    try {
      grants.value = await runSql<GrantRow>(probeDb, 'SHOW GRANTS');
    } catch { grants.value = []; }
  }
}

function onLogout(): void {
  auth.logout();
  router.replace({ name: 'login' });
}

onMounted(loadAll);
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
</style>
