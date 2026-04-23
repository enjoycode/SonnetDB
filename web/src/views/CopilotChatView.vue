<template>
  <n-space vertical :size="16">
    <n-card title="Copilot Chat" :bordered="false" class="chat-hero">
      <n-space vertical :size="14">
        <n-space align="center" justify="space-between" wrap>
          <n-space align="center" wrap :size="10">
            <n-tag :type="targetDb ? 'success' : 'warning'" size="small">
              {{ targetDb ? `当前数据库：${targetDb}` : '暂无可用数据库' }}
            </n-tag>
            <n-popselect
              v-if="databases.length > 1"
              v-model:value="targetDb"
              :options="dbOptions"
              trigger="click"
              size="small"
            >
              <n-button quaternary size="small">切换</n-button>
            </n-popselect>
            <n-button quaternary size="small" @click="reloadDbs">刷新</n-button>
          </n-space>

          <n-space align="center" wrap>
            <n-tag size="small" type="info">多轮历史</n-tag>
            <n-tag size="small" type="success">自动纠错 SQL</n-tag>
            <n-button quaternary size="small" :disabled="turns.length === 0" @click="clearConversation">
              清空对话
            </n-button>
            <n-button
              v-if="loading"
              quaternary
              type="error"
              size="small"
              @click="activeAbort?.abort()"
            >
              停止
            </n-button>
          </n-space>
        </n-space>

        <n-text depth="3">
          Copilot 会自动连接你当前可见的数据库（默认第一个），基于最近对话、技能库、文档召回与只读工具结果生成回答；若模型生成的 SQL 执行失败，会自动携带错误重写并重试。
        </n-text>

        <n-alert v-if="errorMsg" type="error" closable @close="errorMsg = ''">
          {{ errorMsg }}
        </n-alert>
      </n-space>
    </n-card>

    <n-card :bordered="false" content-style="padding: 0;">
      <div ref="transcriptRef" class="chat-transcript">
        <div v-if="turns.length === 0" class="chat-empty">
          <n-empty description="先问一个与当前数据库有关的问题，例如：最近 1 小时每台设备的平均温度是多少？" />
        </div>

        <div v-for="turn in turns" :key="turn.id" class="chat-turn">
          <div class="chat-bubble chat-bubble-user">
            <div class="bubble-caption">你</div>
            <div class="bubble-content">{{ turn.prompt }}</div>
          </div>

          <div class="chat-bubble chat-bubble-assistant">
            <div class="bubble-header">
              <span class="bubble-caption">Copilot</span>
              <n-tag :type="statusTagType(turn.status)" size="small">
                {{ statusLabel(turn.status) }}
              </n-tag>
            </div>

            <n-spin v-if="turn.status === 'streaming' && !turn.answer" size="small">
              <template #description>正在检索上下文并规划工具…</template>
            </n-spin>

            <pre v-if="turn.answer" class="assistant-answer">{{ turn.answer }}</pre>

            <n-alert v-if="turn.errorMessage" type="error" class="turn-error">
              {{ turn.errorMessage }}
            </n-alert>

            <div v-if="turn.sqlCandidates.length > 0" class="sql-actions">
              <div class="action-title">可直接落到 SQL Console 的 SQL</div>
              <div
                v-for="candidate in turn.sqlCandidates"
                :key="candidate.sql"
                class="sql-candidate"
              >
                <pre class="sql-preview">{{ candidate.sql }}</pre>
                <n-space align="center" justify="space-between" wrap>
                  <n-text depth="3">来源：{{ candidate.source }}</n-text>
                  <n-button type="primary" size="small" @click="openInSqlConsole(candidate.sql)">
                    在 SQL Console 执行
                  </n-button>
                </n-space>
              </div>
            </div>

            <n-collapse v-if="hasDetails(turn)" arrow-placement="right" class="turn-collapse">
              <n-collapse-item
                v-if="turn.skillNames.length > 0 || turn.toolNames.length > 0"
                title="命中技能"
                name="skills"
              >
                <n-space vertical :size="10">
                  <n-space v-if="turn.skillNames.length > 0" wrap>
                    <n-tag v-for="skill in turn.skillNames" :key="skill" type="success" size="small">
                      {{ skill }}
                    </n-tag>
                  </n-space>
                  <n-space v-if="turn.toolNames.length > 0" wrap>
                    <n-tag v-for="tool in turn.toolNames" :key="tool" size="small">
                      {{ tool }}
                    </n-tag>
                  </n-space>
                </n-space>
              </n-collapse-item>

              <n-collapse-item v-if="turn.citations.length > 0" title="Citations" name="citations">
                <div class="citation-list">
                  <div
                    v-for="citation in turn.citations"
                    :key="citation.id"
                    class="citation-item"
                  >
                    <div class="citation-head">
                      <n-tag size="small" :type="citationTagType(citation.kind)">{{ citation.id }}</n-tag>
                      <strong>{{ citation.title }}</strong>
                    </div>
                    <div class="citation-meta">{{ citation.kind }} · {{ citation.source }}</div>
                    <div class="citation-snippet">{{ citation.snippet }}</div>
                  </div>
                </div>
              </n-collapse-item>

              <n-collapse-item v-if="turn.toolTraces.length > 0 || turn.steps.length > 0" title="执行轨迹" name="trace">
                <div class="trace-list">
                  <div v-for="trace in turn.toolTraces" :key="trace.id" class="trace-item">
                    <div class="trace-head">
                      <strong>{{ trace.typeLabel }}</strong>
                      <n-tag v-if="trace.attempt" size="small" type="warning">第 {{ trace.attempt }} 轮</n-tag>
                    </div>
                    <div v-if="trace.message" class="trace-message">{{ trace.message }}</div>
                    <pre v-if="trace.arguments" class="trace-block">{{ trace.arguments }}</pre>
                    <pre v-if="trace.result" class="trace-block">{{ trace.result }}</pre>
                  </div>

                  <div v-if="turn.steps.length > 0" class="trace-steps">
                    <div v-for="(step, index) in turn.steps" :key="`${turn.id}-step-${index}`" class="trace-step">
                      {{ step }}
                    </div>
                  </div>
                </div>
              </n-collapse-item>
            </n-collapse>
          </div>
        </div>
      </div>
    </n-card>

    <n-card :bordered="false">
      <n-space vertical :size="12">
        <n-input
          v-model:value="composer"
          type="textarea"
          :autosize="{ minRows: 3, maxRows: 8 }"
          placeholder="例如：先看 cpu 表有哪些字段；如果有 usage 和 temp，再给我一条查询最近 10 分钟平均值的 SQL。"
          @keydown="handleComposerKeydown"
        />

        <n-space align="center" justify="space-between" wrap>
          <n-text depth="3">Enter 发送，Shift+Enter 换行。</n-text>
          <n-button
            type="primary"
            :loading="loading"
            :disabled="!composer.trim() || !targetDb"
            @click="sendMessage"
          >
            发送给 Copilot
          </n-button>
        </n-space>
      </n-space>
    </n-card>
  </n-space>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import {
  NAlert,
  NButton,
  NCard,
  NCollapse,
  NCollapseItem,
  NEmpty,
  NInput,
  NPopselect,
  NSpace,
  NSpin,
  NTag,
  NText,
  type SelectOption,
} from 'naive-ui';
import { useAuthStore } from '@/stores/auth';
import { listDatabases } from '@/api/server';
import {
  streamCopilotChat,
  type CopilotChatEvent,
  type CopilotCitation,
  type CopilotMessage,
} from '@/api/copilot';
import { useSqlConsoleStore } from '@/stores/sqlConsole';

