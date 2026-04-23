<template>
  <!-- 折叠态：右下角圆形按钮 -->
  <div v-if="!visible" class="copilot-fab" @click="open" title="打开 Copilot 助手">
    <span class="copilot-fab-icon">AI</span>
  </div>

  <!-- 展开态：浮窗面板 -->
  <div
    v-else
    class="copilot-dock"
    :class="{ 'is-fullscreen': fullscreen }"
    :style="dockStyle"
  >
    <header class="copilot-dock__header" @mousedown="onDragStart">
      <div class="copilot-dock__title">
        <span class="copilot-dock__avatar">AI</span>
        <strong>Copilot</strong>
        <n-tag size="tiny" :type="status?.embeddingFallback ? 'warning' : 'success'">
          {{ status ? `${status.indexedFiles} 文档 · ${status.indexedChunks} 块` : '加载中…' }}
        </n-tag>
      </div>
      <n-space size="small" :wrap="false">
        <n-popover trigger="click" placement="bottom-end" :width="300" :show-arrow="false">
          <template #trigger>
            <n-button text size="tiny" title="会话历史">≡</n-button>
          </template>
          <div class="copilot-dock__sessions" @mousedown.stop>
            <div class="copilot-dock__sessions-head">
              <n-text strong style="font-size: 12px">最近会话</n-text>
              <n-space size="small">
                <n-button size="tiny" type="primary" @click="onNewSession">+ 新会话</n-button>
                <n-popconfirm @positive-click="onClearAll">
                  <template #trigger>
                    <n-button size="tiny" quaternary type="error" :disabled="sessions.recent.length === 0">清空</n-button>
                  </template>
                  确认清空全部本地会话历史？此操作不可恢复。
                </n-popconfirm>
              </n-space>
            </div>
            <div v-if="sessions.recent.length === 0" class="copilot-dock__sessions-empty">
              暂无会话，发送第一条消息即可创建。
            </div>
            <ul v-else class="copilot-dock__sessions-list">
              <li
                v-for="s in sessions.recent"
                :key="s.id"
                :class="{ 'is-active': s.id === sessions.currentId }"
                @click="onSwitchSession(s.id)"
              >
                <div class="copilot-dock__sessions-item-main">
                  <div class="copilot-dock__sessions-item-title" :title="s.title">{{ s.title }}</div>
                  <div class="copilot-dock__sessions-item-meta">
                    {{ s.db || '(无数据库)' }} · {{ Math.floor(s.messages.length / 2) }} 轮 · {{ formatRelative(s.updatedAt) }}
                  </div>
                </div>
                <n-space size="small" :wrap="false" class="copilot-dock__sessions-item-actions">
                  <n-button quaternary size="tiny" @click.stop="onRenameSession(s)" title="重命名">✎</n-button>
                  <n-button quaternary size="tiny" type="error" @click.stop="onRemoveSession(s.id)" title="删除">×</n-button>
                </n-space>
              </li>
            </ul>
          </div>
        </n-popover>
        <n-button text size="tiny" @click="reloadStatus" title="刷新知识库状态">↻</n-button>
        <n-button text size="tiny" @click="fullscreen = !fullscreen" :title="fullscreen ? '还原' : '全屏'">{{ fullscreen ? '⊟' : '⊕' }}</n-button>
        <n-button text size="tiny" @click="close" title="收起到角标">×</n-button>
      </n-space>
    </header>

    <!-- 知识库状态卡片 -->
    <section v-if="status" class="copilot-dock__kb">
      <div class="copilot-dock__kb-row">
        <span class="copilot-dock__kb-label">Embedding</span>
        <n-tag size="tiny" :type="status.embeddingFallback ? 'warning' : 'info'">
          {{ status.embeddingProvider }}{{ status.embeddingFallback ? ' (降级)' : '' }} · {{ status.vectorDimension }}D
        </n-tag>
      </div>
      <div class="copilot-dock__kb-row">
        <span class="copilot-dock__kb-label">最近摄入</span>
        <span class="copilot-dock__kb-value">{{ status.lastIngestedUtc ? formatTime(status.lastIngestedUtc) : '从未' }}</span>
      </div>
      <div class="copilot-dock__kb-row">
        <span class="copilot-dock__kb-label">技能</span>
        <span class="copilot-dock__kb-value">{{ status.skillCount }} 条</span>
        <n-button
          v-if="auth.isSuperuser"
          size="tiny"
          quaternary
          :loading="reindexing"
          style="margin-left: auto"
          @click="onReindex"
        >立即重建</n-button>
      </div>
    </section>

    <!-- 数据库选择 -->
    <section class="copilot-dock__db">
      <n-select
        size="small"
        v-model:value="selectedDb"
        :options="dbOptions"
        placeholder="选择数据库（用于工具调用）"
        :disabled="dbs.length === 0"
      />
    </section>

    <!-- M8: 模型选择器 -->
    <section class="copilot-dock__model">
      <n-select
        size="small"
        v-model:value="selectedModel"
        :options="modelOptions"
        placeholder="使用服务端默认模型"
        clearable
        filterable
        tag
        :show="modelOptions.length > 0 || undefined"
        :title="modelDefault ? `服务端默认：${modelDefault}` : '未配置默认模型'"
      >
        <template #empty>
          {{ modelDefault ? `默认：${modelDefault}` : '可输入模型名（如 gpt-4o-mini、qwen2.5-coder-32b）' }}
        </template>
      </n-select>
    </section>

    <!-- M7: 权限模式选择 -->
    <section class="copilot-dock__perm">
      <n-popconfirm
        v-if="permissionMode === 'read-only'"
        positive-text="启用读写"
        negative-text="保持只读"
        @positive-click="permissionMode = 'read-write'"
      >
        <template #trigger>
          <n-tag
            size="tiny"
            type="success"
            :bordered="false"
            style="cursor: pointer"
            title="点击切换为读写模式"
          >🔒 只读模式</n-tag>
        </template>
        切换为读写模式后，Copilot 可直接执行 INSERT / DELETE / CREATE MEASUREMENT 等写入语句。是否启用？
      </n-popconfirm>
      <n-tag
        v-else
        size="tiny"
        type="warning"
        :bordered="false"
        closable
        style="cursor: pointer"
        title="点击 × 切换回只读"
        @close="permissionMode = 'read-only'"
      >⚠️ 读写模式</n-tag>
      <n-text depth="3" style="font-size: 11px; margin-left: 6px">
        {{ permissionMode === 'read-only' ? 'Copilot 只能查询' : '可执行写入（仍受凭据权限约束）' }}
      </n-text>
    </section>

    <!-- M6: 页面上下文 -->
    <section v-if="pageContextSummary" class="copilot-dock__ctx">
      <n-tag
        size="tiny"
        :type="contextEnabled ? 'info' : 'default'"
        :bordered="false"
        closable
        @close="contextEnabled = false"
      >
        📍 {{ pageContextSummary }}
      </n-tag>
      <n-button
        v-if="!contextEnabled"
        size="tiny"
        text
        type="primary"
        style="margin-left: 6px"
        @click="contextEnabled = true"
      >启用</n-button>
    </section>

    <!-- 消息流 -->
    <section class="copilot-dock__messages" ref="msgContainer">
      <div v-if="messages.length === 0 && !running" class="copilot-dock__empty">
        <p class="copilot-dock__empty-tip">问点什么？点击下方模板可直接填入：</p>
        <div class="copilot-dock__starters">
          <button
            v-for="s in starters"
            :key="s.title"
            class="copilot-dock__starter"
            :title="s.description"
            @click="onStarterClick(s)"
          >
            <span class="copilot-dock__starter-cat">{{ s.category }}</span>
            <span class="copilot-dock__starter-title">{{ s.title }}</span>
          </button>
        </div>
      </div>
      <div v-for="(msg, idx) in messages" :key="idx" class="copilot-dock__msg" :class="`copilot-dock__msg--${msg.role}`">
        <div class="copilot-dock__msg-role">{{ msg.role === 'user' ? '我' : 'Copilot' }}</div>
        <div class="copilot-dock__msg-body">{{ msg.content }}</div>
      </div>
      <div v-if="streamBuffer" class="copilot-dock__msg copilot-dock__msg--assistant">
        <div class="copilot-dock__msg-role">Copilot</div>
        <div class="copilot-dock__msg-body">{{ streamBuffer }}<span class="copilot-dock__caret" /></div>
      </div>
      <div v-if="errorMsg" class="copilot-dock__error">{{ errorMsg }}</div>
    </section>

    <!-- 输入框 -->
    <footer class="copilot-dock__input">
      <n-input
        v-model:value="prompt"
        type="textarea"
        :autosize="{ minRows: 2, maxRows: 5 }"
        placeholder="向 Copilot 提问，回车发送（Shift+Enter 换行）"
        :disabled="running"
        @keydown="onKeydown"
      />
      <n-space size="small" justify="space-between" style="margin-top: 6px">
        <n-text depth="3" style="font-size: 11px">
          按 Enter 发送
        </n-text>
        <n-space size="small" :wrap="false">
          <n-button v-if="running" size="tiny" type="error" ghost @click="stop">停止</n-button>
          <n-button size="tiny" type="primary" :disabled="!prompt.trim() || running" @click="send">发送</n-button>
        </n-space>
      </n-space>
    </footer>
  </div>
