<template>
  <div class="welcome-page">
    <header class="hero-header">
      <div class="brand-lockup" aria-label="SonnetDB">
        <div class="brand-mark" aria-hidden="true">
          <span class="brand-mark-core" />
        </div>
        <div class="brand-copy">
          <span class="brand-name">SonnetDB</span>
          <span class="brand-tagline">AI 加持的时序数据库</span>
        </div>
      </div>
      <nav class="hero-nav" aria-label="页面导航">
        <button type="button" class="nav-link" @click="scrollToSection('overview')">产品概览</button>
        <button type="button" class="nav-link" @click="scrollToSection('database')">核心价值</button>
        <button type="button" class="nav-link" @click="scrollToSection('capabilities')">重点能力</button>
        <button type="button" class="nav-link" @click="scrollToSection('roadmap')">能力演进</button>
        <button type="button" class="nav-cta" @click="goManage">进入后台</button>
      </nav>
    </header>

    <main class="hero-main">
      <section id="overview" class="hero-panel">
        <div class="hero-copy">
          <div class="hero-eyebrow">产品定位</div>
          <h1>让设备数据、业务指标与智能分析在一套时序数据库里协同工作。</h1>
          <p class="hero-subtitle">
            SonnetDB 面向工业设备、传感器网络、车联网和智能运维场景，承接来自采集端、网关与上游系统的数据，
            在统一时序库中完成存储、控制优化、趋势预测、轨迹分析、语义检索与 Copilot 智能协作，
            帮助客户更快完成从数据接入到分析、从判断到执行的完整闭环。
          </p>

          <div class="hero-actions">
            <button type="button" class="primary-action" @click="scrollToSection('capabilities')">查看重点能力</button>
            <button type="button" class="secondary-action" @click="scrollToSection('database')">了解产品价值</button>
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
                <span class="stage-kicker">产品价值</span>
                <strong>从实时数据接入、控制优化到预测分析和智能问答，SonnetDB 帮助客户把时间序列数据真正用起来。</strong>
                <p>
                  无论是设备遥测、能源监测、车辆轨迹还是业务指标，SonnetDB 都能把上游写入的实时数据、分析能力和智能协作整合到统一平台中。
                </p>
                <ul class="stage-list">
                  <li>帮助设备和传感器场景快速实现速率分析、平滑处理、控制优化和结果回写。</li>
                  <li>帮助业务和运维团队直接完成预测分析、异常识别、统计聚合和趋势判断。</li>
                  <li>帮助智能应用落地语义检索、地图轨迹分析和 Copilot 自然语言协作。</li>
                </ul>
              </section>
              <section class="stage-card">
                <span class="stage-kicker">控制优化</span>
                <strong>PID 控制与回写闭环</strong>
                <p>支持控制量计算、自动整定和结果回写，让数据库直接服务设备调优和控制回路优化。</p>
              </section>
              <section class="stage-card">
                <span class="stage-kicker">预测分析</span>
                <strong>趋势预测与异常识别</strong>
                <p>支持预测、异常检测、变点识别、速率分析和多种平滑算法，帮助客户更早发现问题、更快做出判断。</p>
              </section>
              <section class="stage-card stage-card-accent">
                <span class="stage-kicker">智能与时空</span>
                <strong>语义检索、轨迹分析与 Copilot</strong>
                <p>支持智能搜索、地图轨迹分析、设备位置洞察和自然语言问答，让数据平台更接近真正可用的智能产品。</p>
              </section>
            </div>
          </div>
        </div>
      </section>

      <section id="database" class="section-panel">
        <div class="section-heading">
          <span class="hero-eyebrow">核心价值</span>
          <h2>一套产品覆盖数据接入、分析决策、控制优化与智能协作。</h2>
          <p>
            SonnetDB 将原本分散在数据库、算法服务、地图系统和智能助手中的能力整合到一起，让客户用更低的系统复杂度获得更高的数据价值。
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
          <span class="hero-eyebrow">重点能力</span>
          <h2>围绕设备、传感器、轨迹数据和智能运维打造可直接落地的产品能力。</h2>
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
          <span class="hero-eyebrow">能力演进</span>
          <h2>产品正持续增强高级分析、智能检索、Copilot 和时空分析能力。</h2>
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
    title: '控制优化',
    description: '内置 PID、自动整定、速率分析、平滑处理和控制结果回写能力',
  },
  {
    title: '预测分析',
    description: '支持趋势预测、异常识别、统计分析和丰富的时间窗口计算',
  },
  {
    title: '向量时空',
    description: '支持语义检索、位置分析、轨迹洞察和地图联动展示',
  },
  {
    title: 'Copilot',
    description: '用自然语言完成查询、解释、排障和知识检索，降低使用门槛',
  },
];

