<template>
  <n-space vertical :size="16">
    <n-card title="SQL Console" :bordered="false">
      <n-tabs
        v-model:value="activeTabId"
        type="card"
        addable
        animated
        @add="createTab"
        @close="closeTab"
      >
        <n-tab-pane
          v-for="tab in sqlConsole.tabs"
          :key="tab.id"
          :name="tab.id"
          :tab="tab.title"
          :closable="sqlConsole.tabs.length > 1"
        >
          <n-space v-if="tab.id === activeTabId" vertical :size="12" class="sql-console-pane">
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
              <n-button quaternary @click="createTab">新建选项卡</n-button>
              <span v-if="summary" class="meta">{{ summary }}</span>
            </n-space>

            <n-alert v-if="errorMsg" type="error" :title="errorMsg" closable @close="errorMsg = ''" />

            <n-space vertical :size="10" v-if="results.length > 0">
              <SqlResultPanel
                v-for="(item, idx) in results"
                :key="item.id"
                :index="idx"
                :sql="item.sql"
                :result="item.result"
              />
            </n-space>
            <n-text v-else-if="ranOnce && !errorMsg" depth="3">语句已执行，没有结果集。</n-text>
          </n-space>
        </n-tab-pane>
      </n-tabs>
    </n-card>
  </n-space>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue';
