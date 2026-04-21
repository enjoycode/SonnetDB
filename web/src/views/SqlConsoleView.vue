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
            placeholder="选择数据库或控制面"
          />
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

    <!-- AI 助手面板（仅 AI 启用时显示） -->
    <n-card v-if="aiEnabled" :bordered="false" size="small">
      <template #header>
        <span>SNDBCopilot
          <n-tag size="tiny" type="info" style="margin-left: 8px; vertical-align: middle">
            {{ aiStatus?.provider }} / {{ aiStatus?.model }}
          </n-tag>
        </span>
      </template>

      <n-space vertical :size="10">
        <n-input
          v-model:value="aiPrompt"
          type="textarea"
          :autosize="{ minRows: 2, maxRows: 5 }"
          placeholder="用自然语言描述你的查询需求，例如：查询最近一小时每个设备的平均温度..."
        />

        <n-space>
          <n-button
            type="primary"
            :loading="aiRunning"
            :disabled="!aiPrompt.trim()"
            @click="generateSql"
          >
            生成 SQL
          </n-button>
          <n-button
            :loading="aiRunning"
            :disabled="resultRows.length === 0"
            @click="analyzeResults"
          >
            分析结果
          </n-button>
          <n-button v-if="aiRunning" text type="error" @click="aiAbort?.abort()">停止</n-button>
        </n-space>

        <n-scrollbar v-if="aiResponse" style="max-height: 240px">
          <pre class="ai-response">{{ aiResponse }}</pre>
        </n-scrollbar>
      </n-space>
    </n-card>
  </n-space>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import {
  NCard, NSpace, NButton, NSelect, NAlert, NDataTable, NText,
  NInput, NScrollbar, NTag,
  type DataTableColumns, type SelectOption,
} from 'naive-ui';
import { useAuthStore } from '@/stores/auth';
import {
  execControlPlaneSql, execDataSql, rowsToObjects, type SqlResultSet,
} from '@/api/sql';
import { listDatabases } from '@/api/server';
import { fetchSchema, type MeasurementInfo } from '@/api/schema';
import { fetchAiStatus, streamAiChat, type AiStatusResponse } from '@/api/ai';
import SqlEditor from '@/components/SqlEditor.vue';

const auth = useAuthStore();

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

// AI 状态
const aiEnabled = ref(false);
const aiStatus = ref<AiStatusResponse | null>(null);
const aiPrompt = ref('');
const aiResponse = ref('');
const aiRunning = ref(false);
const aiAbort = ref<AbortController | null>(null);

const dbOptions = computed<SelectOption[]>(() => {
  const options: SelectOption[] = auth.isSuperuser
    ? [{ label: '控制面 (CREATE USER / GRANT / SHOW USERS …)', value: CONTROL_PLANE_KEY }]
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

// ---- AI ----

async function generateSql(): Promise<void> {
  if (!aiPrompt.value.trim() || aiRunning.value) return;
  aiResponse.value = '';
  aiRunning.value = true;
  const ac = new AbortController();
  aiAbort.value = ac;

  const db = targetDb.value !== CONTROL_PLANE_KEY ? targetDb.value : undefined;
  const messages = [{ role: 'user', content: aiPrompt.value }];

  let generated = '';
  try {
    for await (const token of streamAiChat(auth.state?.token ?? '', messages, db, 'sql_gen')) {
      if (ac.signal.aborted) break;
      generated += token;
      aiResponse.value = generated;
    }
    if (generated.trim()) {
      sql.value = generated.trim();
    }
  } catch (e: unknown) {
    aiResponse.value = `错误：${e instanceof Error ? e.message : String(e)}`;
  } finally {
    aiRunning.value = false;
    aiAbort.value = null;
  }
}

async function analyzeResults(): Promise<void> {
  if (resultRows.value.length === 0 || aiRunning.value) return;
  aiResponse.value = '';
  aiRunning.value = true;
  const ac = new AbortController();
  aiAbort.value = ac;

  const preview = resultRows.value.slice(0, 20);
  const content = `SQL: ${sql.value}\n列: ${resultColumns.value.map((c) => String((c as { key: string }).key)).join(', ')}\n数据（前${preview.length}行）:\n${JSON.stringify(preview, null, 2)}`;

  let reply = '';
  try {
    for await (const token of streamAiChat(auth.state?.token ?? '', [{ role: 'user', content }], undefined, 'analyze')) {
      if (ac.signal.aborted) break;
      reply += token;
      aiResponse.value = reply;
    }
  } catch (e: unknown) {
    aiResponse.value = `错误：${e instanceof Error ? e.message : String(e)}`;
  } finally {
    aiRunning.value = false;
    aiAbort.value = null;
  }
}

onMounted(async () => {
  normalizeTarget();
  await Promise.all([
    reloadDbs(),
    fetchAiStatus(auth.api).then((s) => {
      aiStatus.value = s;
      aiEnabled.value = s.enabled;
    }).catch(() => { /* AI 未配置时静默 */ }),
  ]);
  if (targetDb.value && targetDb.value !== CONTROL_PLANE_KEY) {
    await loadSchema(targetDb.value);
  }
});
</script>

<style scoped>
.meta { color: #888; font-size: 12px; }
.ai-response {
  font-size: 13px;
  white-space: pre-wrap;
  word-break: break-word;
  margin: 0;
  padding: 8px;
  background: #f8f8f8;
  border-radius: 4px;
  font-family: inherit;
  line-height: 1.6;
}
</style>
