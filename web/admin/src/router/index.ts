import { createRouter, createWebHistory } from 'vue-router';
import LoginView from '@/views/LoginView.vue';
import AppShell from '@/views/AppShell.vue';
import DashboardView from '@/views/DashboardView.vue';
import SqlConsoleView from '@/views/SqlConsoleView.vue';
import DatabasesView from '@/views/DatabasesView.vue';
import UsersView from '@/views/UsersView.vue';
import GrantsView from '@/views/GrantsView.vue';
import { useAuthStore } from '@/stores/auth';

const router = createRouter({
  // 服务端把 SPA 挂载在 /admin/ 前缀下，统一用 createWebHistory('/admin/')。
  history: createWebHistory('/admin/'),
  routes: [
    { path: '/login', name: 'login', component: LoginView, meta: { anon: true } },
    {
      path: '/',
      component: AppShell,
      redirect: '/dashboard',
      children: [
        { path: 'dashboard', name: 'dashboard', component: DashboardView },
        { path: 'sql', name: 'sql', component: SqlConsoleView },
        { path: 'databases', name: 'databases', component: DatabasesView },
        { path: 'users', name: 'users', component: UsersView, meta: { admin: true } },
        { path: 'grants', name: 'grants', component: GrantsView, meta: { admin: true } },
      ],
    },
  ],
});

router.beforeEach((to) => {
  const auth = useAuthStore();
  if (to.meta.anon) return true;
  if (!auth.isAuthenticated) {
    return { name: 'login', query: { redirect: to.fullPath } };
  }
  if (to.meta.admin && !auth.isSuperuser) {
    return { name: 'dashboard' };
  }
  return true;
});

export default router;