</template>

<script setup lang="ts">
import { computed, h, nextTick, onMounted, ref, watch } from 'vue';
import { useRoute } from 'vue-router';
import {
  NButton, NInput, NPopconfirm, NPopover, NSelect, NSpace, NTag, NText,
  type SelectOption, useDialog, useMessage,
} from 'naive-ui';
import { useAuthStore } from '@/stores/auth';
import { useCopilotSessionsStore, type CopilotSession } from '@/stores/copilotSessions';
import { useSqlConsoleStore } from '@/stores/sqlConsole';
import { listDatabases } from '@/api/server';
import {
  streamCopilotChat,
  fetchCopilotKnowledgeStatus,
  triggerCopilotDocsIngest,
  fetchCopilotModels,
  type CopilotKnowledgeStatus,
  type CopilotMessage,
} from '@/api/copilot';
import { pickStarters, type CopilotStarter } from '@/copilot/starters';

const auth = useAuthStore();
const sessions = useCopilotSessionsStore();
const sqlConsole = useSqlConsoleStore();
const route = useRoute();
const message = useMessage();
const dialog = useDialog();

const visible = ref(false);
const fullscreen = ref(false);
const status = ref<CopilotKnowledgeStatus | null>(null);
const reindexing = ref(false);

const dbs = ref<string[]>([]);
const selectedDb = ref<string>('');
const dbOptions = computed<SelectOption[]>(() => dbs.value.map((d) => ({ label: d, value: d })));

