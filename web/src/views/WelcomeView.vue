<template>
  <div class="welcome-page">
    <header class="hero-header">
      <div class="brand-lockup" aria-label="SonnetDB">
        <div class="brand-mark" aria-hidden="true">
          <span class="brand-mark-core" />
        </div>
        <div class="brand-copy">
          <span class="brand-name">SonnetDB</span>
          <span class="brand-tagline">AI-Powered Time-Series Database</span>
        </div>
      </div>
      <nav class="hero-nav" aria-label="页面导航">
        <button type="button" class="nav-link" @click="scrollToSection('overview')">产品概览</button>
        <button type="button" class="nav-link" @click="scrollToSection('database')">功能体系</button>
        <button type="button" class="nav-link" @click="scrollToSection('capabilities')">核心功能</button>
        <button type="button" class="nav-link" @click="scrollToSection('roadmap')">路线图</button>
        <button type="button" class="nav-cta" @click="goManage">进入后台</button>
      </nav>
    </header>

    <main class="hero-main">
      <section id="overview" class="hero-panel">
        <div class="hero-copy">
          <div class="hero-eyebrow">产品定位</div>
          <h1>面向采集、查询、治理与智能协作的一体化时序数据库。</h1>
          <p class="hero-subtitle">
            SonnetDB 面向真实的时序业务场景，帮助团队用一套产品完成数据写入、SQL 查询、实时事件、权限治理和 AI 辅助分析。
          </p>

          <div class="hero-actions">
            <button type="button" class="primary-action" @click="scrollToSection('capabilities')">查看核心功能</button>
            <button type="button" class="secondary-action" @click="scrollToSection('database')">看功能体系</button>
          </div>

          <div class="hero-badges" aria-label="产品要点">
            <article v-for="item in heroHighlights" :key="item.title" class="hero-badge">
              <span>{{ item.title }}</span>
              <strong>{{ item.description }}</strong>
            </article>
          </div>
        </div>

        <div class="hero-stage">
          <div class="stage-window">
            <div class="window-topbar" aria-hidden="true">
              <span class="window-dot" />
              <span class="window-dot" />
              <span class="window-dot" />
            </div>
            <div class="stage-grid">
              <section class="stage-card stage-card-large">
                <span class="stage-kicker">功能总览</span>
                <strong>用一套数据库产品打通时序写入、查询分析、运维治理和 AI 协作。</strong>
                <p>
                  SonnetDB 面向设备遥测、业务指标和日志序列等场景，把 SQL Console、数据库管理、事件流和 SNDBCopilot 放在统一工作台里。
                </p>
                <ul class="stage-list">
                  <li>从写入到查询都围绕时序场景设计，优先解决时间窗、聚合和过滤问题。</li>
                  <li>数据库、用户、权限、Token 和实时事件集中在后台统一管理。</li>
                  <li>AI 能力可以直接贴近日常排障、分析和运维协作，而不是做成外围附属。</li>
                </ul>
              </section>
              <section class="stage-card">
                <span class="stage-kicker">数据接入</span>
                <strong>SQL 写入 + 时序采集</strong>
                <p>让指标、日志和设备数据先顺畅进入系统，再围绕时序负载做后续分析与治理。</p>
              </section>
              <section class="stage-card">
                <span class="stage-kicker">数据分析</span>
                <strong>时间窗 + 聚合 + 过滤</strong>
                <p>以时间范围、tag 条件和聚合计算为中心，让 SQL 查询更适合时序业务。</p>
              </section>
              <section class="stage-card stage-card-accent">
                <span class="stage-kicker">智能协作</span>
                <strong>SNDBCopilot + 实时事件</strong>
                <p>把 AI 助手和事件流接进同一工作流，帮助团队更快定位问题、理解数据和处理运维任务。</p>
              </section>
            </div>
          </div>
        </div>
      </section>

      <section id="database" class="section-panel">
        <div class="section-heading">
          <span class="hero-eyebrow">功能体系</span>
          <h2>首页优先讲清楚你能用 SonnetDB 做什么。</h2>
          <p>
            我们把首页重点从实现细节转向产品能力本身，让访问者一眼看到接入、查询、管理和 AI 协作这些真正有价值的功能模块。
          </p>
        </div>

        <div class="info-grid">
          <article v-for="item in databaseCards" :key="item.title" class="info-card">
            <span class="info-kicker">{{ item.kicker }}</span>
            <h3>{{ item.title }}</h3>
            <p>{{ item.description }}</p>
          </article>
        </div>
      </section>

      <section id="capabilities" class="section-panel">
        <div class="section-heading">
          <span class="hero-eyebrow">核心功能</span>
          <h2>围绕使用场景组织功能，而不是围绕底层实现组织文案。</h2>
        </div>

        <div class="feature-grid">
          <article v-for="feature in capabilityCards" :key="feature.title" class="feature-card">
            <span class="feature-index">{{ feature.index }}</span>
            <h3>{{ feature.title }}</h3>
            <p>{{ feature.description }}</p>
          </article>
        </div>
      </section>

      <section id="roadmap" class="section-panel section-panel-tight">
        <div class="section-heading">
          <span class="hero-eyebrow">路线图映射</span>
          <h2>当前首页的内容，来自路线图里已经落地的核心能力。</h2>
        </div>

        <div class="roadmap-grid">
          <article v-for="item in roadmapCards" :key="item.title" class="roadmap-card">
            <span class="roadmap-kicker">{{ item.milestone }}</span>
            <h3>{{ item.title }}</h3>
            <p>{{ item.description }}</p>
          </article>
        </div>
      </section>
    </main>
  </div>
