<template>
  <n-card title="SQL Console" :bordered="false">
    <n-space vertical :size="12">
      <n-space align="center">
        <span>目标：</span>
        <n-select
          v-model:value="targetDb"
          :options="dbOptions"
          style="width: 240px"
          placeholder="选择数据库或控制面"
        />
        <n-button @click="reloadDbs" size="small">刷新</n-button>
      </n-space>
      <n-input
        v-model:value="sql"
        type="textarea"
        :autosize="{ minRows: 6, maxRows: 16 }"
        placeholder="SHOW DATABASES;
SELECT * FROM measurement WHERE ts &gt;= '2025-01-01' LIMIT 100;"
      />
      <n-space>
        <n-button type="primary" :loading="running" @click="run">运行</n-button>
        <n-button @click="clear">清空</n-button>
        <span v-if="lastMeta" class="meta">
          {{ lastMeta }}
        </span>
      </n-space>
      <n-alert v-if="errorMsg" type="error" :title="errorMsg" closable @close="errorMsg = ''" />
      <n-data-table
        v-if="resultColumns.length > 0"
        :columns="resultColumns"
        :data="resultRows"
        :bordered="false"
        size="small"
        :max-height="480"
      />
      <n-text v-else-if="ranOnce && !errorMsg" depth="3">语句已执行，没有结果集。</n-text>
    </n-space>
  </n-card>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  NCard, NSpace, NInput, NButton, NSelect, NAlert, NDataTable, NText,
  type DataTableColumns, type SelectOption,
} from 'naive-ui';
import { useAuthStore } from '@/stores/auth';
import {
  execControlPlaneSql, execDataSql, rowsToObjects, type SqlResultSet,
} from '@/api/sql';

const auth = useAuthStore();

const CONTROL_PLANE_KEY = '__control_plane__';
const targetDb = ref<string>(CONTROL_PLANE_KEY);
const databases = ref<string[]>([]);
const sql = ref<string>('SHOW DATABASES');
const running = ref(false);
const errorMsg = ref('');
const ranOnce = ref(false);
const lastMeta = ref('');
const resultColumns = ref<DataTableColumns<Record<string, unknown>>>([]);
const resultRows = ref<Record<string, unknown>[]>([]);

const dbOptions = computed<SelectOption[]>(() => [
  { label: '控制面 (CREATE USER / GRANT / SHOW USERS …)', value: CONTROL_PLANE_KEY },
  ...databases.value.map((d) => ({ label: d, value: d })),
]);

async function reloadDbs(): Promise<void> {
  const rs = await execControlPlaneSql(auth.api, 'SHOW DATABASES');
  if (!rs.error) databases.value = rs.rows.map((r) => String(r[0]));
}

async function run(): Promise<void> {
  errorMsg.value = '';
  resultColumns.value = [];
  resultRows.value = [];
  lastMeta.value = '';
  if (!sql.value.trim()) return;
  running.value = true;
  try {
    const rs: SqlResultSet = targetDb.value === CONTROL_PLANE_KEY
      ? await execControlPlaneSql(auth.api, sql.value)
      : await execDataSql(auth.api, targetDb.value, sql.value);
    ranOnce.value = true;
    if (rs.error) {
      errorMsg.value = `[${rs.error.code ?? 'error'}] ${rs.error.message}`;
      return;
    }
    if (rs.hasColumns) {
      resultColumns.value = rs.columns.map<DataTableColumns<Record<string, unknown>>[number]>((c) => ({
        title: c, key: c, ellipsis: { tooltip: true },
      }));
      resultRows.value = rowsToObjects(rs);
    }
    if (rs.end) {
      const parts: string[] = [];
      if (rs.hasColumns) parts.push(`${rs.end.rowCount} 行`);
      if (rs.end.recordsAffected >= 0) parts.push(`受影响 ${rs.end.recordsAffected}`);
      parts.push(`${rs.end.elapsedMs.toFixed(2)} ms`);
      lastMeta.value = parts.join(' · ');
    }
  } finally {
    running.value = false;
  }
}

function clear(): void {
  sql.value = '';
  resultColumns.value = [];
  resultRows.value = [];
  errorMsg.value = '';
  lastMeta.value = '';
  ranOnce.value = false;
}

onMounted(reloadDbs);
</script>

<style scoped>
.meta { color: #888; font-size: 12px; }
</style>