const prompt = ref('');
/** 来自当前会话的历史消息（只读引用，写入通过 sessions store）。 */
const messages = computed<CopilotMessage[]>(() => sessions.current?.messages ?? []);
const streamBuffer = ref('');
const running = ref(false);
const errorMsg = ref('');
const abort = ref<AbortController | null>(null);

const msgContainer = ref<HTMLElement | null>(null);

// 浮窗位置（可拖拽）
const dockPos = ref({ right: 24, bottom: 24 });
const dockStyle = computed(() => fullscreen.value
  ? { right: '0', bottom: '0', top: '0', left: '0', width: 'auto', height: 'auto' }
  : { right: `${dockPos.value.right}px`, bottom: `${dockPos.value.bottom}px` });

// === M6: 页面上下文 ===
const contextEnabled = ref(true);

// === M7: 权限模式 ===
type PermissionMode = 'read-only' | 'read-write';
const PERM_STORAGE_KEY = 'sndb.copilot.permission.v1';
const permissionMode = ref<PermissionMode>('read-only');
try {
  const saved = localStorage.getItem(PERM_STORAGE_KEY);
  if (saved === 'read-write') permissionMode.value = 'read-write';
} catch {
  // 忽略 localStorage 不可用
}
watch(permissionMode, (mode) => {
  try {
    localStorage.setItem(PERM_STORAGE_KEY, mode);
  } catch {
    // 忽略
  }
});