</template>

<script setup lang="ts">
import { useRouter } from 'vue-router';
import { useAuthStore } from '../stores/auth';
import { useSetupStore } from '../stores/setup';

const router = useRouter();
const auth = useAuthStore();
const setup = useSetupStore();

const heroHighlights = [
  {
    title: 'SQL 写查',
    description: '围绕时序数据的写入、过滤、聚合和时间范围查询设计',
  },
  {
    title: '统一管理',
    description: '数据库、用户、权限、Token 和事件流集中在一个后台里',
  },
  {
    title: 'AI 傍身',
    description: 'SNDBCopilot 为分析、排障和运维协作提供智能辅助',
  },
];

const databaseCards = [
  {
    kicker: '接入',
    title: 'SQL 写入与时序数据落库',
    description: '围绕指标、设备和日志序列的接入体验组织能力，让数据尽快写入并进入可查询状态。',
  },
  {
    kicker: '查询',
    title: '时间范围、聚合与过滤优先',
    description: '把时间窗、tag 条件、聚合分析和 SQL Console 作为第一层体验，而不是放在深处。',
  },
  {
    kicker: '治理',
    title: '数据库与权限管理同台协作',
    description: '数据库列表、用户、权限与 Token 都放在统一后台里，方便团队日常维护与分工。',
  },
  {
    kicker: '智能',
    title: 'AI 助手和实时事件直接可用',
    description: 'SNDBCopilot 与事件流入口直接面向运营和排障流程，让系统更像一个会协作的数据库产品。',
  },
];

const capabilityCards = [
  {
    index: '01',
    title: 'SQL Console',
    description: '直接执行时序 SQL，快速完成查询验证、数据排查和结果确认。',
  },
  {
    index: '02',
    title: '数据库管理',
    description: '集中查看数据库状态与配置，让数据资产管理和日常维护更清楚。',
  },
  {
    index: '03',
    title: '权限与 Token',
    description: '用用户、授权和 Token 管理把访问控制做进产品，而不是留给外围系统补齐。',
  },
  {
    index: '04',
    title: 'SNDBCopilot 与事件流',
    description: '把 AI 辅助和实时事件放进同一后台，让分析、诊断和协作动作更连贯。',
  },
];

const roadmapCards = [
  {
    milestone: 'M1-M4',
    title: '数据底座',
    description: '先把时序写入、查询和基本管理能力做稳，为上层产品体验打好基础。',
  },
  {
    milestone: 'M5-M7',
    title: '稳定运行',
    description: '继续补齐保留策略、压缩、删除与索引能力，让数据库在长期运行中更可靠。',
  },
  {
    milestone: 'M8-M12',
    title: '智能与扩展',
    description: '把控制台、事件流、AI 协作和更多高阶能力串起来，形成完整的产品闭环。',
  },
];

function scrollToSection(id: string): void {
  document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
}

async function goManage(): Promise<void> {
  try {
    await setup.ensureLoaded();
  } catch {
    // 如果健康检查或安装状态暂时不可达，仍然继续走管理入口的默认路由。
  }

  if (setup.needsSetup) {
    await router.push({ name: 'setup' });
    return;
  }

  if (auth.isAuthenticated) {
    await router.push({ name: 'dashboard' });
    return;
  }

  await router.push({ name: 'login' });
}
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

.brand-lockup {
  display: inline-flex;
  align-items: center;
  gap: 14px;
}

