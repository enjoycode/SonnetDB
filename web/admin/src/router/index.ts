import { createRouter, createWebHistory } from 'vue-router';
import LoginView from '@/views/LoginView.vue';
import DashboardView from '@/views/DashboardView.vue';
import { useAuthStore } from '@/stores/auth';

const router = createRouter({
  // 服务端把 SPA 挂载在 /admin/ 前缀下，统一用 createWebHistory('/admin/')。
  history: createWebHistory('/admin/'),
  routes: [
    { path: '/', redirect: '/dashboard' },
    { path: '/login', name: 'login', component: LoginView, meta: { anon: true } },
    { path: '/dashboard', name: 'dashboard', component: DashboardView },
  ],
});

router.beforeEach((to) => {
  const auth = useAuthStore();
  if (to.meta.anon) return true;
  if (!auth.isAuthenticated) {
    return { name: 'login', query: { redirect: to.fullPath } };
  }
  return true;
});

export default router;