interface SqlCandidate {
  sql: string;
  source: string;
}

interface ToolTrace {
  id: string;
  typeLabel: string;
  message: string;
  arguments?: string;
  result?: string;
  attempt?: number;
}

interface ChatTurn {
  id: string;
  prompt: string;
  answer: string;
  status: 'streaming' | 'done' | 'error';
  errorMessage: string;
  skillNames: string[];
  toolNames: string[];
  citations: CopilotCitation[];
  toolTraces: ToolTrace[];
  steps: string[];
  sqlCandidates: SqlCandidate[];
}

const auth = useAuthStore();
const router = useRouter();
const sqlConsole = useSqlConsoleStore();

const databases = ref<string[]>([]);
const targetDb = ref('');
const composer = ref('');
const loading = ref(false);
const errorMsg = ref('');
const turns = ref<ChatTurn[]>([]);
const transcriptRef = ref<HTMLElement | null>(null);
const activeAbort = ref<AbortController | null>(null);

const dbOptions = computed<SelectOption[]>(() => (
  databases.value.map((db) => ({ label: db, value: db }))
));

async function reloadDbs(): Promise<void> {
  const result = await listDatabases(auth.api);
  if (result.error) {
    errorMsg.value = result.error.message;
    return;
  }

  databases.value = result.databases;
  if (!targetDb.value || !databases.value.includes(targetDb.value)) {
    targetDb.value = databases.value[0] ?? '';
  }
}

