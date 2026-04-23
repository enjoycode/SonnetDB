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

    <!-- 消息流 -->
    <section class="copilot-dock__messages" ref="msgContainer">
      <div v-if="messages.length === 0 && !running" class="copilot-dock__empty">
        <p>问点什么？例如：</p>
        <ul>
          <li @click="quickPrompt('列出当前数据库的所有 measurement')">列出当前数据库的所有 measurement</li>
          <li @click="quickPrompt('帮我建一张存储温度数据的表，包含设备 tag 和 float 字段')">帮我建一张存储温度数据的表</li>
          <li @click="quickPrompt('如何用 knn 做向量检索？')">如何用 knn 做向量检索？</li>
        </ul>
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
import { computed, nextTick, onMounted, ref, watch } from 'vue';
import { NButton, NInput, NSelect, NSpace, NTag, NText, type SelectOption, useMessage } from 'naive-ui';
import { useAuthStore } from '@/stores/auth';
import { listDatabases } from '@/api/server';
import {
  streamCopilotChat,
  fetchCopilotKnowledgeStatus,
  triggerCopilotDocsIngest,
  type CopilotKnowledgeStatus,
  type CopilotMessage,
} from '@/api/copilot';

const auth = useAuthStore();
const message = useMessage();

const visible = ref(false);
const fullscreen = ref(false);
const status = ref<CopilotKnowledgeStatus | null>(null);
const reindexing = ref(false);

const dbs = ref<string[]>([]);
const selectedDb = ref<string>('');
const dbOptions = computed<SelectOption[]>(() => dbs.value.map((d) => ({ label: d, value: d })));

const prompt = ref('');
const messages = ref<CopilotMessage[]>([]);
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

function open(): void {
  visible.value = true;
  if (status.value === null) {
    void reloadStatus();
    void reloadDbs();
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

function quickPrompt(text: string): void {
  prompt.value = text;
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

  const userText = prompt.value.trim();
  prompt.value = '';
  errorMsg.value = '';
  messages.value.push({ role: 'user', content: userText });
  streamBuffer.value = '';
  running.value = true;
  await scrollToBottom();

  const ac = new AbortController();
  abort.value = ac;

  const stepLog: string[] = [];
  try {
    for await (const event of streamCopilotChat(
      auth.state.token,
      { db: selectedDb.value, messages: messages.value },
      ac.signal,
    )) {
      if (ac.signal.aborted) break;
      if (event.type === 'final' && event.answer) {
        streamBuffer.value = event.answer;
      } else if (event.type === 'error') {
        errorMsg.value = event.message ?? 'Copilot 请求失败';
      } else if (event.message) {
        stepLog.push(event.message);
        // 仅当尚无 final 时显示进度
        if (!streamBuffer.value) streamBuffer.value = stepLog.slice(-3).join('\n');
      }
      await scrollToBottom();
    }
    if (streamBuffer.value) {
      messages.value.push({ role: 'assistant', content: streamBuffer.value });
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
</style>
