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
          <n-text depth="3" style="font-size: 12px">
            提示：可输入 <code>USE &lt;db&gt;</code> 切换 / <code>SELECT current_database()</code> 查询当前库
          </n-text>
        </n-space>

        <SqlEditor
          v-model="sql"
          :schema="currentSchema"
          placeholder="SHOW DATABASES;"
        />

        <n-space>
          <n-button type="primary" :loading="running" @click="run">运行</n-button>
          <n-button @click="clear">清空</n-button>
          <span v-if="summary" class="meta">{{ summary }}</span>
        </n-space>

        <n-alert v-if="errorMsg" type="error" :title="errorMsg" closable @close="errorMsg = ''" />

        <n-space vertical :size="10" v-if="results.length > 0">
          <SqlResultPanel
            v-for="(item, idx) in results"
            :key="idx"
            :index="idx"
            :sql="item.sql"
            :result="item.result"
          />
        </n-space>
        <n-text v-else-if="ranOnce && !errorMsg" depth="3">语句已执行，没有结果集。</n-text>
      </n-space>
    </n-card>
  </n-space>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue';
import {
  NCard, NSpace, NButton, NSelect, NAlert, NText, NTag,
  type SelectOption,
} from 'naive-ui';
import { useAuthStore } from '@/stores/auth';
import {
  execControlPlaneSql, execDataSql, type SqlResultSet,
} from '@/api/sql';
import { splitSqlStatements } from '@/api/sqlSplit';
import {
  parseSqlMetaCommand,
  buildClientResultSet,
  buildClientErrorResultSet,
} from '@/api/sqlMeta';
import { listDatabases } from '@/api/server';
import { fetchSchema, type MeasurementInfo } from '@/api/schema';
import SqlEditor from '@/components/SqlEditor.vue';
import SqlResultPanel from '@/components/SqlResultPanel.vue';
import { useSqlConsoleStore } from '@/stores/sqlConsole';

interface ExecutedStatement {
  sql: string;
  result: SqlResultSet;
}

const auth = useAuthStore();
const sqlConsole = useSqlConsoleStore();

const CONTROL_PLANE_KEY = '__control_plane__';
const targetDb = ref<string>('');
const databases = ref<string[]>([]);
const sql = ref<string>('SHOW DATABASES');
const running = ref(false);
const errorMsg = ref('');
const ranOnce = ref(false);
const summary = ref('');
const results = ref<ExecutedStatement[]>([]);
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
  results.value = [];
  summary.value = '';
  if (!sql.value.trim()) return;
  if (!targetDb.value) {
    errorMsg.value = '当前没有可执行的数据面数据库。';
    return;
  }
  const statements = splitSqlStatements(sql.value);
  if (statements.length === 0) return;

  running.value = true;
  try {
    let okCount = 0;
    let failCount = 0;
    let totalElapsed = 0;
    const collected: ExecutedStatement[] = [];

    for (const stmt of statements) {
      // SQL Console 元命令：USE <db> / SHOW CURRENT_DATABASE / SELECT current_database() —
      // 完全在客户端处理，不走服务端，避免命中服务端 SqlParser 的「未知关键字」错误。
      const meta = parseSqlMetaCommand(stmt);
      const rs = meta
        ? executeMetaCommand(meta)
        : (targetDb.value === CONTROL_PLANE_KEY
          ? await execControlPlaneSql(auth.api, stmt)
          : await execDataSql(auth.api, targetDb.value, stmt));
      collected.push({ sql: stmt, result: rs });
      if (rs.error) {
        failCount += 1;
      } else {
        okCount += 1;
        if (rs.end) totalElapsed += rs.end.elapsedMs;
      }
      // 渐进展示：每条立刻插入，避免长批阻塞 UI。
      results.value = [...collected];
      // 任何一条失败即停止后续执行，避免基于失败结果继续跑。
      if (rs.error) break;
    }

    ranOnce.value = true;
    const parts = [
      `共 ${statements.length} 条`,
      `成功 ${okCount}`,
    ];
    if (failCount > 0) parts.push(`失败 ${failCount}`);
    parts.push(`合计 ${totalElapsed.toFixed(2)} ms`);
    summary.value = parts.join(' · ');
  } finally {
    running.value = false;
  }
}

/**
 * 客户端处理 SQL Console 元命令：USE / 查询当前数据库。
 * <p>
 * 当前 db 名约定：control-plane 显示为字面量 <c>system</c>；用户库显示为名字本身。
 * <c>USE system</c> 会切到控制面（仅 superuser 可用）。
 */
function executeMetaCommand(meta: ReturnType<typeof parseSqlMetaCommand>): SqlResultSet {
  if (!meta) return buildClientErrorResultSet('console_meta', '未识别的元命令。');

  const currentName = targetDb.value === CONTROL_PLANE_KEY ? 'system' : targetDb.value;

  if (meta.kind === 'current-database') {
    return buildClientResultSet(['current_database'], [[currentName]]);
  }

  // meta.kind === 'use'
  const wanted = meta.database;
  const isSystem = wanted === 'system' || wanted === '*';
  if (isSystem) {
    if (!auth.isSuperuser) {
      return buildClientErrorResultSet('forbidden', '仅 superuser 才能切换到系统数据库。');
    }
    targetDb.value = CONTROL_PLANE_KEY;
    return buildClientResultSet(['database'], [['system']]);
  }
  if (!databases.value.includes(wanted)) {
    return buildClientErrorResultSet(
      'database_not_found',
      `数据库 "${wanted}" 不存在或当前用户没有访问权限。可用列表：${databases.value.join(', ') || '(空)'}。`,
    );
  }
  targetDb.value = wanted;
  return buildClientResultSet(['database'], [[wanted]]);
}

function clear(): void {
  sql.value = '';
  results.value = [];
  errorMsg.value = '';
  summary.value = '';
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
