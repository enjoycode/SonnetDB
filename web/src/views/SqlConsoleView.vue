<template>
  <n-space vertical :size="16">
    <n-card title="SQL Console" :bordered="false">
      <n-space vertical :size="12">
        <n-space align="center">
          <span>目标：</span>
          <n-select
            v-model:value="targetDb"
            :options="dbOptions"
            style="width: 240px"
            placeholder="选择数据库"
          />
          <n-text depth="3" style="font-size: 12px">
            <template v-if="targetDb === CONTROL_PLANE_KEY">
              <n-tag size="tiny" type="warning" style="margin-right: 4px">system</n-tag>
              系统数据库（执行 CREATE USER / GRANT / SHOW USERS / CREATE DATABASE 等控制面 SQL）
            </template>
            <template v-else-if="targetDb">
              用户数据库（执行 SELECT / INSERT / CREATE MEASUREMENT 等数据面 SQL）
            </template>
          </n-text>
          <n-button @click="reloadDbs" size="small">刷新</n-button>
        </n-space>

        <SqlEditor
          v-model="sql"
          :schema="currentSchema"
          placeholder="SHOW DATABASES;"
        />

        <n-space>
          <n-button type="primary" :loading="running" @click="run">运行</n-button>
          <n-button @click="clear">清空</n-button>
          <span v-if="lastMeta" class="meta">{{ lastMeta }}</span>
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
  </n-space>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue';
import {
  NCard, NSpace, NButton, NSelect, NAlert, NDataTable, NText,
  type DataTableColumns, type SelectOption,
} from 'naive-ui';
import { useAuthStore } from '@/stores/auth';
import {
  execControlPlaneSql, execDataSql, rowsToObjects, type SqlResultSet,
} from '@/api/sql';
import { listDatabases } from '@/api/server';
import { fetchSchema, type MeasurementInfo } from '@/api/schema';
import SqlEditor from '@/components/SqlEditor.vue';
import { useSqlConsoleStore } from '@/stores/sqlConsole';

const auth = useAuthStore();
const sqlConsole = useSqlConsoleStore();

const CONTROL_PLANE_KEY = '__control_plane__';
const targetDb = ref<string>('');
const databases = ref<string[]>([]);
const sql = ref<string>('SHOW DATABASES');
const running = ref(false);
const errorMsg = ref('');
const ranOnce = ref(false);
const lastMeta = ref('');
const resultColumns = ref<DataTableColumns<Record<string, unknown>>>([]);
const resultRows = ref<Record<string, unknown>[]>([]);
const currentSchema = ref<MeasurementInfo[]>([]);

const dbOptions = computed<SelectOption[]>(() => {
  const options: SelectOption[] = auth.isSuperuser
    ? [{ label: 'system （系统库 / 控制面）', value: CONTROL_PLANE_KEY }]
    : [];
  return [
    ...options,
    ...databases.value.map((d) => ({ label: d, value: d })),
  ];
});

async function reloadDbs(): Promise<void> {
  const result = await listDatabases(auth.api);
  if (result.error) {
    errorMsg.value = result.error.message;
    return;
  }
  databases.value = result.databases;
  normalizeTarget();
}

function normalizeTarget(): void {
  if (auth.isSuperuser) {
    if (!targetDb.value) targetDb.value = CONTROL_PLANE_KEY;
    return;
  }
  if (targetDb.value === CONTROL_PLANE_KEY || !databases.value.includes(targetDb.value)) {
    targetDb.value = databases.value[0] ?? '';
  }
}

async function loadSchema(db: string): Promise<void> {
  if (!db || db === CONTROL_PLANE_KEY) {
    currentSchema.value = [];
    return;
  }
  try {
    const resp = await fetchSchema(auth.api, db);
    currentSchema.value = resp.measurements;
  } catch {
    currentSchema.value = [];
  }
}

watch(targetDb, (db) => loadSchema(db));

// M6: 同步当前 SQL/db 到共享 store，供 CopilotDock 注入页面上下文。
watch([targetDb, sql], ([db, text]) => {
  sqlConsole.setCurrent(db === CONTROL_PLANE_KEY ? '' : db, text);
}, { immediate: true });

async function run(): Promise<void> {
  errorMsg.value = '';
  resultColumns.value = [];
  resultRows.value = [];
  lastMeta.value = '';
  if (!sql.value.trim()) return;
  if (!targetDb.value) {
    errorMsg.value = '当前没有可执行的数据面数据库。';
    return;
  }
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

async function applyPendingExecution(): Promise<void> {
  const pending = sqlConsole.consumeExecution();
  if (!pending) return;

  targetDb.value = pending.db;
  sql.value = pending.sql;
  await loadSchema(pending.db);
  await nextTick();

  if (pending.runImmediately) {
    await run();
  }
}

onMounted(async () => {
  normalizeTarget();
  await reloadDbs();
  if (targetDb.value && targetDb.value !== CONTROL_PLANE_KEY) {
    await loadSchema(targetDb.value);
  }
  await applyPendingExecution();
});
</script>

<style scoped>
.meta { color: #888; font-size: 12px; }
</style>