// === M8: 模型选择器 ===
const MODEL_STORAGE_KEY = 'sndb.copilot.model.v1';
const selectedModel = ref<string>('');
const modelDefault = ref<string>('');
const modelCandidates = ref<string[]>([]);
try {
  const saved = localStorage.getItem(MODEL_STORAGE_KEY);
  if (saved) selectedModel.value = saved;
} catch {
  // 忽略
}
watch(selectedModel, (m) => {
  try {
    localStorage.setItem(MODEL_STORAGE_KEY, m ?? '');
  } catch {
    // 忽略
  }
});
const modelOptions = computed<SelectOption[]>(() => {
  const set = new Set<string>();
  const out: SelectOption[] = [];
  const push = (label: string, value: string, suffix = '') => {
    if (!value || set.has(value)) return;
    set.add(value);
    out.push({ label: label + suffix, value });
  };
  if (modelDefault.value) push(modelDefault.value, modelDefault.value, '（默认）');
  for (const c of modelCandidates.value) push(c, c);
  if (selectedModel.value) push(selectedModel.value, selectedModel.value);
  return out;
});
async function loadModels() {
  if (!auth.state?.token) return;
  try {
    const m = await fetchCopilotModels(auth.state.token);
    modelDefault.value = m.default ?? '';
    modelCandidates.value = m.candidates ?? [];
    if (!selectedModel.value && modelDefault.value) {
      // 没有用户选择时，UI 显示默认模型但不写入 localStorage（保持空 = 走服务端默认）。
    }
  } catch {
    // 忽略：模型列表不可用时退化为自由输入。
  }
}

const ROUTE_LABELS: Record<string, string> = {
  dashboard: '概览',
  sql: 'SQL Console',
  chat: 'Copilot Chat',
  databases: '数据库管理',
  events: '事件流',
  users: '用户',
  grants: '权限',
  tokens: 'Token',
  'ai-settings': 'Copilot 设置',
  home: '产品首页',
};

interface PageContext {
  routeKey: string;
  routeLabel: string;
  routePath: string;
  sql: string;
  sqlDb: string;
}

const pageContext = computed<PageContext | null>(() => {
  const key = (route.name as string | undefined) ?? '';
  if (!key) return null;
  return {
    routeKey: key,
    routeLabel: ROUTE_LABELS[key] ?? key,
    routePath: route.path,
    sql: sqlConsole.currentSql.trim(),
    sqlDb: sqlConsole.currentDb,
  };
});

const pageContextSummary = computed<string>(() => {
  const ctx = pageContext.value;
  if (!ctx) return '';
  const parts: string[] = [`当前页面：${ctx.routeLabel}`];
  if (ctx.routeKey === 'sql' && ctx.sql.length > 0) {
    parts.push(`SQL ${ctx.sql.length} 字符`);
  }
  if (ctx.sqlDb && ctx.sqlDb !== selectedDb.value) {
    parts.push(`db=${ctx.sqlDb}`);
  }
  return parts.join(' · ');
});