function clearConversation(): void {
  turns.value = [];
  errorMsg.value = '';
}

watch(targetDb, (nextDb, previousDb) => {
  if (previousDb && nextDb && previousDb !== nextDb && turns.value.length > 0) {
    clearConversation();
  }
});

function buildMessages(nextPrompt: string): CopilotMessage[] {
  const messages: CopilotMessage[] = [];
  for (const turn of turns.value) {
    messages.push({ role: 'user', content: turn.prompt });
    if (turn.status === 'done' && turn.answer.trim()) {
      messages.push({ role: 'assistant', content: turn.answer });
    }
  }
  messages.push({ role: 'user', content: nextPrompt });
  return messages;
}

async function sendMessage(): Promise<void> {
  const prompt = composer.value.trim();
  if (!prompt || loading.value) return;
  if (!targetDb.value) {
    errorMsg.value = '请先选择一个数据库。';
    return;
  }

  errorMsg.value = '';
  composer.value = '';
  loading.value = true;
  const historyMessages = buildMessages(prompt);

  const turn: ChatTurn = {
    id: `${Date.now()}-${Math.random().toString(16).slice(2)}`,
    prompt,
    answer: '',
    status: 'streaming',
    errorMessage: '',
    skillNames: [],
    toolNames: [],
    citations: [],
    toolTraces: [],
    steps: [],
    sqlCandidates: [],
  };
  turns.value.push(turn);
  await nextTick();
  scrollToBottom();

  const controller = new AbortController();
  activeAbort.value = controller;

  try {
    for await (const event of streamCopilotChat(
      auth.state?.token ?? '',
      { db: targetDb.value, messages: historyMessages },
      controller.signal,
    )) {
      applyEvent(turn, event);
      await nextTick();
      scrollToBottom();
    }

    if (turn.status === 'streaming') {
      turn.status = 'done';
    }
  } catch (error: unknown) {
    turn.status = 'error';
    turn.errorMessage = controller.signal.aborted
      ? '已手动停止当前请求。'
      : (error instanceof Error ? error.message : String(error));
    errorMsg.value = turn.errorMessage;
  } finally {
    loading.value = false;
    activeAbort.value = null;
    await nextTick();
    scrollToBottom();
  }
}

