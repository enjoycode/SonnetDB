<template>
  <div>
    <n-grid :cols="3" :x-gap="16" :y-gap="16">
      <n-gi>
        <n-card title="数据库数量">
          <n-statistic :value="databases.length" />
        </n-card>
      </n-gi>
      <n-gi v-if="auth.isSuperuser">
        <n-card title="用户数量">
          <n-statistic :value="users.length" />
        </n-card>
      </n-gi>
      <n-gi v-if="auth.isSuperuser">
        <n-card title="授权条目">
          <n-statistic :value="grants.length" />
        </n-card>
      </n-gi>
    </n-grid>

    <n-card title="数据库" style="margin-top:16px;">
      <n-data-table :columns="dbCols" :data="dbRows" :bordered="false" size="small" />
    </n-card>

    <n-card v-if="auth.isSuperuser" title="用户" style="margin-top:16px;">
      <n-data-table :columns="userCols" :data="users" :bordered="false" size="small" />
    </n-card>

    <n-alert v-if="lastError" type="error" :title="lastError" style="margin-top:16px;" closable />
  </div>
</template>

<script setup lang="ts">
import { computed, h, onMounted, ref } from 'vue';
import { NCard, NGrid, NGi, NStatistic, NDataTable, NAlert, type DataTableColumns } from 'naive-ui';
import { useAuthStore } from '@/stores/auth';
import { execControlPlaneSql, rowsToObjects } from '@/api/sql';

interface UserRow { name: string; is_superuser: boolean; created_utc: string; token_count: number; [k: string]: unknown }
interface GrantRow { user_name: string; database: string; permission: string; [k: string]: unknown }

const auth = useAuthStore();

const databases = ref<string[]>([]);
const users = ref<UserRow[]>([]);
const grants = ref<GrantRow[]>([]);
const lastError = ref<string>('');

const dbRows = computed(() => databases.value.map((d) => ({ name: d })));

const dbCols: DataTableColumns<{ name: string }> = [{ title: '数据库', key: 'name' }];
const userCols: DataTableColumns<UserRow> = [
  { title: '用户名', key: 'name' },
  { title: '超级用户', key: 'is_superuser', render: (r) => h('span', r.is_superuser ? '是' : '否') },
  { title: '创建时间', key: 'created_utc' },
  { title: 'Token 数', key: 'token_count' },
];

async function loadAll(): Promise<void> {
  lastError.value = '';
  const dbRs = await execControlPlaneSql(auth.api, 'SHOW DATABASES');
  if (dbRs.error) { lastError.value = dbRs.error.message; return; }
  databases.value = dbRs.rows.map((r) => String(r[0]));

  if (auth.isSuperuser) {
    const usrRs = await execControlPlaneSql(auth.api, 'SHOW USERS');
    if (!usrRs.error) users.value = rowsToObjects<UserRow>(usrRs);

    const grRs = await execControlPlaneSql(auth.api, 'SHOW GRANTS');
    if (!grRs.error) grants.value = rowsToObjects<GrantRow>(grRs);
  }
}

onMounted(loadAll);
</script>
