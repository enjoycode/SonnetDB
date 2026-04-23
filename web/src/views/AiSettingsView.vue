<template>
  <n-card title="Copilot 设置" :bordered="false">
    <n-space vertical :size="20">

      <!-- 获取 API Key + 加群 -->
      <n-card size="small" embedded :bordered="false" style="background: #f7fbff; border-radius: 10px">
        <n-space align="flex-start" :size="24" :wrap="false">
          <img
            :src="qrUrl"
            alt="扫码加群"
            style="width: 130px; height: 130px; border-radius: 8px; flex-shrink: 0; display: block; background: #fff"
            @error="handleQrError"
          />
          <n-space vertical :size="8" style="padding-top: 4px">
            <n-text strong style="font-size: 15px">扫码加入用户群</n-text>
            <n-text depth="3">
              进群即可免费领取 <strong>API Key</strong>，并获取 Copilot 新功能通知、
              使用技巧和官方支持。
            </n-text>
            <n-text depth="3" style="font-size: 12px">
              也可发邮件至
              <n-button text type="primary" tag="a" href="mailto:support@sonnetdb.com" style="font-size: 12px">
                support@sonnetdb.com
              </n-button>
              索取。
            </n-text>
          </n-space>
        </n-space>
      </n-card>

      <n-divider style="margin: 0" />

      <n-form
        :model="form"
        label-placement="left"
        label-width="120"
        :disabled="saving"
      >
        <n-form-item label="启用 Copilot">
          <n-switch v-model:value="form.enabled" />
          <n-text depth="3" style="margin-left: 12px; font-size: 12px">
            开启后 SQL Console 将显示 Copilot 面板
          </n-text>
        </n-form-item>

        <n-form-item label="服务节点">
          <n-radio-group v-model:value="form.provider">
            <n-space>
              <n-radio value="international">
                <n-space align="center" :size="6">
                  <span>国际版</span>
                  <n-tag size="tiny" type="default">推荐</n-tag>
                </n-space>
              </n-radio>
              <n-radio value="domestic">
                <n-space align="center" :size="6">
                  <span>国内版</span>
                  <n-tag size="tiny" type="success">低延迟</n-tag>
                </n-space>
              </n-radio>
            </n-space>
          </n-radio-group>
        </n-form-item>

        <n-form-item label="API Key">
          <n-input
            v-model:value="form.apiKey"
            type="password"
            show-password-on="click"
            :placeholder="hasApiKey ? '已设置（留空则保留原密钥）' : '扫码入群后可领取 API Key'"
            style="width: 360px"
          />
        </n-form-item>

        <n-form-item label="模型">
          <n-input
            v-model:value="form.model"
            placeholder="claude-sonnet-4-6"
            style="width: 240px"
          />
          <n-text depth="3" style="margin-left: 10px; font-size: 12px">
            默认模型已满足大多数场景
          </n-text>
        </n-form-item>

        <n-form-item label="超时（秒）">
          <n-input-number
            v-model:value="form.timeoutSeconds"
            :min="5"
            :max="300"
            style="width: 120px"
          />
        </n-form-item>

        <n-form-item label=" " :show-feedback="false">
          <n-space>
            <n-button type="primary" :loading="saving" @click="save">保存配置</n-button>
            <n-button
              :loading="testing"
              :disabled="!form.enabled || (!hasApiKey && !form.apiKey)"
              @click="testConnection"
            >
              测试连接
            </n-button>
            <n-text v-if="!form.enabled" depth="3" style="font-size: 12px; align-self: center">
              请先勾选「启用 Copilot」并保存配置后再测试连接。
            </n-text>
          </n-space>
        </n-form-item>
      </n-form>

      <n-alert v-if="saveMsg" :type="saveOk ? 'success' : 'error'" closable @close="saveMsg = ''">
        {{ saveMsg }}
      </n-alert>
      <n-alert v-if="testMsg" :type="testOk ? 'success' : 'error'" closable @close="testMsg = ''">
        {{ testMsg }}
      </n-alert>

      <n-text depth="3" style="font-size: 12px">
        当前节点：
        <strong>{{ form.provider === 'domestic' ? '国内版 (ai.sonnetdb.com)' : '国际版 (sonnet.vip)' }}</strong>
      </n-text>

      <n-divider style="margin: 0" />

      <!-- 知识库（M4） -->
      <n-card size="small" embedded :bordered="false" title="本地知识库">
        <template #header-extra>
          <n-space size="small">
            <n-button size="small" quaternary :loading="kbLoading" @click="loadKbStatus">刷新</n-button>
            <n-button
              size="small"
              type="primary"
              :loading="kbReindexing"
              :disabled="!kbStatus?.enabled"
              @click="reindexKb"
            >立即重建索引</n-button>
          </n-space>
        </template>
        <n-space vertical :size="6" v-if="kbStatus">
          <n-space :size="8" align="center">
            <n-tag size="small" :type="kbStatus.enabled ? 'success' : 'default'">
              {{ kbStatus.enabled ? '已启用' : '未启用' }}
            </n-tag>
            <n-tag size="small" :type="kbStatus.embeddingFallback ? 'warning' : 'info'">
              Embedding：{{ kbStatus.embeddingProvider }}{{ kbStatus.embeddingFallback ? ' (降级)' : '' }} · {{ kbStatus.vectorDimension }}D
            </n-tag>
          </n-space>
          <n-text depth="3" style="font-size: 12px">
            已索引文档 <strong>{{ kbStatus.indexedFiles }}</strong> 篇 ·
            分块 <strong>{{ kbStatus.indexedChunks }}</strong> 段 ·
            技能 <strong>{{ kbStatus.skillCount }}</strong> 条 ·
            最近摄入：<strong>{{ kbStatus.lastIngestedUtc ? formatTime(kbStatus.lastIngestedUtc) : '从未' }}</strong>
          </n-text>
          <n-text depth="3" style="font-size: 11px">
            根目录：{{ kbStatus.docsRoots.join(' / ') || '(默认)' }}
          </n-text>
        </n-space>
        <n-text v-else depth="3" style="font-size: 12px">
          {{ kbLoading ? '加载中…' : (kbErr || '点击「刷新」查看知识库状态') }}
        </n-text>
      </n-card>

    </n-space>
  </n-card>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue';
