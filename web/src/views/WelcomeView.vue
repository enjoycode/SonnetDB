<template>
  <div class="welcome-page">
    <header class="hero-header">
      <BrandLogo />
      <nav class="hero-nav">
        <button type="button" class="nav-link" @click="scrollToSection('overview')">产品</button>
        <button type="button" class="nav-link" @click="openHelp">帮助</button>
        <button type="button" class="nav-cta" @click="goManage">{{ manageLabel }}</button>
      </nav>
    </header>

    <main class="hero-main">
      <section id="overview" class="hero-panel">
        <div class="hero-copy">
          <div class="hero-eyebrow">{{ heroEyebrow }}</div>
          <h1>让时序数据库像一个可交付产品，而不是一组零散接口。</h1>
          <p>
            SonnetDB 把单文件时序引擎、SQL 写入查询、嵌入式管理后台和首次安装向导收束成一套完整体验。
            你可以先完成初始化，再通过管理控制台接管数据库、用户、权限与实时事件流。
          </p>

          <div class="hero-actions">
            <button type="button" class="primary-action" @click="goManage">{{ primaryActionLabel }}</button>
            <button type="button" class="secondary-action" @click="openHelp">查看帮助</button>
          </div>

          <div class="hero-status-grid">
            <article class="status-tile status-highlight">
              <span class="tile-label">安装状态</span>
              <strong>{{ setup.needsSetup ? '等待首次安装' : '已完成初始化' }}</strong>
              <p>
                <template v-if="setup.needsSetup">
                  建议服务器 ID：<code>{{ setup.suggestedServerId }}</code>
                </template>
                <template v-else>
                  组织：<code>{{ setup.organization ?? '未命名组织' }}</code>
                </template>
              </p>
            </article>
            <article class="status-tile">
              <span class="tile-label">服务器 ID</span>
              <strong>{{ setup.serverId ?? setup.suggestedServerId }}</strong>
              <p>{{ setup.needsSetup ? '将在首次安装时写入 installation.json。' : '已固定为当前实例身份标识。' }}</p>
            </article>
            <article class="status-tile">
              <span class="tile-label">控制方式</span>
              <strong>用户名密码 + Bearer Token</strong>
              <p>首次安装会同时创建管理员账号与初始 API Token，后续可在后台继续签发和回收。</p>
            </article>
          </div>
        </div>

        <div class="hero-stage">
          <div class="stage-window">
            <div class="window-topbar">
              <span class="window-dot" />
              <span class="window-dot" />
              <span class="window-dot" />
            </div>
            <div class="stage-grid">
              <section class="stage-card stage-card-large">
                <span class="stage-kicker">Overview</span>
                <strong>{{ setup.needsSetup ? '准备进入首次安装' : '管理面板已就绪' }}</strong>
                <p>
                  {{ setup.needsSetup
                    ? '先完成服务器标识、组织、管理员与首个 Bearer Token 的初始化，再进入控制台。'
                    : '数据库、用户、Token 与事件流已统一收束到 /admin 管理入口。'
                  }}
                </p>
                <div class="stage-sparkline" aria-hidden="true">
                  <span v-for="bar in sparkBars" :key="bar" :style="{ height: `${bar}%` }" />
                </div>
              </section>
              <section class="stage-card">
                <span class="stage-kicker">Identity</span>
                <strong>{{ setup.serverId ?? setup.suggestedServerId }}</strong>
                <p>{{ setup.organization ?? '等待输入组织名称' }}</p>
              </section>
              <section class="stage-card">
                <span class="stage-kicker">Access</span>
                <strong>{{ setup.needsSetup ? '创建管理员' : auth.isAuthenticated ? '已登录管理员' : '准备登录' }}</strong>
                <p>{{ auth.isAuthenticated ? auth.username : '使用管理入口进入控制台。' }}</p>
              </section>
              <section class="stage-card stage-card-accent">
                <span class="stage-kicker">Realtime</span>
                <strong>SSE / Metrics / SQL</strong>
                <p>从首页进入后台后，可以直接查看数据库状态、慢查询和实时事件流。</p>
              </section>
            </div>
          </div>
        </div>
      </section>

      <section class="feature-strip">
        <article v-for="feature in features" :key="feature.title" class="feature-card">
          <span class="feature-index">{{ feature.index }}</span>
          <h2>{{ feature.title }}</h2>
          <p>{{ feature.description }}</p>
        </article>
      </section>

      <section id="help" class="help-panel">
        <div class="help-heading">
          <span class="hero-eyebrow">Help</span>
          <h2>首次使用建议按这三个步骤走</h2>
        </div>
        <div class="help-grid">
          <article class="help-card">
            <strong>1. 先完成安装</strong>
            <p>设置服务器 ID、组织名称、管理员用户名、密码和第一枚 Bearer Token。</p>
          </article>
          <article class="help-card">
            <strong>2. 进入管理后台</strong>
            <p>安装完成后可以直接登录，后续数据库创建、用户授权、Token 管理都在同一套后台里完成。</p>
          </article>
          <article class="help-card">
            <strong>3. 再接入业务流量</strong>
            <p>数据写入和查询依旧走 SQL 与 HTTP API；前台首页只负责把产品体验和安装入口做清楚。</p>
          </article>
        </div>
      </section>
    </main>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import BrandLogo from '@/components/BrandLogo.vue';
