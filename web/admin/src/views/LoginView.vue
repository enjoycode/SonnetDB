<template>
  <div class="login-page">
    <n-card title="TSLite 管理控制台" class="login-card" :bordered="false">
      <n-form @submit.prevent="onSubmit">
        <n-form-item label="用户名">
          <n-input v-model:value="username" placeholder="admin" autofocus />
        </n-form-item>
        <n-form-item label="密码">
          <n-input v-model:value="password" type="password" show-password-on="click" placeholder="••••••" />
        </n-form-item>
        <n-button type="primary" block :loading="loading" attr-type="submit">登录</n-button>
        <n-text v-if="error" type="error" style="display:block;margin-top:12px;">{{ error }}</n-text>
      </n-form>
    </n-card>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { useRouter, useRoute } from 'vue-router';
import { NCard, NForm, NFormItem, NInput, NButton, NText } from 'naive-ui';
import { useAuthStore } from '@/stores/auth';

const username = ref('');
const password = ref('');
const loading = ref(false);
const error = ref<string | null>(null);

const auth = useAuthStore();
const router = useRouter();
const route = useRoute();

async function onSubmit(): Promise<void> {
  if (!username.value || !password.value) {
    error.value = '请输入用户名与密码。';
    return;
  }
  loading.value = true;
  error.value = null;
  try {
    await auth.login(username.value, password.value);
    const redirect = (route.query.redirect as string | undefined) ?? '/dashboard';
    await router.replace(redirect);
  } catch (e: unknown) {
    error.value = (e as { response?: { data?: { message?: string } } })?.response?.data?.message ?? '登录失败。';
  } finally {
    loading.value = false;
  }
}
</script>

<style scoped>
.login-page {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100%;
  background: linear-gradient(135deg, #18a058 0%, #36ad6a 100%);
}
.login-card {
  width: 360px;
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.12);
}
</style>
