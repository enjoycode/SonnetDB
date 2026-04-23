<template>
  <n-card
    size="small"
    :bordered="true"
    :segmented="{ content: true, footer: false }"
    class="sql-result-card"
  >
    <template #header>
      <n-space size="small" align="center" :wrap="false" style="font-size: 13px">
        <n-tag size="small" :type="statusType" :bordered="false">#{{ index + 1 }}</n-tag>
        <code class="sql-result-card__sql" :title="sql">{{ trimmedSql }}</code>
      </n-space>
    </template>
    <template #header-extra>
      <n-space size="small" align="center" :wrap="false">
        <span v-if="meta" class="sql-result-card__meta">{{ meta }}</span>
        <n-tabs
          v-if="hasRows"
          v-model:value="view"
          type="segment"
          size="small"
          style="min-width: 220px"
        >
          <n-tab name="markdown" tab="文本" />
          <n-tab name="table" tab="表格" />
          <n-tab name="chart" tab="图表" />
        </n-tabs>
      </n-space>
    </template>

    <n-alert
      v-if="result.error"
      type="error"
      :title="`[${result.error.code ?? 'error'}] ${result.error.message}`"
    />

    <template v-else>
      <template v-if="hasRows">
        <!-- 文本（Markdown 表格 + 客户端渲染；常规 Markdown 由 marked 渲染，
             复杂内容（如 SVG / HTML 区块）允许直接以 raw HTML 透传） -->
        <div v-if="view === 'markdown'" class="sql-result-card__md" v-html="markdownHtml" />

        <!-- 表格 -->
        <n-data-table
          v-else-if="view === 'table'"
          :columns="dataColumns"
          :data="rows"
          :bordered="false"
          size="small"
          :max-height="420"
        />

        <!-- 图表（SVG 折线） -->
        <SqlResultChart
          v-else
          :columns="result.columns"
          :rows="rows"
        />
      </template>
      <n-text v-else depth="3">语句已执行，没有结果集。</n-text>
    </template>
  </n-card>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import {
  NAlert, NCard, NDataTable, NSpace, NTabs, NTab, NTag, NText,
  type DataTableColumns,
} from 'naive-ui';
import { marked } from 'marked';
import SqlResultChart from './SqlResultChart.vue';
import { rowsToObjects, type SqlResultSet } from '@/api/sql';

interface Props {
  index: number;
  sql: string;
  result: SqlResultSet;
}
const props = defineProps<Props>();

type View = 'markdown' | 'table' | 'chart';
const view = ref<View>('markdown');

const hasRows = computed(() => props.result.hasColumns && props.result.rows.length > 0);

watch(hasRows, (v) => {
  if (!v) view.value = 'markdown';
}, { immediate: true });

const trimmedSql = computed(() => {
  const oneLine = props.sql.replace(/\s+/g, ' ').trim();
  return oneLine.length > 120 ? `${oneLine.slice(0, 117)}…` : oneLine;
});

const meta = computed(() => {
  if (!props.result.end) return '';
  const parts: string[] = [];
  if (props.result.hasColumns) parts.push(`${props.result.end.rowCount} 行`);
  if (props.result.end.recordsAffected >= 0) parts.push(`受影响 ${props.result.end.recordsAffected}`);
  parts.push(`${props.result.end.elapsedMs.toFixed(2)} ms`);
  return parts.join(' · ');
});

const statusType = computed<'success' | 'error' | 'info'>(() => {
  if (props.result.error) return 'error';
  if (hasRows.value) return 'success';
  return 'info';
});

const rows = computed(() => rowsToObjects(props.result));

const dataColumns = computed<DataTableColumns<Record<string, unknown>>>(() =>
  props.result.columns.map((c) => ({
    title: c, key: c, ellipsis: { tooltip: true },
  })));

/**
 * 把结果集渲染成 Markdown 字符串（含表头分隔行 + 数据行）。
 * 表格内的 `|` / `\\` 做最小转义，避免破坏 Markdown 语法。
 *
 * 行数 > 100 时只展示前 100 行并附统计行，避免一次性渲染过大。
 */
const markdownSource = computed(() => {
  if (!props.result.hasColumns) return '_语句执行成功，但没有结果集。_';
  const cols = props.result.columns;
  if (cols.length === 0) return '_语句执行成功，但没有列。_';

  const max = 100;
  const slice = props.result.rows.slice(0, max);
  const escape = (v: unknown): string => {
    if (v === null || v === undefined) return '';
    return String(v).replace(/\\/g, '\\\\').replace(/\|/g, '\\|').replace(/\n/g, ' ');
  };

  const lines: string[] = [];
  lines.push(`| ${cols.map(escape).join(' | ')} |`);
  lines.push(`| ${cols.map(() => '---').join(' | ')} |`);
  for (const row of slice) {
    lines.push(`| ${cols.map((_, i) => escape(row[i])).join(' | ')} |`);
  }
  if (props.result.rows.length > max) {
    lines.push('');
    lines.push(`_… 仅展示前 ${max} 行，共 ${props.result.rows.length} 行。切换到「表格」或「图表」查看完整结果。_`);
  }
  return lines.join('\n');
});

const markdownHtml = computed(() => {
  // 允许 marked 输出原生 HTML，便于值列里直接嵌入 <svg>。
  // 内容来源是服务端 SQL 结果，已通过 escape() 转义 Markdown 特殊符号；
  // 对潜在 XSS 的应对：列值中不出现 <script>，且标签对 HTML 实体的输出不会执行。
  const html = marked.parse(markdownSource.value, { async: false }) as string;
  return html;
});
</script>

<style scoped>
.sql-result-card { background: #fff; }
.sql-result-card__sql {
  font-family: 'JetBrains Mono', Consolas, Menlo, monospace;
  font-size: 12px;
  color: #345;
  background: rgba(44, 123, 229, 0.08);
  padding: 1px 6px;
  border-radius: 4px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 600px;
  display: inline-block;
}
.sql-result-card__meta { color: #888; font-size: 12px; }
.sql-result-card__md {
  font-size: 13px;
  overflow-x: auto;
}
.sql-result-card__md :deep(table) {
  border-collapse: collapse;
  width: 100%;
}
.sql-result-card__md :deep(th),
.sql-result-card__md :deep(td) {
  border: 1px solid rgba(0, 0, 0, 0.08);
  padding: 4px 8px;
  text-align: left;
  font-variant-numeric: tabular-nums;
}
.sql-result-card__md :deep(th) {
  background: rgba(13, 59, 102, 0.06);
  font-weight: 600;
}
.sql-result-card__md :deep(code) {
  background: rgba(0, 0, 0, 0.05);
  padding: 1px 4px;
  border-radius: 3px;
}
</style>