import {
  NAlert,
  NButton,
  NCard,
  NSelect,
  NSpace,
  NTabPane,
  NTabs,
  NTag,
  NText,
  type SelectOption,
} from 'naive-ui';
import { useAuthStore } from '@/stores/auth';
import {
  execControlPlaneSql,
  execDataSql,
  type SqlResultSet,
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
import {
  CONTROL_PLANE_KEY,
  useSqlConsoleStore,
  type SqlConsoleExecutedStatement,
} from '@/stores/sqlConsole';

const auth = useAuthStore();
const sqlConsole = useSqlConsoleStore();

const databases = ref<string[]>([]);
const runningTabId = ref<string | null>(null);
const currentSchema = ref<MeasurementInfo[]>([]);

const activeTab = computed(() => sqlConsole.activeTab);
const activeTabId = computed({
  get: () => sqlConsole.activeTabId ?? '',
  set: (id: string) => sqlConsole.activateTab(id),
});

const targetDb = computed({
  get: () => activeTab.value?.db ?? '',
  set: (db: string) => sqlConsole.patchActiveTab({ db }),
});

const sql = computed({
  get: () => activeTab.value?.sql ?? '',
  set: (value: string) => sqlConsole.patchActiveTab({ sql: value }),
});

const results = computed(() => activeTab.value?.results ?? []);
const ranOnce = computed(() => activeTab.value?.ranOnce ?? false);
const summary = computed(() => activeTab.value?.summary ?? '');
const running = computed(() => runningTabId.value === activeTab.value?.id);
const errorMsg = computed({
  get: () => activeTab.value?.errorMsg ?? '',
  set: (value: string) => sqlConsole.patchActiveTab({ errorMsg: value }),
});

const dbOptions = computed<SelectOption[]>(() => {
  const options: SelectOption[] = auth.isSuperuser
    ? [{ label: 'system （系统库 / 控制面）', value: CONTROL_PLANE_KEY }]
    : [];
  return [
    ...options,
    ...databases.value.map((d) => ({ label: d, value: d })),
  ];
});

function makeStatementId(): string {
  return `stmt_${Date.now().toString(36)}_${Math.random().toString(36).slice(2, 8)}`;
}

function tabTitleFromSql(text: string, fallback = '查询'): string {
  const oneLine = text.replace(/\s+/g, ' ').trim();
  if (!oneLine) return fallback;
  return oneLine.length > 28 ? `${oneLine.slice(0, 25)}...` : oneLine;
}

function defaultDbForNewTab(): string {
  if (activeTab.value?.db) return activeTab.value.db;
  if (auth.isSuperuser) return CONTROL_PLANE_KEY;
  return databases.value[0] ?? '';
}

function defaultSqlForDb(db: string): string {
  return db === CONTROL_PLANE_KEY ? 'SHOW DATABASES' : 'SHOW MEASUREMENTS';
}

function createTab(): void {
  const db = defaultDbForNewTab();
  sqlConsole.createTab({
    title: `查询 ${sqlConsole.tabs.length + 1}`,
    db,
    sql: defaultSqlForDb(db),
  });
  void loadSchema(targetDb.value);
}

function closeTab(id: string): void {
  sqlConsole.closeTab(id);
  void loadSchema(targetDb.value);
}

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

watch(targetDb, (db) => loadSchema(db), { immediate: true });

async function run(): Promise<void> {
  const tab = activeTab.value;
  if (!tab) return;

  const tabId = tab.id;
  sqlConsole.patchTab(tabId, {
    errorMsg: '',
    results: [],
    summary: '',
    ranOnce: false,
    title: tabTitleFromSql(sql.value, tab.title),
  });

  if (!sql.value.trim()) return;
  if (!targetDb.value) {
    sqlConsole.patchTab(tabId, { errorMsg: '当前没有可执行的数据面数据库。' });
    return;
  }

  const statements = splitSqlStatements(sql.value);
  if (statements.length === 0) return;

  runningTabId.value = tabId;
  try {
    let okCount = 0;
    let failCount = 0;
    let totalElapsed = 0;
    const collected: SqlConsoleExecutedStatement[] = [];

    for (const stmt of statements) {
      const meta = parseSqlMetaCommand(stmt);
      const rs = meta
        ? await executeMetaCommand(meta)
        : (targetDb.value === CONTROL_PLANE_KEY
          ? await execControlPlaneSql(auth.api, stmt)
          : await execDataSql(auth.api, targetDb.value, stmt));

      collected.push({
        id: makeStatementId(),
        sql: stmt,
        result: rs,
        createdAt: Date.now(),
        source: meta ? 'meta' : 'manual',
      });

      if (rs.error) {
        failCount += 1;
      } else {
        okCount += 1;
        if (rs.end) totalElapsed += rs.end.elapsedMs;
        if (!meta && /^\s*(create|drop)\s+database\b/i.test(stmt)) {
          await reloadDbs();
        }
      }

      sqlConsole.setTabResults(tabId, [...collected], '', '', true);
      if (rs.error) break;
    }

    const parts = [
      `共 ${statements.length} 条`,
      `成功 ${okCount}`,
    ];
    if (failCount > 0) parts.push(`失败 ${failCount}`);
    parts.push(`合计 ${totalElapsed.toFixed(2)} ms`);
    sqlConsole.setTabResults(tabId, collected, parts.join(' · '), '', true);
  } finally {
    if (runningTabId.value === tabId) runningTabId.value = null;
  }
}

/**
 * 客户端处理 SQL Console 元命令：USE / 查询当前数据库。
 * 当前 db 名约定：control-plane 显示为 system；用户库显示为名字本身。
 */
async function executeMetaCommand(meta: ReturnType<typeof parseSqlMetaCommand>): Promise<SqlResultSet> {
  if (!meta) return buildClientErrorResultSet('console_meta', '未识别的元命令。');

  const currentName = targetDb.value === CONTROL_PLANE_KEY ? 'system' : targetDb.value;

  if (meta.kind === 'current-database') {
    return buildClientResultSet(['current_database'], [[currentName]]);
  }

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
    await reloadDbs();
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
  sqlConsole.clearActiveTab();
}

async function applyPendingExecution(): Promise<void> {
  const pending = sqlConsole.consumeExecution();
  if (!pending) return;

  if (pending.tabId) {
    sqlConsole.activateTab(pending.tabId);
  }
  targetDb.value = pending.db;
  sql.value = pending.sql;
  await loadSchema(pending.db);
  await nextTick();

  if (pending.runImmediately) {
    await run();
  }
}

watch(() => sqlConsole.pendingExecution, () => {
  if (sqlConsole.pendingExecution) {
    void applyPendingExecution();
  }
}, { deep: true });

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
.sql-console-pane { padding-top: 12px; }
</style>