function applyEvent(turn: ChatTurn, event: CopilotChatEvent): void {
  if (event.message) {
    turn.steps.push(event.message);
  }

  if (event.skillNames) {
    turn.skillNames = [...event.skillNames];
  }

  if (event.toolNames) {
    turn.toolNames = [...event.toolNames];
  }

  if (event.citations) {
    mergeCitations(turn, event.citations);
  }

  if (event.type === 'tool_call' || event.type === 'tool_retry' || event.type === 'tool_result') {
    turn.toolTraces.push({
      id: `${turn.id}-${turn.toolTraces.length}-${event.type}`,
      typeLabel: event.type === 'tool_call'
        ? '工具调用'
        : event.type === 'tool_retry'
          ? '自动重试'
          : '工具结果',
      message: event.message ?? '',
      arguments: event.toolArguments,
      result: event.toolResult,
      attempt: event.attempt,
    });
    collectSqlCandidatesFromArguments(turn, event.toolArguments, event.type === 'tool_retry' ? '自动改写' : '工具调用');
  }

  if (event.type === 'final') {
    turn.answer = event.answer?.trim() ?? '';
    turn.status = 'done';
    collectSqlCandidatesFromText(turn, turn.answer, '最终回答');
  }

  if (event.type === 'error') {
    turn.status = 'error';
    turn.errorMessage = event.message ?? 'Copilot 请求失败。';
  }

  if (event.type === 'done' && turn.status === 'streaming') {
    turn.status = 'done';
  }
}

function mergeCitations(turn: ChatTurn, citations: CopilotCitation[]): void {
  const seen = new Set(turn.citations.map((citation) => citation.id));
  for (const citation of citations) {
    if (!seen.has(citation.id)) {
      turn.citations.push(citation);
      seen.add(citation.id);
    }
  }
}

function collectSqlCandidatesFromArguments(turn: ChatTurn, toolArguments?: string, source = '工具调用'): void {
  if (!toolArguments) return;

  try {
    const parsed = JSON.parse(toolArguments) as { sql?: string };
    if (typeof parsed.sql === 'string') {
      addSqlCandidate(turn, parsed.sql, source);
    }
  } catch {
    // ignore malformed tool args
  }
}

function collectSqlCandidatesFromText(turn: ChatTurn, text: string, source: string): void {
  for (const candidate of extractSqlCandidates(text)) {
    addSqlCandidate(turn, candidate, source);
  }
}

function addSqlCandidate(turn: ChatTurn, sql: string, source: string): void {
  const normalized = sql.trim();
  if (!looksLikeSql(normalized)) return;

  const exists = turn.sqlCandidates.some((candidate) => (
    normalizeSql(candidate.sql) === normalizeSql(normalized)
  ));
  if (!exists) {
    turn.sqlCandidates.push({ sql: normalized, source });
  }
}

function extractSqlCandidates(text: string): string[] {
  if (!text.trim()) return [];

  const matches = new Set<string>();
  const fencedRegex = /```(?:sql)?\s*([\s\S]*?)```/gi;
  for (const match of text.matchAll(fencedRegex)) {
    const candidate = match[1]?.trim();
    if (candidate && looksLikeSql(candidate)) {
      matches.add(candidate);
    }
  }

  if (looksLikeSql(text.trim())) {
    matches.add(text.trim());
  }

  return Array.from(matches);
}

function looksLikeSql(text: string): boolean {
  return /^\s*(SELECT|SHOW|DESCRIBE)\b/i.test(text);
}

function normalizeSql(sql: string): string {
  return sql.replace(/\s+/g, ' ').trim().toLowerCase();
}

function openInSqlConsole(sql: string): void {
  sqlConsole.queueExecution({
    db: targetDb.value,
    sql,
    runImmediately: true,
  });
  void router.push({ name: 'sql' });
}

function statusTagType(status: ChatTurn['status']): 'default' | 'info' | 'success' | 'error' {
  switch (status) {
    case 'streaming': return 'info';
    case 'done': return 'success';
    case 'error': return 'error';
    default: return 'default';
  }
}

function statusLabel(status: ChatTurn['status']): string {
  switch (status) {
    case 'streaming': return '处理中';
    case 'done': return '已完成';
    case 'error': return '出错';
    default: return '未知';
  }
}

function citationTagType(kind: string): 'default' | 'info' | 'success' | 'warning' {
  switch (kind) {
    case 'doc': return 'info';
    case 'skill': return 'success';
    case 'tool': return 'warning';
    default: return 'default';
  }
}

