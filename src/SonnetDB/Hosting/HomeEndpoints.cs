using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SonnetDB.Hosting;

/// <summary>
/// 把产品官网首页挂载到根路径 <c>/</c>。
/// </summary>
internal static class HomeEndpoints
{
    private static readonly byte[] _pageBytes = Encoding.UTF8.GetBytes(PageHtml);

    public static void MapHomePage(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/", ["GET"], async (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            ctx.Response.ContentLength = _pageBytes.Length;
            await ctx.Response.Body.WriteAsync(_pageBytes).ConfigureAwait(false);
        });
    }

    private const string PageHtml = """
        <!DOCTYPE html>
        <html lang="zh-CN">
        <head>
          <meta charset="UTF-8">
          <meta name="viewport" content="width=device-width, initial-scale=1.0">
          <title>SonnetDB — 嵌入式时序数据库</title>
          <style>
            :root {
              --blue: #0d3b66;
              --blue-mid: #146c94;
              --green: #18a058;
              --bg: #f8fbff;
              --bg-alt: #eef5f9;
              --card: #ffffff;
              --ink: #1a2733;
              --ink-soft: #5a7082;
              --ink-faint: #8fa8b8;
              --border: rgba(13,59,102,0.08);
              --shadow-sm: 0 4px 16px rgba(13,59,102,0.07);
              --shadow-md: 0 14px 40px rgba(13,59,102,0.10);
              --shadow-lg: 0 24px 64px rgba(13,59,102,0.13);
              --r-sm: 12px;
              --r-md: 20px;
              --r-lg: 28px;
            }

            *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
            html { scroll-behavior: smooth; }
            body {
              font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", "PingFang SC",
                           "Microsoft YaHei", sans-serif;
              font-size: 16px;
              line-height: 1.6;
              color: var(--ink);
              background: var(--bg);
            }
            a { text-decoration: none; color: inherit; }
            code {
              font-family: "JetBrains Mono", "Fira Code", Menlo, monospace;
              font-size: 0.88em;
              background: rgba(13,59,102,0.07);
              padding: 1px 5px;
              border-radius: 4px;
            }

            /* ---- Nav ---- */
            .nav {
              position: sticky; top: 0; z-index: 100;
              display: flex; align-items: center; justify-content: space-between; gap: 24px;
              padding: 0 clamp(20px, 5vw, 64px);
              height: 64px;
              background: rgba(248,251,255,0.92);
              backdrop-filter: blur(20px);
              border-bottom: 1px solid var(--border);
            }
            .brand {
              display: flex; align-items: center; gap: 10px;
              font-weight: 700; font-size: 1.2rem; color: var(--blue);
            }
            .brand-icon {
              width: 32px; height: 32px; border-radius: 8px;
              background: linear-gradient(135deg, var(--blue) 0%, var(--green) 100%);
              display: flex; align-items: center; justify-content: center;
              color: white; font-size: 14px; font-weight: 800; flex-shrink: 0;
            }
            .nav-links { display: flex; align-items: center; gap: 4px; }
            .nav-link {
              padding: 8px 14px; border-radius: 999px;
              color: var(--ink-soft); font-size: 0.93rem;
              transition: background .15s, color .15s;
            }
            .nav-link:hover { background: rgba(13,59,102,0.06); color: var(--ink); }
            .nav-btn {
              padding: 9px 20px; border-radius: 999px;
              background: linear-gradient(135deg, var(--blue) 0%, var(--green) 100%);
              color: #f8fbff; font-size: 0.93rem; font-weight: 600;
              box-shadow: 0 8px 20px rgba(13,59,102,0.18);
              transition: transform .15s, box-shadow .15s;
            }
            .nav-btn:hover { transform: translateY(-1px); box-shadow: 0 12px 28px rgba(13,59,102,0.24); }

            /* ---- Hero ---- */
            .hero {
              padding: clamp(64px, 10vw, 128px) clamp(20px, 5vw, 64px) clamp(48px, 6vw, 88px);
              text-align: center;
              background:
                radial-gradient(ellipse 80% 60% at 50% -10%, rgba(24,160,88,0.13), transparent),
                radial-gradient(ellipse 60% 40% at 20% 110%, rgba(13,59,102,0.10), transparent),
                var(--bg);
            }
            .hero-badge {
              display: inline-flex; align-items: center; gap: 8px;
              padding: 6px 14px; border-radius: 999px;
              border: 1px solid rgba(24,160,88,0.3);
              background: rgba(24,160,88,0.07);
              color: #14804a; font-size: 0.82rem; font-weight: 600;
              letter-spacing: 0.04em; margin-bottom: 28px;
            }
            .hero-badge::before {
              content: ''; width: 7px; height: 7px; border-radius: 50%; background: var(--green);
            }
            .hero-title {
              max-width: 720px; margin: 0 auto 24px;
              font-size: clamp(2.2rem, 5vw, 4.2rem);
              font-weight: 800; line-height: 1.06; letter-spacing: -0.04em;
            }
            .accent {
              background: linear-gradient(135deg, var(--blue) 0%, #146c94 40%, var(--green) 100%);
              -webkit-background-clip: text; -webkit-text-fill-color: transparent;
              background-clip: text;
            }
            .hero-sub {
              max-width: 600px; margin: 0 auto 40px;
              color: var(--ink-soft); font-size: 1.08rem; line-height: 1.75;
            }
            .hero-actions {
              display: flex; flex-wrap: wrap; justify-content: center; gap: 14px; margin-bottom: 0;
            }
            .btn-primary {
              display: inline-block; padding: 14px 28px; border-radius: 999px;
              background: linear-gradient(135deg, var(--blue) 0%, var(--green) 100%);
              color: #f8fbff; font-size: 1rem; font-weight: 700;
              box-shadow: 0 14px 36px rgba(13,59,102,0.22);
              transition: transform .15s, box-shadow .15s;
            }
            .btn-primary:hover { transform: translateY(-2px); box-shadow: 0 20px 44px rgba(13,59,102,0.28); }
            .btn-secondary {
              display: inline-block; padding: 14px 28px; border-radius: 999px;
              background: rgba(13,59,102,0.06); color: var(--ink);
              font-size: 1rem; font-weight: 600; border: 1px solid var(--border);
              transition: background .15s;
            }
            .btn-secondary:hover { background: rgba(13,59,102,0.10); }

            /* ---- Stats bar ---- */
            .stats {
              display: flex; flex-wrap: wrap; justify-content: center;
              border-top: 1px solid var(--border); border-bottom: 1px solid var(--border);
              background: var(--card);
            }
            .stat-item {
              flex: 1 1 160px; padding: 22px 28px; text-align: center;
              border-right: 1px solid var(--border);
            }
            .stat-item:last-child { border-right: none; }
            .stat-num { display: block; font-size: 1.55rem; font-weight: 800; color: var(--blue); }
            .stat-label { font-size: 0.84rem; color: var(--ink-soft); margin-top: 4px; }

            /* ---- Section shared ---- */
            .section { padding: clamp(48px, 7vw, 96px) clamp(20px, 5vw, 64px); }
            .section-header {
              text-align: center; max-width: 600px; margin: 0 auto clamp(32px, 5vw, 56px);
            }
            .eyebrow {
              display: inline-block; color: #146c94;
              font-size: 0.78rem; font-weight: 700; letter-spacing: 0.14em;
              text-transform: uppercase; margin-bottom: 12px;
            }
            .section-title {
              font-size: clamp(1.8rem, 3.5vw, 2.8rem);
              font-weight: 800; line-height: 1.1; letter-spacing: -0.03em;
            }
            .section-sub {
              margin-top: 16px; color: var(--ink-soft);
              font-size: 1.02rem; line-height: 1.7;
            }

            /* ---- Features ---- */
            .features { background: var(--bg-alt); }
            .feature-grid {
              display: grid;
              grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
              gap: 20px; max-width: 1100px; margin: 0 auto;
            }
            .feature-card {
              padding: 28px; border-radius: var(--r-lg);
              border: 1px solid var(--border); background: var(--card);
              box-shadow: var(--shadow-sm);
              transition: transform .2s, box-shadow .2s;
            }
            .feature-card:hover { transform: translateY(-3px); box-shadow: var(--shadow-md); }
            .feature-icon {
              width: 48px; height: 48px; border-radius: var(--r-sm);
              background: linear-gradient(135deg, rgba(13,59,102,0.08), rgba(24,160,88,0.08));
              display: flex; align-items: center; justify-content: center;
              font-size: 1.4rem; margin-bottom: 18px;
            }
            .feature-title { font-size: 1.05rem; font-weight: 700; margin-bottom: 10px; }
            .feature-desc { color: var(--ink-soft); font-size: 0.93rem; line-height: 1.7; }

            /* ---- Code demo ---- */
            .demo { background: var(--bg); }
            .demo-inner {
              display: grid; grid-template-columns: 1fr 1fr;
              gap: 48px; align-items: center; max-width: 1100px; margin: 0 auto;
            }
            .demo-copy .section-header { text-align: left; max-width: none; margin: 0 0 20px; }
            .demo-copy p { color: var(--ink-soft); line-height: 1.75; margin-bottom: 12px; }
            .code-window {
              border-radius: var(--r-lg);
              background: linear-gradient(180deg, #0e2238 0%, #143f60 100%);
              box-shadow: var(--shadow-lg); overflow: hidden;
            }
            .code-topbar {
              display: flex; gap: 8px; padding: 14px 18px;
              border-bottom: 1px solid rgba(255,255,255,0.06);
              align-items: center;
            }
            .code-dot { width: 10px; height: 10px; border-radius: 50%; background: rgba(255,255,255,0.22); }
            .code-tab { margin-left: 12px; font-size: 0.78rem; color: rgba(255,255,255,0.45); }
            pre.code-body {
              padding: 24px;
              font-family: "JetBrains Mono", "Fira Code", Menlo, monospace;
              font-size: 0.82rem; line-height: 1.75; color: #c9d8e8;
              overflow-x: auto; white-space: pre;
            }
            .kw { color: #79b8ff; }
            .fn { color: #b3f0a0; }
            .str { color: #f0b887; }
            .cmt { color: #5a7d9a; font-style: italic; }
            .num { color: #f7cc88; }

            /* ---- Quickstart ---- */
            .quickstart { background: var(--bg-alt); }
            .steps {
              display: grid;
              grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
              gap: 20px; max-width: 900px; margin: 0 auto;
            }
            .step {
              padding: 28px; border-radius: var(--r-lg);
              border: 1px solid var(--border); background: var(--card);
              box-shadow: var(--shadow-sm);
            }
            .step-num {
              display: inline-flex; align-items: center; justify-content: center;
              width: 36px; height: 36px; border-radius: 50%;
              background: linear-gradient(135deg, var(--blue), var(--green));
              color: white; font-weight: 800; font-size: 0.9rem; margin-bottom: 18px;
            }
            .step-title { font-size: 1.05rem; font-weight: 700; margin-bottom: 10px; }
            .step-desc { color: var(--ink-soft); font-size: 0.93rem; line-height: 1.7; }
            .step-code {
              margin-top: 14px; padding: 10px 14px; border-radius: var(--r-sm);
              background: rgba(13,59,102,0.06);
              font-family: "JetBrains Mono", monospace; font-size: 0.8rem;
              color: var(--blue); word-break: break-all;
            }

            /* ---- CTA ---- */
            .cta-section {
              padding: clamp(60px, 8vw, 100px) clamp(20px, 5vw, 64px);
              text-align: center;
              background: linear-gradient(135deg, var(--blue) 0%, #146c94 50%, var(--green) 100%);
              color: white;
            }
            .cta-title {
              font-size: clamp(1.8rem, 3.5vw, 2.8rem); font-weight: 800;
              line-height: 1.1; letter-spacing: -0.03em; margin-bottom: 16px;
            }
            .cta-sub {
              max-width: 520px; margin: 0 auto 36px;
              opacity: 0.85; font-size: 1.05rem; line-height: 1.7;
            }
            .btn-white {
              display: inline-block; padding: 14px 32px; border-radius: 999px;
              background: white; color: var(--blue);
              font-size: 1rem; font-weight: 700;
              box-shadow: 0 14px 36px rgba(0,0,0,0.15);
              transition: transform .15s, box-shadow .15s;
            }
            .btn-white:hover { transform: translateY(-2px); box-shadow: 0 20px 44px rgba(0,0,0,0.20); }

            /* ---- Footer ---- */
            footer {
              padding: 28px clamp(20px, 5vw, 64px);
              display: flex; flex-wrap: wrap; align-items: center;
              justify-content: space-between; gap: 12px;
              border-top: 1px solid var(--border); background: var(--card);
            }
            .footer-brand { display: flex; align-items: center; gap: 10px; font-weight: 700; color: var(--blue); }
            .footer-links { display: flex; gap: 20px; }
            .footer-links a { font-size: 0.88rem; color: var(--ink-soft); transition: color .15s; }
            .footer-links a:hover { color: var(--ink); }
            .footer-copy { font-size: 0.82rem; color: var(--ink-faint); }

            /* ---- Responsive ---- */
            @media (max-width: 768px) {
              .demo-inner { grid-template-columns: 1fr; }
              .nav-links .nav-link { display: none; }
              .stat-item { flex: 1 1 130px; padding: 16px 12px; }
            }
            @media (max-width: 480px) {
              .hero-title { font-size: 2rem; }
              .nav-btn { padding: 8px 14px; font-size: 0.88rem; }
            }
          </style>
        </head>
        <body>

          <nav class="nav">
            <a href="/" class="brand">
              <div class="brand-icon">S</div>
              SonnetDB
            </a>
            <div class="nav-links">
              <a href="/help/" class="nav-link">文档</a>
              <a href="/admin/" class="nav-btn">管理控制台</a>
            </div>
          </nav>

          <section class="hero">
            <div class="hero-badge">开源 · 嵌入式 · 零依赖</div>
            <h1 class="hero-title">
              为物联网而生的<br><span class="accent">时序数据库</span>
            </h1>
            <p class="hero-sub">
              SonnetDB 把单文件时序引擎、标准 SQL 接口、内置 AI 分析和完整管理后台
              收束为一套可独立交付的产品。无外部依赖，嵌入即用。
            </p>
            <div class="hero-actions">
              <a href="/admin/" class="btn-primary">进入管理控制台 →</a>
              <a href="/help/" class="btn-secondary">查看文档</a>
            </div>
          </section>

          <div class="stats">
            <div class="stat-item">
              <span class="stat-num">&lt; 5ms</span>
              <div class="stat-label">P99 写入延迟</div>
            </div>
            <div class="stat-item">
              <span class="stat-num">零依赖</span>
              <div class="stat-label">单文件独立部署</div>
            </div>
            <div class="stat-item">
              <span class="stat-num">SQL</span>
              <div class="stat-label">标准查询接口</div>
            </div>
            <div class="stat-item">
              <span class="stat-num">AI</span>
              <div class="stat-label">内置 SNDBCopilot</div>
            </div>
          </div>

          <section class="section features">
            <div class="section-header">
              <span class="eyebrow">核心特性</span>
              <h2 class="section-title">时序数据的完整解决方案</h2>
              <p class="section-sub">从数据写入到查询分析，从用户管理到 AI 辅助，开箱即得完整能力。</p>
            </div>
            <div class="feature-grid">
              <div class="feature-card">
                <div class="feature-icon">⚡</div>
                <div class="feature-title">高性能时序引擎</div>
                <p class="feature-desc">基于列式存储和时间分区的单文件引擎，针对时序写入和范围查询深度优化，P99 延迟低于 5ms。</p>
              </div>
              <div class="feature-card">
                <div class="feature-icon">🗃️</div>
                <div class="feature-title">标准 SQL 接口</div>
                <p class="feature-desc">无需学习私有查询语言。支持聚合函数、时间函数和窗口函数，与主流 SQL 生态无缝对接。</p>
              </div>
              <div class="feature-card">
                <div class="feature-icon">🤖</div>
                <div class="feature-title">SNDBCopilot · AI 助手</div>
                <p class="feature-desc">内置 AI 对话助手，支持自然语言生成 SQL、查询结果智能分析，流式输出带来接近 Copilot 的体验。</p>
              </div>
              <div class="feature-card">
                <div class="feature-icon">📈</div>
                <div class="feature-title">预测与异常检测</div>
                <p class="feature-desc">内置 forecast() 时序预测函数（Holt-Winters）和 anomaly() 异常检测，直接在 SQL 中调用。</p>
              </div>
              <div class="feature-card">
                <div class="feature-icon">🛡️</div>
                <div class="feature-title">细粒度权限管理</div>
                <p class="feature-desc">内置用户系统、数据库级权限授权和 Bearer Token 管理。支持只读用户、多租户隔离场景。</p>
              </div>
              <div class="feature-card">
                <div class="feature-icon">🔌</div>
                <div class="feature-title">ADO.NET 原生驱动</div>
                <p class="feature-desc">提供完整的 .NET ADO.NET 驱动，可嵌入任何 .NET 应用作进程内数据库，也支持独立服务器模式。</p>
              </div>
            </div>
          </section>

          <section class="section demo">
            <div class="demo-inner">
              <div class="demo-copy">
                <div class="section-header">
                  <span class="eyebrow">SQL 示例</span>
                  <h2 class="section-title">用熟悉的 SQL 读写时序数据</h2>
                </div>
                <p>SonnetDB 采用标准 SQL 语法，<code>tags</code> 用于高效过滤和分组，时间字段 <code>ts</code> 自动建立时间分区索引。</p>
                <p>内置 <code>date_trunc()</code>、<code>now()</code>、<code>interval</code> 等时序专用函数，以及 <code>forecast()</code> 预测 TVF。</p>
              </div>
              <div class="code-window">
                <div class="code-topbar">
                  <span class="code-dot"></span><span class="code-dot"></span><span class="code-dot"></span>
                  <span class="code-tab">sonnetdb.sql</span>
                </div>
                <pre class="code-body"><span class="cmt">-- 写入传感器数据</span>
        <span class="kw">INSERT INTO</span> sensors
          (ts, temperature, humidity, location=<span class="str">'building-a'</span>)
        <span class="kw">VALUES</span> (now(), <span class="num">23.4</span>, <span class="num">65.2</span>);

        <span class="cmt">-- 按小时聚合，最近 24 小时</span>
        <span class="kw">SELECT</span>
          <span class="fn">date_trunc</span>(<span class="str">'hour'</span>, ts) <span class="kw">AS</span> hour,
          <span class="fn">avg</span>(temperature)     <span class="kw">AS</span> avg_temp
        <span class="kw">FROM</span> sensors
        <span class="kw">WHERE</span> ts &gt; now() - <span class="kw">interval</span> <span class="str">'24h'</span>
          <span class="kw">AND</span> location = <span class="str">'building-a'</span>
        <span class="kw">GROUP BY</span> <span class="num">1</span>;

        <span class="cmt">-- 预测未来 6 小时温度趋势</span>
        <span class="kw">SELECT</span> * <span class="kw">FROM</span>
          <span class="fn">forecast</span>(<span class="str">'sensors'</span>, <span class="str">'temperature'</span>, <span class="num">6</span>);</pre>
              </div>
            </div>
          </section>

          <section class="section quickstart">
            <div class="section-header">
              <span class="eyebrow">快速开始</span>
              <h2 class="section-title">三步上手，分钟级部署</h2>
            </div>
            <div class="steps">
              <div class="step">
                <div class="step-num">1</div>
                <div class="step-title">下载并运行</div>
                <p class="step-desc">从 GitHub Releases 下载对应平台的单文件可执行包，直接运行，无需安装 .NET 运行时。</p>
                <div class="step-code">./sonnetdb --urls http://0.0.0.0:5000</div>
              </div>
              <div class="step">
                <div class="step-num">2</div>
                <div class="step-title">完成首次安装向导</div>
                <p class="step-desc">浏览器访问管理控制台，按向导设置服务器标识、组织名称、管理员账号和首枚 API Token。</p>
                <div class="step-code">http://localhost:5000/admin/</div>
              </div>
              <div class="step">
                <div class="step-num">3</div>
                <div class="step-title">写入数据并查询</div>
                <p class="step-desc">用 SQL Console 创建数据库并写入时序数据，或通过 HTTP API 和 ADO.NET 驱动对接业务系统。</p>
                <div class="step-code">POST /v1/db/{db}/sql</div>
              </div>
            </div>
          </section>

          <section class="cta-section">
            <h2 class="cta-title">立即开始使用 SonnetDB</h2>
            <p class="cta-sub">进入管理控制台，完成首次安装，开始管理你的时序数据。</p>
            <a href="/admin/" class="btn-white">进入管理控制台 →</a>
          </section>

          <footer>
            <div class="footer-brand">
              <div class="brand-icon">S</div>
              SonnetDB
            </div>
            <div class="footer-links">
              <a href="/help/">文档</a>
              <a href="/admin/">管理控制台</a>
            </div>
            <span class="footer-copy">© 2025 SonnetDB Contributors</span>
          </footer>

        </body>
        </html>
        """;
}
