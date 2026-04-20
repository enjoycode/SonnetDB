<template>
  <n-card title="数据库" :bordered="false">
    <n-space vertical :size="12">
      <n-space>
        <n-input v-model:value="newName" placeholder="新数据库名（字母数字下划线短横线）" style="width:280px;" />
        <n-button type="primary" :disabled="!auth.isSuperuser" @click="onCreate">CREATE DATABASE</n-button>
        <n-button @click="reload">刷新</n-button>
      </n-space>
      <n-alert v-if="errorMsg" type="error" :title="errorMsg" closable @close="errorMsg = ''" />
      <n-data-table :columns="cols" :data="rows" :bordered="false" size="small" />
    </n-space>
  </n-card>
</template>

<script setup lang="ts">
import { computed, h, onMounted, ref } from 'vue';
import {
  NCard, NSpace, NInput, NButton, NAlert, NDataTable, NPopconfirm, useMessage,
  type DataTableColumns,
} from 'naive-ui';
import { useAuthStore } from '@/stores/auth';
import { execControlPlaneSql, isValidIdentifier } from '@/api/sql';

const auth = useAuthStore();
const message = useMessage();

interface DbRow { name: string }
const databases = ref<string[]>([]);
const newName = ref('');
const errorMsg = ref('');

const rows = computed<DbRow[]>(() => databases.value.map((n) => ({ name: n })));

const cols = computed<DataTableColumns<DbRow>>(() => [
  { title: '名称', key: 'name' },
  {
    title: '操作',
    key: 'actions',
    width: 120,
    render: (row) => h(NPopconfirm, {
      onPositiveClick: () => onDrop(row.name),
      disabled: !auth.isSuperuser,
    }, {
      trigger: () => h(NButton, {
        size: 'small', type: 'error', text: true, disabled: !auth.isSuperuser,
      }, { default: () => 'DROP' }),
      default: () => `确认 DROP DATABASE ${row.name}？数据将不可恢复。`,
    }),
  },
]);

async function reload(): Promise<void> {
  errorMsg.value = '';
  const rs = await execControlPlaneSql(auth.api, 'SHOW DATABASES');
  if (rs.error) { errorMsg.value = rs.error.message; return; }
  databases.value = rs.rows.map((r) => String(r[0]));
}

async function onCreate(): Promise<void> {
  const name = newName.value.trim();
  if (!isValidIdentifier(name)) {
    message.error('名称必须以字母开头，仅包含字母数字下划线。');
    return;
  }
  const rs = await execControlPlaneSql(auth.api, `CREATE DATABASE ${name}`);
  if (rs.error) { message.error(rs.error.message); return; }
  message.success(`已创建 ${name}`);
  newName.value = '';
  await reload();
}

async function onDrop(name: string): Promise<void> {
  const rs = await execControlPlaneSql(auth.api, `DROP DATABASE ${name}`);
  if (rs.error) { message.error(rs.error.message); return; }
  message.success(`已删除 ${name}`);
  await reload();
}

onMounted(reload);
</script>
