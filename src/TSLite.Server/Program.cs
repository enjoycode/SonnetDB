using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TSLite.Server.Auth;
using TSLite.Server.Configuration;
using TSLite.Server.Contracts;
using TSLite.Server.Endpoints;
using TSLite.Server.Hosting;
using TSLite.Server.Json;

namespace TSLite.Server;

/// <summary>
/// AOT-friendly Minimal API 入口。
/// </summary>
public static class Program
{
    /// <summary>
    /// 构建并运行 TSLite Server。
    /// </summary>
    public static async Task<int> Main(string[] args)
    {
        var app = BuildApp(args);
        await app.RunAsync().ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// 构造但不启动 <see cref="WebApplication"/>。供测试代码注入自定义配置。
    /// </summary>
    /// <param name="args">命令行参数（透传给 <see cref="WebApplication.CreateSlimBuilder(string[])"/>）。</param>
    /// <param name="overrideOptions">可选覆盖配置（如自定义 DataRoot / Tokens）。</param>
    public static WebApplication BuildApp(string[] args, ServerOptions? overrideOptions = null)
    {
        var builder = WebApplication.CreateSlimBuilder(args);

        // 配置：appsettings + 环境变量 TSLITE_*
        builder.Configuration.AddEnvironmentVariables(prefix: "TSLITE_");

        var serverOptions = overrideOptions ?? LoadServerOptions(builder.Configuration);

        Configure(builder, serverOptions);

        var app = builder.Build();
        ConfigureMiddleware(app, serverOptions);
        MapEndpoints(app, serverOptions);
        return app;
    }

    private static void BindNonAot(Microsoft.Extensions.Configuration.BinderOptions options)
    {
        options.BindNonPublicProperties = false;
    }

    private static ServerOptions LoadServerOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("TSLiteServer");
        var options = new ServerOptions();

        var dataRoot = section["DataRoot"];
        if (!string.IsNullOrWhiteSpace(dataRoot))
            options.DataRoot = dataRoot;

        var autoLoad = section["AutoLoadExistingDatabases"];
        if (bool.TryParse(autoLoad, out var auto))
            options.AutoLoadExistingDatabases = auto;

        var allowAnon = section["AllowAnonymousProbes"];
        if (bool.TryParse(allowAnon, out var anon))
            options.AllowAnonymousProbes = anon;

        var tokens = section.GetSection("Tokens");
        foreach (var child in tokens.GetChildren())
        {
            if (!string.IsNullOrEmpty(child.Key) && !string.IsNullOrEmpty(child.Value))
                options.Tokens[child.Key] = child.Value;
        }
        return options;
    }

    private static void Configure(WebApplicationBuilder builder, ServerOptions serverOptions)
    {
        builder.Services.Configure<JsonOptions>(o =>
        {
            o.SerializerOptions.TypeInfoResolverChain.Insert(0, ServerJsonContext.Default);
        });

        builder.Services.AddSingleton(serverOptions);
        builder.Services.AddSingleton<ServerMetrics>();
        builder.Services.AddSingleton(sp =>
        {
            var registry = new TsdbRegistry(serverOptions.DataRoot);
            if (serverOptions.AutoLoadExistingDatabases)
                registry.LoadExisting();
            return registry;
        });

        // 在应用关闭时优雅释放所有 Tsdb 实例
        builder.Services.AddSingleton<IHostedService>(sp => new RegistryShutdownHook(sp.GetRequiredService<TsdbRegistry>()));
    }

    private static void ConfigureMiddleware(WebApplication app, ServerOptions serverOptions)
    {
        // Bearer 认证（在所有 endpoint 之前）
        app.Use(async (context, next) =>
        {
            var status = BearerAuthMiddleware.Authenticate(context, serverOptions);
            if (status is not null)
            {
                context.Response.StatusCode = status.Value;
                context.Response.ContentType = "application/json; charset=utf-8";
                var err = new ErrorResponse(status.Value == 401 ? "unauthorized" : "forbidden",
                    status.Value == 401 ? "缺失或无效的 Bearer token。" : "权限不足。");
                await JsonSerializer.SerializeAsync(context.Response.Body, err, ServerJsonContext.Default.ErrorResponse).ConfigureAwait(false);
                return;
            }
            await next(context).ConfigureAwait(false);
        });
    }