.brand-mark {
  width: 52px;
  height: 52px;
  border-radius: 18px;
  display: grid;
  place-items: center;
  background: linear-gradient(135deg, #0d3b66 0%, #146c94 55%, #18a058 100%);
  box-shadow: 0 16px 30px rgba(13, 59, 102, 0.16);
}

.brand-mark-core {
  width: 22px;
  height: 22px;
  border-radius: 50%;
  border: 4px solid rgba(248, 251, 255, 0.94);
  box-shadow: inset 0 0 0 2px rgba(248, 251, 255, 0.12);
}

.brand-copy {
  display: inline-flex;
  flex-direction: column;
  gap: 2px;
}

.brand-name {
  font-size: 1.25rem;
  font-weight: 700;
  line-height: 1.1;
  letter-spacing: 0.03em;
  color: var(--sndb-ink-strong);
}

.brand-tagline {
  font-size: 0.78rem;
  line-height: 1.2;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--sndb-ink-soft);
}

.hero-nav {
  display: inline-flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
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
.section-panel {
  border: 1px solid rgba(13, 59, 102, 0.08);
  border-radius: 28px;
  background: rgba(248, 251, 255, 0.84);
  box-shadow: 0 22px 54px rgba(13, 59, 102, 0.08);
  backdrop-filter: blur(14px);
}

.hero-copy {
  padding: clamp(28px, 4vw, 42px);
}

.hero-eyebrow,
.roadmap-kicker,
.stage-kicker,
.info-kicker,
.feature-index {
  display: block;
  color: var(--sndb-ink-soft);
  font-size: 0.78rem;
  letter-spacing: 0.12em;
  text-transform: uppercase;
}

.hero-eyebrow {
  margin-bottom: 16px;
  color: #146c94;
  font-weight: 700;
}

.hero-copy h1,
.section-heading h2 {
  margin: 0;
  font-size: clamp(2.3rem, 5vw, 4.3rem);
  line-height: 1.02;
  letter-spacing: -0.04em;
}

.section-heading h2 {
  font-size: clamp(1.8rem, 4vw, 2.8rem);
}

.hero-subtitle,
.section-heading p,
.stage-card p,
.info-card p,
.feature-card p,
.roadmap-card p {
  margin: 18px 0 0;
  color: var(--sndb-ink-soft);
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

.hero-badges,
.info-grid,
.feature-grid,
.roadmap-grid {
  display: grid;
  gap: 16px;
}

.hero-badges {
  grid-template-columns: repeat(3, minmax(0, 1fr));
  margin-top: 28px;
}

.hero-badge,
.info-card,
.feature-card,
.roadmap-card,
.stage-card {
  border-radius: 22px;
  border: 1px solid rgba(13, 59, 102, 0.08);
  background: #ffffff;
  box-shadow: 0 14px 34px rgba(13, 59, 102, 0.06);
}

.hero-badge {
  padding: 16px 18px;
}

.hero-badge span {
  display: block;
  color: var(--sndb-ink-soft);
  font-size: 0.78rem;
  letter-spacing: 0.12em;
  text-transform: uppercase;
}

.hero-badge strong {
  display: block;
  margin-top: 10px;
  font-size: 1.02rem;
  line-height: 1.45;
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

.stage-card strong,
.info-card h3,
.feature-card h3,
.roadmap-card h3 {
  display: block;
  margin-top: 10px;
  font-size: 1.08rem;
}

.stage-list {
  margin: 16px 0 0;
  padding-left: 18px;
  color: rgba(248, 251, 255, 0.82);
}

.stage-list li + li {
  margin-top: 8px;
}

.section-panel {
  padding: clamp(26px, 4vw, 38px);
}

.section-panel-tight {
  margin-bottom: 4px;
}

.section-heading {
  display: flex;
  flex-direction: column;
  gap: 12px;
  margin-bottom: 22px;
}

.info-grid {
  grid-template-columns: repeat(4, minmax(0, 1fr));
}

.info-card,
.feature-card,
.roadmap-card {
  padding: 22px;
}

.info-card p,
.feature-card p,
.roadmap-card p {
  margin-top: 12px;
}

.feature-grid {
  grid-template-columns: repeat(4, minmax(0, 1fr));
}

.roadmap-grid {
  grid-template-columns: repeat(3, minmax(0, 1fr));
}

@media (max-width: 1100px) {
  .hero-panel,
  .info-grid,
  .feature-grid,
  .roadmap-grid,
  .hero-badges {
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

  .hero-copy h1,
  .section-heading h2 {
    font-size: 2rem;
  }
}
</style>