/** 把页面上下文构造成一条 system message（仅在 send 时临时注入，不进入会话历史）。 */
function buildContextMessage(): CopilotMessage | null {
  if (!contextEnabled.value) return null;
  const ctx = pageContext.value;
  if (!ctx) return null;
  const lines: string[] = [
    '[页面上下文 / Page Context]',
    `用户当前所在页面：${ctx.routeLabel}（路由：${ctx.routePath}）。`,
  ];
  if (ctx.routeKey === 'sql') {
    if (ctx.sqlDb) lines.push(`SQL Console 当前选中的数据库：${ctx.sqlDb}。`);
    if (ctx.sql) {
      // 截断超长 SQL 避免超过 token 预算
      const snippet = ctx.sql.length > 2000 ? `${ctx.sql.slice(0, 2000)}\n…(已截断 ${ctx.sql.length - 2000} 字符)` : ctx.sql;
      lines.push('用户正在编辑的 SQL：');
      lines.push('```sql');
      lines.push(snippet);
      lines.push('```');
    } else {
      lines.push('SQL Console 编辑器当前为空。');
    }
    lines.push('如果用户提问与「这条 SQL / 当前查询 / 报错」相关，请优先围绕上面这段 SQL 回答。');
  } else if (ctx.routeKey === 'databases') {
    lines.push('用户正在查看「数据库管理」页面。如果用户问到 measurement / schema 信息，可调用工具列出当前选中数据库的 measurement。');
  } else if (ctx.routeKey === 'events') {
    lines.push('用户正在查看「事件流」页面（实时 SSE 事件）。');
  } else if (ctx.routeKey === 'ai-settings') {
    lines.push('用户正在「Copilot 设置」页面，可能在排查 API Key / 模型配置 / 知识库索引相关问题。');
  }
  return { role: 'system', content: lines.join('\n') };
}

function open(): void {
  visible.value = true;
  if (status.value === null) {
    void reloadStatus();
    void reloadDbs();
    void loadModels();
  }
}

function close(): void {
  visible.value = false;
  fullscreen.value = false;
}

async function reloadStatus(): Promise<void> {
  if (!auth.state?.token) return;
  try {
    status.value = await fetchCopilotKnowledgeStatus(auth.state.token);
  } catch (e) {
    errorMsg.value = e instanceof Error ? e.message : String(e);
  }
}

async function reloadDbs(): Promise<void> {
  try {
    const result = await listDatabases(auth.api);
    dbs.value = result.databases;
    if (!selectedDb.value && dbs.value.length > 0) {
      selectedDb.value = dbs.value[0];
    }
  } catch {
    // ignore — user 可能无 list 权限
  }
}

async function onReindex(): Promise<void> {
  if (!auth.state?.token) return;
  reindexing.value = true;
  try {
    await triggerCopilotDocsIngest(auth.state.token, true);
    message.success('知识库索引已触发，请稍后刷新查看');
    await reloadStatus();
  } catch (e) {
    message.error(e instanceof Error ? e.message : String(e));
  } finally {
    reindexing.value = false;
  }
}

const starters = computed<CopilotStarter[]>(() => pickStarters(pageContext.value?.routeKey ?? null, 6));

function onStarterClick(s: CopilotStarter): void {
  prompt.value = s.prompt;
}

function onKeydown(e: KeyboardEvent): void {
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault();
    void send();
  }
}