function hasDetails(turn: ChatTurn): boolean {
  return turn.skillNames.length > 0
    || turn.toolNames.length > 0
    || turn.citations.length > 0
    || turn.toolTraces.length > 0
    || turn.steps.length > 0;
}

function handleComposerKeydown(event: KeyboardEvent): void {
  if (event.key === 'Enter' && !event.shiftKey) {
    event.preventDefault();
    void sendMessage();
  }
}

function scrollToBottom(): void {
  const element = transcriptRef.value;
  if (!element) return;
  element.scrollTop = element.scrollHeight;
}

onMounted(async () => {
  await reloadDbs();
});
</script>

<style scoped>
.chat-hero {
  background:
    radial-gradient(circle at top right, rgba(24, 160, 88, 0.1), transparent 32%),
    linear-gradient(135deg, rgba(13, 59, 102, 0.05), rgba(255, 255, 255, 0.96));
}

.field-label {
  color: var(--sndb-ink-soft);
  font-size: 0.94rem;
}

.chat-transcript {
  max-height: 68vh;
  overflow-y: auto;
  padding: 20px;
  background:
    linear-gradient(180deg, rgba(248, 251, 255, 0.94), rgba(237, 245, 249, 0.72));
}

.chat-empty {
  display: grid;
  place-items: center;
  min-height: 320px;
}

.chat-turn {
  display: flex;
  flex-direction: column;
  gap: 12px;
  margin-bottom: 22px;
}

.chat-bubble {
  max-width: min(860px, 100%);
  border-radius: 22px;
  padding: 16px 18px;
  box-shadow: var(--sndb-shadow);
}

.chat-bubble-user {
  align-self: flex-end;
  background: linear-gradient(135deg, #0d3b66, #155087);
  color: #fff;
}

.chat-bubble-assistant {
  align-self: flex-start;
  background: rgba(255, 255, 255, 0.96);
  border: 1px solid rgba(13, 59, 102, 0.08);
}

.bubble-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 8px;
}

.bubble-caption {
  font-size: 0.82rem;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
}

.bubble-content,
.assistant-answer,
.citation-snippet,
.trace-message,
.trace-step {
  white-space: pre-wrap;
  word-break: break-word;
  line-height: 1.7;
}

.assistant-answer {
  margin: 0;
  padding: 12px 14px;
  border-radius: 16px;
  background: rgba(13, 59, 102, 0.04);
  font-family: inherit;
}

.turn-error {
  margin-top: 12px;
}

.sql-actions {
  margin-top: 14px;
  padding-top: 14px;
  border-top: 1px solid rgba(13, 59, 102, 0.08);
}

.action-title {
  margin-bottom: 10px;
  color: var(--sndb-ink-soft);
  font-size: 0.9rem;
}

.sql-candidate {
  margin-bottom: 12px;
  padding: 12px;
  border-radius: 16px;
  background: rgba(24, 160, 88, 0.06);
}

.sql-preview,
.trace-block {
  margin: 0 0 10px;
  padding: 12px;
  border-radius: 14px;
  overflow-x: auto;
  white-space: pre-wrap;
  word-break: break-word;
  background: rgba(16, 38, 61, 0.05);
  color: var(--sndb-ink-strong);
  font-family: "SFMono-Regular", Consolas, "Liberation Mono", monospace;
  font-size: 0.88rem;
}

.turn-collapse {
  margin-top: 14px;
}

.citation-list,
.trace-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.citation-item,
.trace-item,
.trace-steps {
  padding: 12px 14px;
  border-radius: 16px;
  background: rgba(13, 59, 102, 0.04);
}

.citation-head,
.trace-head {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  margin-bottom: 6px;
}

.citation-meta {
  margin-bottom: 6px;
  color: var(--sndb-ink-soft);
  font-size: 0.82rem;
}

.trace-step + .trace-step {
  margin-top: 8px;
}

@media (max-width: 768px) {
  .chat-transcript {
    max-height: none;
  }

  .chat-bubble {
    max-width: 100%;
  }

  .bubble-header {
    align-items: flex-start;
    flex-direction: column;
  }
}
</style>
