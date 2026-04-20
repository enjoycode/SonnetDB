<template>
  <n-card title="Token 管理" :bordered="false">
    <n-space vertical :size="12">
      <n-space>
        <n-select
          v-model:value="issueUser"
          :options="userOptions"
          placeholder="选择用户"
          style="width: 180px"
        />
        <n-button type="primary" @click="onIssue">ISSUE TOKEN</n-button>
        <n-select
          v-model:value="filterUser"
          :options="userOptions"
          clearable
          placeholder="按用户筛选"
          style="width: 180px"
        />
        <n-button @click="reload">刷新</n-button>
      </n-space>

      <n-alert v-if="errorMsg" type="error" :title="errorMsg" closable @close="errorMsg = ''" />

      <n-data-table :columns="cols" :data="tokens" :bordered="false" size="small" />
    </n-space>

    <n-modal v-model:show="issuedModal" preset="card" title="新 Token" style="width: 560px">
      <n-space vertical :size="12">
        <n-text>Token 明文只会展示一次，请立即复制保存。</n-text>
        <n-space>
          <n-tag type="success">{{ issuedTokenId }}</n-tag>
          <n-tag>{{ issuedUserName }}</n-tag>
        </n-space>
        <n-input
          :value="issuedToken"
          type="textarea"
          readonly
          :autosize="{ minRows: 3, maxRows: 6 }"
        />
        <n-space justify="end">
          <n-button @click="copyIssuedToken">复制 Token</n-button>
          <n-button type="primary" @click="issuedModal = false">关闭</n-button>
        </n-space>
      </n-space>
    </n-modal>
  </n-card>
</template>

<script setup lang="ts">
import { computed, h, onMounted, ref, watch } from 'vue';
import {
  NCard, NSpace, NSelect, NButton, NAlert, NDataTable, NPopconfirm,
  NModal, NText, NTag, NInput, useMessage, type DataTableColumns, type SelectOption,
} from 'naive-ui';
import { useAuthStore } from '@/stores/auth';
import { execControlPlaneSql, quote, rowsToObjects } from '@/api/sql';

interface TokenRow {
  token_id: string;
  user_name: string;
  created_utc: string;
  last_used_utc: string | null;
  [k: string]: unknown;
}

const auth = useAuthStore();
const message = useMessage();

const tokens = ref<TokenRow[]>([]);
const users = ref<string[]>([]);
const issueUser = ref<string | null>(null);
const filterUser = ref<string | null>(null);
const errorMsg = ref('');

const issuedModal = ref(false);
const issuedTokenId = ref('');
const issuedToken = ref('');
const issuedUserName = ref('');

const userOptions = computed<SelectOption[]>(() => users.value.map((user) => ({
  label: user,
  value: user,
})));

const cols: DataTableColumns<TokenRow> = [
  { title: 'Token ID', key: 'token_id' },
  { title: '用户', key: 'user_name' },
  { title: '签发时间', key: 'created_utc' },
  {
    title: '最近使用',
    key: 'last_used_utc',
    render: (row) => row.last_used_utc ?? '从未使用',
  },
  {
    title: '操作',
    key: 'actions',
    width: 110,
    render: (row) => h(NPopconfirm, {
      onPositiveClick: () => onRevoke(row.token_id),
    }, {
      trigger: () => h(NButton, { size: 'small', type: 'error', text: true }, { default: () => 'REVOKE' }),
      default: () => `确认吊销 ${row.token_id}？`,
    }),
  },
];

async function reload(): Promise<void> {
  errorMsg.value = '';
  const showUsersSql = 'SHOW USERS';
  const showTokensSql = filterUser.value ? `SHOW TOKENS FOR ${filterUser.value}` : 'SHOW TOKENS';
  const [usersResult, tokensResult] = await Promise.all([
    execControlPlaneSql(auth.api, showUsersSql),
    execControlPlaneSql(auth.api, showTokensSql),
  ]);

  if (usersResult.error) {
    errorMsg.value = usersResult.error.message;
    return;
  }

  users.value = usersResult.rows.map((row) => String(row[0]));
  if (issueUser.value && !users.value.includes(issueUser.value)) {
    issueUser.value = null;
  }
  if (!issueUser.value) {
    issueUser.value = users.value[0] ?? null;
  }
  if (filterUser.value && !users.value.includes(filterUser.value)) {
    filterUser.value = null;
  }

  if (tokensResult.error) {
    errorMsg.value = tokensResult.error.message;
    tokens.value = [];
    return;
  }

  tokens.value = rowsToObjects<TokenRow>(tokensResult);
}

async function onIssue(): Promise<void> {
  if (!issueUser.value) {
    message.error('请先选择用户。');
    return;
  }

  const result = await execControlPlaneSql(auth.api, `ISSUE TOKEN FOR ${issueUser.value}`);
  if (result.error) {
    message.error(result.error.message);
    return;
  }

  const row = result.rows[0];
  if (!row || row.length < 2) {
    message.error('服务端没有返回 token 明文。');
    return;
  }

  issuedTokenId.value = String(row[0]);
  issuedToken.value = String(row[1]);
  issuedUserName.value = issueUser.value;
  issuedModal.value = true;
  message.success(`已为 ${issueUser.value} 签发新 token`);
  await reload();
}

async function onRevoke(tokenId: string): Promise<void> {
  const result = await execControlPlaneSql(auth.api, `REVOKE TOKEN ${quote(tokenId)}`);
  if (result.error) {
    message.error(result.error.message);
    return;
  }

  message.success(`已吊销 ${tokenId}`);
  await reload();
}

async function copyIssuedToken(): Promise<void> {
  if (!issuedToken.value) {
    return;
  }

  try {
    if (!navigator.clipboard) {
      message.warning('当前环境不支持剪贴板复制。');
      return;
    }

    await navigator.clipboard.writeText(issuedToken.value);
    message.success('Token 已复制到剪贴板。');
  } catch {
    message.error('复制失败，请手动复制。');
  }
}

watch(filterUser, async () => {
  await reload();
});

onMounted(reload);
</script>