async function send(): Promise<void> {
  if (!prompt.value.trim() || running.value) return;
  if (!selectedDb.value) {
    errorMsg.value = '请先选择一个数据库（Copilot 的工具调用以数据库为单位）';
    return;
  }
  if (!auth.state?.token) return;

  // 没有当前会话则先建一个；切换数据库时同步到当前会话。
  if (!sessions.current) {
    sessions.create(selectedDb.value);
  } else if (sessions.current.db !== selectedDb.value) {
    sessions.current.db = selectedDb.value;
  }
  const sessionId = sessions.currentId;

  const userText = prompt.value.trim();
  const userMsg: CopilotMessage = { role: 'user', content: userText };
  prompt.value = '';
  errorMsg.value = '';
  // 把 user 消息立即加到当前会话（先用临时 assistant 占位，最后替换）。
  sessions.current!.messages.push(userMsg);
  streamBuffer.value = '';
  running.value = true;
  await scrollToBottom();

  const ac = new AbortController();
  abort.value = ac;

  // M6: 构造请求载荷 = [可选 system 上下文] + 会话历史
  const ctxMsg = buildContextMessage();
  const requestMessages: CopilotMessage[] = ctxMsg
    ? [ctxMsg, ...sessions.current!.messages]
    : sessions.current!.messages;

  const stepLog: string[] = [];
  let finalAnswer = '';
  try {
    for await (const event of streamCopilotChat(
      auth.state.token,
      {
        db: selectedDb.value,
        messages: requestMessages,
        mode: permissionMode.value,
        ...(selectedModel.value ? { model: selectedModel.value } : {}),
      },
      ac.signal,
    )) {
      if (ac.signal.aborted) break;
      if (event.type === 'final' && event.answer) {
        finalAnswer = event.answer;
        streamBuffer.value = event.answer;
      } else if (event.type === 'error') {
        errorMsg.value = event.message ?? 'Copilot 请求失败';
      } else if (event.message) {
        stepLog.push(event.message);
        // 仅当尚无 final 时显示进度
        if (!finalAnswer) streamBuffer.value = stepLog.slice(-3).join('\n');
      }
      await scrollToBottom();
    }
    if (finalAnswer) {
      // 把 assistant 最终回答写回会话；store 的 watch 会持久化。
      sessions.current!.messages.push({ role: 'assistant', content: finalAnswer });
      sessions.current!.updatedAt = Date.now();
      if (sessions.current!.title === '新会话') {
        // 用 setMessages 触发标题派生
        sessions.setMessages(sessionId!, sessions.current!.messages);
      }
      streamBuffer.value = '';
    }
  } catch (e: unknown) {
    if (!ac.signal.aborted) {
      errorMsg.value = e instanceof Error ? e.message : String(e);
    }
  } finally {
    running.value = false;
    abort.value = null;
    await scrollToBottom();
  }
}

function stop(): void {
  abort.value?.abort();
}

// === 会话历史（M5）===
function onNewSession(): void {
  sessions.create(selectedDb.value);
  streamBuffer.value = '';
  errorMsg.value = '';
}

function onSwitchSession(id: string): void {
  sessions.switchTo(id);
  streamBuffer.value = '';
  errorMsg.value = '';
  if (sessions.current?.db) {
    selectedDb.value = sessions.current.db;
  }
}

function onRemoveSession(id: string): void {
  sessions.remove(id);
}

function onClearAll(): void {
  sessions.clearAll();
}

function onRenameSession(s: CopilotSession): void {
  const inputRef = ref(s.title);
  dialog.create({
    title: '重命名会话',
    content: () => h(NInput, { value: inputRef.value, 'onUpdate:value': (v: string) => { inputRef.value = v; } }),
    positiveText: '保存',
    negativeText: '取消',
    onPositiveClick: () => {
      sessions.rename(s.id, inputRef.value);
    },
  });
}

function formatRelative(ts: number): string {
  const diff = Date.now() - ts;
  if (diff < 60_000) return '刚刚';
  if (diff < 3600_000) return `${Math.floor(diff / 60_000)} 分钟前`;
  if (diff < 86_400_000) return `${Math.floor(diff / 3600_000)} 小时前`;
  if (diff < 7 * 86_400_000) return `${Math.floor(diff / 86_400_000)} 天前`;
  try { return new Date(ts).toLocaleDateString(); } catch { return ''; }
}

async function scrollToBottom(): Promise<void> {
  await nextTick();
  const el = msgContainer.value;
  if (el) el.scrollTop = el.scrollHeight;
}

function formatTime(iso: string): string {
  try {
    return new Date(iso).toLocaleString();
  } catch {
    return iso;
  }
}