import { useAuthStore } from '@/stores/auth';
import { useSetupStore } from '@/stores/setup';

const router = useRouter();
const auth = useAuthStore();
const setup = useSetupStore();

const sparkBars = [28, 42, 36, 58, 48, 72, 66, 82];
const features = [
  {
    index: '01',
    title: '首次安装而不是空白页',
    description: '当 .system 为空时，/admin 会先引导用户完成初始化，而不是直接丢给一个不能登录的后台入口。',
  },
  {
    index: '02',
    title: '服务器身份一次讲清楚',
    description: '服务器 ID、组织、管理员账号和初始 Bearer Token 在同一页完成设置，后续首页也能持续展示这些身份信息。',
  },
  {
    index: '03',
    title: '产品首页和管理后台分层',
    description: '首页负责介绍产品、引导安装和承接帮助信息；真正的数据库、用户与权限管理进入 /app 后再展开。',
  },
];

const heroEyebrow = computed(() => (setup.needsSetup ? 'First Install' : 'SonnetDB Control Surface'));
const primaryActionLabel = computed(() => {
  if (setup.loading) return '加载中...';
  if (setup.needsSetup) return '开始首次安装';
  return auth.isAuthenticated ? '进入管理后台' : '进入管理控制台';
});
const manageLabel = computed(() => {
  if (setup.needsSetup) return '管理';
  return auth.isAuthenticated ? '进入后台' : '管理';
});

function scrollToSection(id: string): void {
  document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
}

function goManage(): void {
  if (setup.needsSetup) {
    router.push({ name: 'setup' });
    return;
  }
  if (auth.isAuthenticated) {
    router.push({ name: 'dashboard' });
    return;
  }
  router.push({ name: 'login' });
}

function openHelp(): void {
  const popup = window.open('/help/', '_blank', 'noopener,noreferrer');
  if (!popup) {
    window.location.assign('/help/');
  }
}

onMounted(async () => {
  await setup.ensureLoaded();
});
</script>

<style scoped>
.welcome-page {
  min-height: 100%;
  color: var(--sndb-ink-strong);
  background:
    radial-gradient(circle at top left, rgba(24, 160, 88, 0.14), transparent 28%),
    radial-gradient(circle at top right, rgba(13, 59, 102, 0.12), transparent 34%),
    linear-gradient(180deg, #f8fbff 0%, #eef5f9 100%);
}

.hero-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 24px;
  padding: 28px clamp(24px, 5vw, 56px) 12px;
}

.hero-nav {
  display: inline-flex;
  align-items: center;
  gap: 10px;
}

.nav-link,
.nav-cta,
.primary-action,
.secondary-action {
  border: 0;
  cursor: pointer;
  transition: transform 160ms ease, box-shadow 160ms ease, background 160ms ease, color 160ms ease;
}

.nav-link {
  padding: 10px 14px;
  border-radius: 999px;
  background: transparent;
  color: var(--sndb-ink-soft);
  font: inherit;
}

.nav-link:hover,
.secondary-action:hover {
  transform: translateY(-1px);
  color: var(--sndb-ink-strong);
}

.nav-cta,
.primary-action {
  padding: 11px 18px;
  border-radius: 999px;
  background: linear-gradient(135deg, #0d3b66 0%, #18a058 100%);
  color: #f8fbff;
  font: inherit;
  font-weight: 600;
  box-shadow: 0 14px 28px rgba(13, 59, 102, 0.18);
}

.nav-cta:hover,
.primary-action:hover {
  transform: translateY(-1px);
  box-shadow: 0 20px 34px rgba(13, 59, 102, 0.22);
}

.hero-main {
  display: flex;
  flex-direction: column;
  gap: 32px;
  padding: 12px clamp(24px, 5vw, 56px) 56px;
}

.hero-panel {
  display: grid;
  grid-template-columns: minmax(0, 1.15fr) minmax(320px, 0.85fr);
  gap: 28px;
  align-items: stretch;
}

.hero-copy,
.hero-stage,
.help-panel {
  border: 1px solid rgba(13, 59, 102, 0.08);
  border-radius: 28px;
  background: rgba(248, 251, 255, 0.84);
  box-shadow: 0 22px 54px rgba(13, 59, 102, 0.08);
  backdrop-filter: blur(14px);
}

.hero-copy {
  padding: clamp(28px, 4vw, 42px);
}

.hero-eyebrow {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 16px;
  color: #146c94;
  font-size: 0.78rem;
  font-weight: 700;
  letter-spacing: 0.12em;
  text-transform: uppercase;
}

.hero-copy h1,
.help-heading h2 {
  margin: 0;
  font-size: clamp(2.4rem, 5vw, 4.4rem);
  line-height: 1.02;
  letter-spacing: -0.04em;
}

.help-heading h2 {
  font-size: clamp(1.8rem, 4vw, 2.8rem);
}

.hero-copy p {
  margin: 18px 0 0;
  max-width: 56ch;
  color: var(--sndb-ink-soft);
  font-size: 1.03rem;
  line-height: 1.75;
}

.hero-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  margin-top: 28px;
}