const databaseCards = [
  {
    kicker: '控制',
    title: '设备控制优化',
    description: '支持 PID 控制、自动整定和控制结果回写，帮助客户更快把时序数据接入控制闭环。',
  },
  {
    kicker: '传感器',
    title: '传感器数据分析',
    description: '支持速率计算、平滑处理、累计变化、状态持续时长和状态切换分析，适合设备监测与运维场景。',
  },
  {
    kicker: '预测',
    title: '趋势预测与异常预警',
    description: '支持时间序列预测、异常检测、变点识别和相似走势分析，帮助客户更早发现风险和机会。',
  },
  {
    kicker: '函数',
    title: '高级统计与自定义分析',
    description: '支持分位数、直方图、去重统计、滑动窗口和自定义函数扩展，满足行业化分析需求。',
  },
  {
    kicker: '向量',
    title: '语义检索与相似度分析',
    description: '支持向量数据、相似度搜索和高性能近邻检索，为知识库、推荐和智能问答提供底座。',
  },
  {
    kicker: '时空',
    title: '位置数据与轨迹分析',
    description: '支持地理围栏、距离计算、速度统计、轨迹回放和地图联动，适合车联网、物流和移动设备场景。',
  },
  {
    kicker: 'Copilot',
    title: 'Copilot 智能协作',
    description: '把知识库、技能库和智能工具接入数据库工作流，让查询、排障、解释和培训更轻松。',
  },
];

const capabilityCards = [
  {
    index: '01',
    title: 'PID 控制与自动整定',
    description: '既能根据历史数据估算控制参数，也能直接输出控制结果，帮助客户缩短调试和投产周期。',
  },
  {
    index: '02',
    title: '控制结果回写',
    description: '支持把控制计算结果直接回写到时间序列中，方便做回测、联调和自动化闭环验证。',
  },
  {
    index: '03',
    title: '速率与平滑分析',
    description: '支持速率、增量、累计、移动平均和指数平滑，让设备数据趋势更容易理解和使用。',
  },
  {
    index: '04',
    title: '预测与异常检测',
    description: '支持未来趋势预测、异常识别、突变检测和相似走势对比，帮助团队提前响应风险。',
  },
  {
    index: '05',
    title: '高级统计分析',
    description: '支持均值以外的分位数、中位数、众数、波动分析、直方图和去重统计，适合更复杂的数据分析需求。',
  },
  {
    index: '06',
    title: '时间窗口与状态分析',
    description: '支持补点、插值、状态持续时长和状态切换分析，帮助客户更细致地理解设备运行过程。',
  },
  {
    index: '07',
    title: '自定义分析扩展',
    description: '支持把企业自己的算法和分析逻辑接入 SonnetDB，形成更贴合行业场景的专属能力。',
  },
  {
    index: '08',
    title: '语义搜索与知识召回',
    description: '支持向量检索和相似内容召回，为知识库问答、文档搜索和智能应用提供基础能力。',
  },
  {
    index: '09',
    title: '地图与轨迹分析',
    description: '支持位置筛选、距离测算、轨迹里程、速度统计和地图回放，方便做车辆、人员和设备路径分析。',
  },
  {
    index: '10',
    title: 'SNDBCopilot',
    description: '通过自然语言帮助客户完成数据查询、结果解释、问题定位和知识检索，显著降低使用门槛。',
  },
];

const roadmapCards = [
  {
    milestone: '当前可用',
    title: '高级分析能力',
    description: '控制优化、预测分析、高级统计和自定义扩展已经成为 SonnetDB 的核心产品能力。',
  },
  {
    milestone: '持续增强',
    title: '智能检索能力',
    description: '语义检索和高性能相似度搜索正在持续增强，为智能问答和知识库应用提供更强支持。',
  },
  {
    milestone: '持续增强',
    title: 'Copilot 智能协作',
    description: 'Copilot 将进一步增强自然语言查询、知识检索、运维排障和分析辅助体验。',
  },
  {
    milestone: '持续增强',
    title: '时空分析能力',
    description: '位置数据、轨迹分析、地图联动和时空洞察能力将持续完善，覆盖更多车联网和移动设备场景。',
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
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
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
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
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
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
}

.roadmap-grid {
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
}

@media (max-width: 1100px) {
  .hero-panel,
  .info-grid,
  .feature-grid,
  .roadmap-grid {
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
