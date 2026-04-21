<template>
  <n-card title="SNDBCopilot 设置" :bordered="false">
    <n-space vertical :size="20">

      <!-- 获取 API Key + 加群 -->
      <n-card size="small" embedded :bordered="false" style="background: #f7fbff; border-radius: 10px">
        <n-space align="flex-start" :size="24" :wrap="false">
          <img
            :src="qrUrl"
            alt="扫码加群"
            style="width: 130px; height: 130px; border-radius: 8px; flex-shrink: 0; display: block"
          />
          <n-space vertical :size="8" style="padding-top: 4px">
            <n-text strong style="font-size: 15px">扫码加入用户群</n-text>
            <n-text depth="3">
              进群即可免费领取 <strong>API Key</strong>，并获取 SNDBCopilot 新功能通知、
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
            开启后 SQL Console 将显示 SNDBCopilot 面板
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
            <n-button :loading="testing" :disabled="!hasApiKey && !form.apiKey" @click="testConnection">
              测试连接
            </n-button>
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

    </n-space>
  </n-card>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue';
import {
  NAlert, NButton, NCard, NDivider, NForm, NFormItem, NInput, NInputNumber,
  NRadio, NRadioGroup, NSpace, NSwitch, NTag, NText,
} from 'naive-ui';
import { useAuthStore } from '@/stores/auth';
import { fetchAiConfig, saveAiConfig, streamAiChat } from '@/api/ai';

const auth = useAuthStore();

// public/ 下的图片路径，随 base URL 自动适配（生产环境 /admin/qr-group.png）
const qrUrl = `${import.meta.env.BASE_URL}qr-group.png`;

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
    testMsg.value = `连接失败：${e instanceof Error ? e.message : String(e)}`;
  } finally {
    testing.value = false;
  }
}

onMounted(load);
</script>