.secondary-action {
  padding: 11px 18px;
  border-radius: 999px;
  background: rgba(13, 59, 102, 0.06);
  color: var(--sndb-ink-strong);
  font: inherit;
  font-weight: 600;
}

.hero-status-grid,
.feature-strip,
.help-grid {
  display: grid;
  gap: 16px;
}

.hero-status-grid {
  grid-template-columns: repeat(3, minmax(0, 1fr));
  margin-top: 30px;
}

.status-tile,
.feature-card,
.help-card,
.stage-card {
  border-radius: 22px;
  border: 1px solid rgba(13, 59, 102, 0.08);
  background: #ffffff;
  box-shadow: 0 14px 34px rgba(13, 59, 102, 0.06);
}

.status-tile {
  padding: 18px;
}

.status-highlight {
  background: linear-gradient(135deg, rgba(13, 59, 102, 0.98), rgba(20, 108, 148, 0.92));
  color: #f8fbff;
}

.status-highlight p,
.status-highlight .tile-label {
  color: rgba(248, 251, 255, 0.82);
}

.tile-label,
.stage-kicker,
.feature-index {
  display: block;
  color: var(--sndb-ink-soft);
  font-size: 0.78rem;
  letter-spacing: 0.12em;
  text-transform: uppercase;
}

.status-tile strong,
.stage-card strong,
.help-card strong {
  display: block;
  margin-top: 10px;
  font-size: 1.08rem;
}

.status-tile p,
.stage-card p,
.feature-card p,
.help-card p {
  margin: 10px 0 0;
  color: var(--sndb-ink-soft);
  line-height: 1.65;
}

.status-tile code {
  font-size: 0.94em;
}

.hero-stage {
  padding: 18px;
}

.stage-window {
  height: 100%;
  border-radius: 24px;
  background: linear-gradient(180deg, #0e2238 0%, #143f60 100%);
  color: #f8fbff;
  box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.06);
  overflow: hidden;
}

.window-topbar {
  display: flex;
  gap: 8px;
  padding: 16px 18px;
}

.window-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  background: rgba(248, 251, 255, 0.36);
}

.stage-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px;
  padding: 0 18px 18px;
}

.stage-card {
  padding: 18px;
  background: rgba(255, 255, 255, 0.06);
  color: #f8fbff;
}

.stage-card p,
.stage-kicker {
  color: rgba(248, 251, 255, 0.72);
}

.stage-card-large {
  grid-column: span 2;
}

.stage-card-accent {
  background: linear-gradient(135deg, rgba(24, 160, 88, 0.24), rgba(20, 108, 148, 0.16));
}

.stage-sparkline {
  display: flex;
  align-items: flex-end;
  gap: 8px;
  height: 92px;
  margin-top: 18px;
}

.stage-sparkline span {
  flex: 1;
  border-radius: 999px 999px 10px 10px;
  background: linear-gradient(180deg, rgba(248, 251, 255, 0.9), rgba(24, 160, 88, 0.6));
}

.feature-strip {
  grid-template-columns: repeat(3, minmax(0, 1fr));
}

.feature-card,
.help-card {
  padding: 22px;
}

.feature-card h2 {
  margin: 14px 0 0;
  font-size: 1.14rem;
}

.help-panel {
  padding: clamp(28px, 4vw, 36px);
}

.help-heading {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.help-grid {
  grid-template-columns: repeat(3, minmax(0, 1fr));
  margin-top: 24px;
}

@media (max-width: 1100px) {
  .hero-panel,
  .feature-strip,
  .help-grid,
  .hero-status-grid {
    grid-template-columns: 1fr;
  }

  .stage-grid {
    grid-template-columns: 1fr;
  }

  .stage-card-large {
    grid-column: span 1;
  }
}

@media (max-width: 720px) {
  .hero-header {
    flex-direction: column;
    align-items: flex-start;
  }

  .hero-nav {
    flex-wrap: wrap;
  }

  .hero-copy h1,
  .help-heading h2 {
    font-size: 2rem;
  }
}
</style>
