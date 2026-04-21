import { createRouter, createWebHistory } from 'vue-router';
import SetupView from '@/views/SetupView.vue';
import LoginView from '@/views/LoginView.vue';
import AppShell from '@/views/AppShell.vue';
import DashboardView from '@/views/DashboardView.vue';
import SqlConsoleView from '@/views/SqlConsoleView.vue';
import DatabasesView from '@/views/DatabasesView.vue';
import EventsView from '@/views/EventsView.vue';
import UsersView from '@/views/UsersView.vue';
import GrantsView from '@/views/GrantsView.vue';
import TokensView from '@/views/TokensView.vue';
import AiSettingsView from '@/views/AiSettingsView.vue';
import { useAuthStore } from '@/stores/auth';
import { useSetupStore } from '@/stores/setup';

const router = createRouter({
  history: createWebHistory('/admin/'),
  routes: [
    { path: '/', redirect: '/app/dashboard' },
    { path: '/setup', name: 'setup', component: SetupView, meta: { anon: true } },
    { path: '/login', name: 'login', component: LoginView, meta: { anon: true } },
    {
      path: '/app',
      component: AppShell,
      meta: { app: true },
      redirect: '/app/dashboard',
      children: [
        { path: 'dashboard', name: 'dashboard', component: DashboardView },
        { path: 'sql', name: 'sql', component: SqlConsoleView },
        { path: 'databases', name: 'databases', component: DatabasesView },
        { path: 'events', name: 'events', component: EventsView },
        { path: 'users', name: 'users', component: UsersView, meta: { admin: true } },
        { path: 'grants', name: 'grants', component: GrantsView, meta: { admin: true } },
        { path: 'tokens', name: 'tokens', component: TokensView, meta: { admin: true } },
        { path: 'ai-settings', name: 'ai-settings', component: AiSettingsView, meta: { admin: true } },
      ],
    },
  ],
});

router.beforeEach(async (to) => {
  const auth = useAuthStore();
  const setup = useSetupStore();

  try {
    await setup.ensureLoaded();
  } catch {
    if (to.meta.app) {
      return { name: 'login' };
    }
    return true;
  }

  if (setup.needsSetup) {
    auth.apply(null);
    if (to.name === 'setup') {
      return true;
    }
    return { name: 'setup' };
  }

  if (to.name === 'setup') {
    return auth.isAuthenticated ? { name: 'dashboard' } : { name: 'login' };
  }

  if (to.name === 'login' && auth.isAuthenticated) {
    return { name: 'dashboard' };
  }

  if (to.meta.app && !auth.isAuthenticated) {
    return { name: 'login', query: { redirect: to.fullPath } };
  }

  if (to.meta.admin && !auth.isSuperuser) {
    return { name: 'dashboard' };
  }

  return true;
});

export default router;