import {
  NAlert, NButton, NCard, NDivider, NForm, NFormItem, NInput, NInputNumber,
  NRadio, NRadioGroup, NSpace, NSwitch, NTag, NText, useMessage,
} from 'naive-ui';
import { useAuthStore } from '@/stores/auth';
import { fetchAiConfig, saveAiConfig, streamAiChat } from '@/api/ai';
import {
  fetchCopilotKnowledgeStatus,
  triggerCopilotDocsIngest,
  type CopilotKnowledgeStatus,
} from '@/api/copilot';

const auth = useAuthStore();
const message = useMessage();

// 知识库（M4）
const kbStatus = ref<CopilotKnowledgeStatus | null>(null);
const kbLoading = ref(false);
const kbReindexing = ref(false);
const kbErr = ref('');

async function loadKbStatus(): Promise<void> {
  if (!auth.state?.token) return;
  kbLoading.value = true;
  kbErr.value = '';
  try {
    kbStatus.value = await fetchCopilotKnowledgeStatus(auth.state.token);
  } catch (e: unknown) {
    kbErr.value = e instanceof Error ? e.message : String(e);
  } finally {
    kbLoading.value = false;
  }
}

async function reindexKb(): Promise<void> {
  if (!auth.state?.token) return;
  kbReindexing.value = true;
  try {
    await triggerCopilotDocsIngest(auth.state.token, true);
    message.success('已触发知识库重建，请稍候点击「刷新」查看结果');
    setTimeout(() => { void loadKbStatus(); }, 800);
  } catch (e: unknown) {
    message.error(e instanceof Error ? e.message : String(e));
  } finally {
    kbReindexing.value = false;
  }
}

function formatTime(iso: string): string {
  try {
    return new Date(iso).toLocaleString();
  } catch {
    return iso;
  }
}

// public/ 下的二维码资源（缺省提供 SVG 占位，运维可替换为真实 PNG）。
// 优先尝试 PNG，失败时回退到 SVG 占位，避免出现 broken image。
const qrUrl = ref(`${import.meta.env.BASE_URL}qr-group.png`);
function handleQrError(): void {
  const fallback = `${import.meta.env.BASE_URL}qr-group.svg`;
  if (qrUrl.value !== fallback) qrUrl.value = fallback;
}

const form = ref({
  enabled: false,
  provider: 'international',
  apiKey: '',
  model: 'claude-sonnet-4-6',
  timeoutSeconds: 60,
});
const hasApiKey = ref(false);
const saving = ref(false);
const testing = ref(false);
const saveMsg = ref('');
const saveOk = ref(false);
const testMsg = ref('');
const testOk = ref(false);

async function load(): Promise<void> {
  try {
    const cfg = await fetchAiConfig(auth.api);
    form.value = {
      enabled: cfg.enabled,
      provider: cfg.provider,
      apiKey: '',
      model: cfg.model,
      timeoutSeconds: cfg.timeoutSeconds,
    };
    hasApiKey.value = cfg.hasApiKey;
  } catch {
    // 静默失败，保持默认值
  }
}

async function save(): Promise<void> {
  saving.value = true;
  saveMsg.value = '';
  try {
    await saveAiConfig(auth.api, {
      enabled: form.value.enabled,
      provider: form.value.provider,
      apiKey: form.value.apiKey || undefined,
      model: form.value.model,
      timeoutSeconds: form.value.timeoutSeconds,
    });
    saveOk.value = true;
    saveMsg.value = '配置已保存。';
    if (form.value.apiKey) {
      hasApiKey.value = true;
      form.value.apiKey = '';
    }
  } catch (e: unknown) {
    saveOk.value = false;
    saveMsg.value = `保存失败：${e instanceof Error ? e.message : String(e)}`;
  } finally {
    saving.value = false;
  }
}

async function testConnection(): Promise<void> {
  testing.value = true;
  testMsg.value = '';
  testOk.value = false;
  try {
    const token = auth.state?.token ?? '';
    let reply = '';
    for await (const chunk of streamAiChat(token, [{ role: 'user', content: '请用一句话介绍你自己' }])) {
      reply += chunk;
      if (reply.length > 120) break;
    }
    testOk.value = true;
    testMsg.value = `连接成功！模型回复：${reply.slice(0, 100)}${reply.length > 100 ? '…' : ''}`;
  } catch (e: unknown) {
    const raw = e instanceof Error ? e.message : String(e);
    // 服务端尚未保存 enabled=true 时的友好提示
    if (raw.includes('ai_disabled')) {
      testMsg.value = '连接失败：AI 助手当前未启用。请先勾选「启用 Copilot」、填好 API Key 并点击「保存配置」，然后再测试连接。';
    } else {
      testMsg.value = `连接失败：${raw}`;
    }
  } finally {
    testing.value = false;
  }
}

onMounted(() => {
  void load();
  void loadKbStatus();
});
</script>