    private static void MapEndpoints(WebApplication app, ServerOptions serverOptions)
    {
        var registry = app.Services.GetRequiredService<TsdbRegistry>();
        var metrics = app.Services.GetRequiredService<ServerMetrics>();

        // ---- 健康 / 指标 ----
        app.MapGet("/healthz", () =>
        {
            var resp = new HealthResponse("ok", registry.Count, metrics.UptimeSeconds);
            return Results.Json(resp, ServerJsonContext.Default.HealthResponse);
        });

        app.MapGet("/metrics", (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "text/plain; version=0.0.4; charset=utf-8";
            return ctx.Response.WriteAsync(PrometheusFormatter.Render(metrics, registry));
        });

        // ---- 数据库管理 ----
        app.MapGet("/v1/db", (HttpContext ctx) =>
        {
            var resp = new DatabaseListResponse(registry.ListDatabases());
            return Results.Json(resp, ServerJsonContext.Default.DatabaseListResponse);
        });

        app.MapPost("/v1/db", async (HttpContext ctx) =>
        {
            var role = BearerAuthMiddleware.GetRole(ctx);
            if (!BearerAuthMiddleware.IsAdmin(role))
                return ForbiddenResult("仅 admin 可创建数据库。");
            var req = await ReadJsonAsync(ctx, ServerJsonContext.Default.CreateDatabaseRequest).ConfigureAwait(false);
            if (req is null)
                return BadRequestResult("请求体不可为空。");
            if (!TsdbRegistry.IsValidName(req.Name))
                return BadRequestResult($"非法数据库名 '{req.Name}'。");
            var created = registry.TryCreate(req.Name, out _);
            return Results.Json(new DatabaseOperationResponse(req.Name, created ? "created" : "exists"),
                ServerJsonContext.Default.DatabaseOperationResponse,
                statusCode: created ? StatusCodes.Status201Created : StatusCodes.Status200OK);
        });

        app.MapDelete("/v1/db/{db}", (HttpContext ctx, string db) =>
        {
            var role = BearerAuthMiddleware.GetRole(ctx);
            if (!BearerAuthMiddleware.IsAdmin(role))
                return ForbiddenResult("仅 admin 可删除数据库。");
            if (!TsdbRegistry.IsValidName(db))
                return BadRequestResult($"非法数据库名 '{db}'。");
            var dropped = registry.Drop(db);
            return Results.Json(new DatabaseOperationResponse(db, dropped ? "dropped" : "not_found"),
                ServerJsonContext.Default.DatabaseOperationResponse,
                statusCode: dropped ? StatusCodes.Status200OK : StatusCodes.Status404NotFound);
        });

        // ---- SQL ----
        app.MapPost("/v1/db/{db}/sql", async (HttpContext ctx, string db) =>
        {
            if (!TryResolveDatabase(ctx, registry, db, out var tsdb))
                return;
            var req = await ReadJsonAsync(ctx, ServerJsonContext.Default.SqlRequest).ConfigureAwait(false);
            if (req is null)
            {
                await WriteSimpleErrorAsync(ctx, StatusCodes.Status400BadRequest, "bad_request", "请求体不可为空。").ConfigureAwait(false);
                return;
            }
            var role = BearerAuthMiddleware.GetRole(ctx);
            await SqlEndpointHandler.HandleSingleAsync(ctx, tsdb, req, metrics, BearerAuthMiddleware.CanWrite(role)).ConfigureAwait(false);
        });

        app.MapPost("/v1/db/{db}/sql/batch", async (HttpContext ctx, string db) =>
        {
            if (!TryResolveDatabase(ctx, registry, db, out var tsdb))
                return;
            var req = await ReadJsonAsync(ctx, ServerJsonContext.Default.SqlBatchRequest).ConfigureAwait(false);
            if (req is null || req.Statements.Count == 0)
            {
                await WriteSimpleErrorAsync(ctx, StatusCodes.Status400BadRequest, "bad_request", "请求体或 statements 不可为空。").ConfigureAwait(false);
                return;
            }
            var role = BearerAuthMiddleware.GetRole(ctx);
            await SqlEndpointHandler.HandleBatchAsync(ctx, tsdb, req, metrics, BearerAuthMiddleware.CanWrite(role)).ConfigureAwait(false);
        });
    }

    private static bool TryResolveDatabase(HttpContext ctx, TsdbRegistry registry, string db, out Engine.Tsdb tsdb)
    {
        if (!TsdbRegistry.IsValidName(db))
        {
            _ = WriteSimpleErrorAsync(ctx, StatusCodes.Status400BadRequest, "bad_request", $"非法数据库名 '{db}'。");
            tsdb = null!;
            return false;
        }
        if (!registry.TryGet(db, out tsdb))
        {
            _ = WriteSimpleErrorAsync(ctx, StatusCodes.Status404NotFound, "db_not_found", $"数据库 '{db}' 不存在。");
            return false;
        }
        return true;
    }

    private static async Task WriteSimpleErrorAsync(HttpContext ctx, int statusCode, string code, string message)
    {
        if (ctx.Response.HasStarted) return;
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(ctx.Response.Body, new ErrorResponse(code, message),
            ServerJsonContext.Default.ErrorResponse, ctx.RequestAborted).ConfigureAwait(false);
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpContext ctx, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        where T : class
    {
        if (ctx.Request.ContentLength == 0)
            return null;
        try
        {
            return await JsonSerializer.DeserializeAsync(ctx.Request.Body, typeInfo, ctx.RequestAborted).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IResult ForbiddenResult(string message)
        => Results.Json(new ErrorResponse("forbidden", message),
            ServerJsonContext.Default.ErrorResponse, statusCode: StatusCodes.Status403Forbidden);

    private static IResult BadRequestResult(string message)
        => Results.Json(new ErrorResponse("bad_request", message),
            ServerJsonContext.Default.ErrorResponse, statusCode: StatusCodes.Status400BadRequest);
}

internal sealed class RegistryShutdownHook(TsdbRegistry registry) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        registry.Dispose();
        return Task.CompletedTask;
    }
}