// 拖拽
let dragStart: { x: number; y: number; right: number; bottom: number } | null = null;
function onDragStart(e: MouseEvent): void {
  if (fullscreen.value) return;
  dragStart = { x: e.clientX, y: e.clientY, right: dockPos.value.right, bottom: dockPos.value.bottom };
  document.addEventListener('mousemove', onDragMove);
  document.addEventListener('mouseup', onDragEnd);
}
function onDragMove(e: MouseEvent): void {
  if (!dragStart) return;
  const dx = e.clientX - dragStart.x;
  const dy = e.clientY - dragStart.y;
  dockPos.value = {
    right: Math.max(0, dragStart.right - dx),
    bottom: Math.max(0, dragStart.bottom - dy),
  };
}
function onDragEnd(): void {
  dragStart = null;
  document.removeEventListener('mousemove', onDragMove);
  document.removeEventListener('mouseup', onDragEnd);
}

// 当用户登录后，预加载状态
watch(() => auth.isAuthenticated, (val) => {
  if (val && visible.value) {
    void reloadStatus();
    void reloadDbs();
  }
});

onMounted(() => {
  // 不主动 open，只在用户点击 FAB 时才请求接口，避免未启用 Copilot 时报 409。
});
</script>

<style scoped>
.copilot-fab {
  position: fixed;
  right: 24px;
  bottom: 24px;
  width: 52px;
  height: 52px;
  border-radius: 50%;
  background: linear-gradient(135deg, #2c7be5, #0d3b66);
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  box-shadow: 0 8px 24px rgba(13, 59, 102, 0.32);
  z-index: 9999;
  transition: transform 0.15s ease;
}
.copilot-fab:hover { transform: scale(1.05); }
.copilot-fab-icon { font-weight: 700; font-size: 14px; letter-spacing: 0.5px; }

.copilot-dock {
  position: fixed;
  width: 380px;
  height: 540px;
  background: #fff;
  border: 1px solid rgba(0, 0, 0, 0.08);
  border-radius: 12px;
  box-shadow: 0 20px 48px rgba(13, 59, 102, 0.22);
  display: flex;
  flex-direction: column;
  z-index: 9998;
  overflow: hidden;
}
.copilot-dock.is-fullscreen {
  width: auto !important;
  height: auto !important;
  border-radius: 0;
  border: none;
}
.copilot-dock__header {
  padding: 8px 12px;
  border-bottom: 1px solid rgba(0, 0, 0, 0.06);
  display: flex;
  align-items: center;
  justify-content: space-between;
  cursor: move;
  background: linear-gradient(180deg, rgba(248, 251, 255, 1), rgba(238, 245, 249, 1));
  user-select: none;
}
.copilot-dock__title {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
}
.copilot-dock__avatar {
  width: 22px;
  height: 22px;
  border-radius: 50%;
  background: linear-gradient(135deg, #2c7be5, #0d3b66);
  color: #fff;
  font-size: 10px;
  font-weight: 700;
  display: inline-flex;
  align-items: center;
  justify-content: center;
}
.copilot-dock__kb {
  padding: 8px 12px;
  border-bottom: 1px solid rgba(0, 0, 0, 0.04);
  font-size: 12px;
  background: rgba(13, 59, 102, 0.025);
}
.copilot-dock__kb-row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin: 2px 0;
}
.copilot-dock__kb-label { color: var(--sndb-ink-soft, #678); min-width: 56px; }
.copilot-dock__kb-value { color: var(--sndb-ink-strong, #111); }

.copilot-dock__db {
  padding: 8px 12px 0;
}
.copilot-dock__model {
  padding: 6px 12px 0;
}
.copilot-dock__perm {
  padding: 6px 12px 0;
  display: flex;
  align-items: center;
  flex-wrap: wrap;
}
.copilot-dock__ctx {
  padding: 6px 12px 0;
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 4px;
}
.copilot-dock__messages {
  flex: 1;
  overflow-y: auto;
  padding: 8px 12px;
  font-size: 13px;
}
.copilot-dock__empty {
  color: var(--sndb-ink-soft, #678);
  font-size: 12px;
}
.copilot-dock__empty ul { margin: 6px 0 0; padding: 0; list-style: none; }
.copilot-dock__empty li {
  padding: 6px 8px;
  border-radius: 6px;
  margin: 2px 0;
  cursor: pointer;
  background: rgba(44, 123, 229, 0.06);
}
.copilot-dock__empty li:hover { background: rgba(44, 123, 229, 0.12); }
.copilot-dock__empty-tip { margin: 0 0 8px; }
.copilot-dock__starters {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
  gap: 6px;
}
.copilot-dock__starter {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 2px;
  padding: 8px 10px;
  border: 1px solid rgba(44, 123, 229, 0.18);
  border-radius: 8px;
  background: rgba(44, 123, 229, 0.04);
  color: var(--sndb-ink, #1f2937);
  font-size: 12px;
  text-align: left;
  cursor: pointer;
  transition: background 0.15s, border-color 0.15s;
}
.copilot-dock__starter:hover {
  background: rgba(44, 123, 229, 0.12);
  border-color: rgba(44, 123, 229, 0.45);
}
.copilot-dock__starter-cat {
  font-size: 10px;
  padding: 1px 6px;
  border-radius: 999px;
  background: rgba(44, 123, 229, 0.15);
  color: rgb(44, 123, 229);
  line-height: 1.4;
}
.copilot-dock__starter-title {
  font-weight: 600;
  line-height: 1.3;
}
.copilot-dock__msg { margin: 8px 0; }
.copilot-dock__msg-role { font-size: 11px; color: var(--sndb-ink-soft, #678); margin-bottom: 2px; }
.copilot-dock__msg-body { white-space: pre-wrap; word-break: break-word; }
.copilot-dock__msg--user .copilot-dock__msg-body {
  background: rgba(44, 123, 229, 0.08);
  padding: 6px 8px;
  border-radius: 6px;
}
.copilot-dock__error {
  color: #d03050;
  font-size: 12px;
  padding: 6px 8px;
  background: rgba(208, 48, 80, 0.06);
  border-radius: 6px;
  margin-top: 6px;
}
.copilot-dock__caret {
  display: inline-block;
  width: 6px;
  height: 12px;
  background: currentColor;
  margin-left: 2px;
  animation: blink 1s infinite;
  vertical-align: text-bottom;
}
@keyframes blink { 50% { opacity: 0; } }
.copilot-dock__input {
  padding: 8px 12px 12px;
  border-top: 1px solid rgba(0, 0, 0, 0.06);
  background: #fff;
}

/* 会话历史 popover */
.copilot-dock__sessions { font-size: 12px; }
.copilot-dock__sessions-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 4px 4px 8px;
  border-bottom: 1px solid rgba(0, 0, 0, 0.06);
  margin-bottom: 6px;
}
.copilot-dock__sessions-empty {
  padding: 16px 4px;
  color: var(--sndb-ink-soft, #678);
  text-align: center;
}
.copilot-dock__sessions-list {
  list-style: none;
  margin: 0;
  padding: 0;
  max-height: 320px;
  overflow-y: auto;
}
.copilot-dock__sessions-list li {
  display: flex;
  gap: 8px;
  padding: 6px 8px;
  border-radius: 6px;
  cursor: pointer;
  align-items: center;
}
.copilot-dock__sessions-list li:hover { background: rgba(13, 59, 102, 0.05); }
.copilot-dock__sessions-list li.is-active { background: rgba(44, 123, 229, 0.12); }
.copilot-dock__sessions-item-main { flex: 1; min-width: 0; }
.copilot-dock__sessions-item-title {
  font-weight: 600;
  color: var(--sndb-ink-strong, #111);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.copilot-dock__sessions-item-meta {
  color: var(--sndb-ink-soft, #678);
  font-size: 11px;
  margin-top: 2px;
}
.copilot-dock__sessions-item-actions { flex-shrink: 0; }
</style>
