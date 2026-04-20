import { defineStore } from 'pinia';
import { computed, ref } from 'vue';
import { createApiClient, loadAuth, persistAuth, type AuthState } from '@/api/client';

export const useAuthStore = defineStore('auth', () => {
  const state = ref<AuthState | null>(loadAuth());
  const api = createApiClient(() => state.value?.token ?? null);

  const isAuthenticated = computed(() => state.value !== null);
  const username = computed(() => state.value?.username ?? '');
  const isSuperuser = computed(() => state.value?.isSuperuser ?? false);

  async function login(username: string, password: string): Promise<void> {
    const resp = await api.post<AuthState>('/v1/auth/login', { username, password });
    state.value = resp.data;
    persistAuth(state.value);
  }

  function logout(): void {
    state.value = null;
    persistAuth(null);
  }

  return { state, api, isAuthenticated, username, isSuperuser, login, logout };
});
