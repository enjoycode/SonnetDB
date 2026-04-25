export interface CopilotMessage {
  role: string;
  content: string;
}

export interface CopilotKnowledgeStatus {
  enabled: boolean;
  embeddingProvider: string;
  embeddingFallback: boolean;
  vectorDimension: number;
  docsRoots: string[];
  indexedFiles: number;
  indexedChunks: number;
  lastIngestedUtc: string | null;
  skillCount: number;
}

interface ParsedErrorResponse {
  code: string;
  message: string;
}

const CopilotReadinessHints: Record<string, string> = {
  disabled: 'Copilot 当前未启用，请在「Copilot 设置」中启用后再试。',
  'chat.endpoint_invalid': 'Copilot Chat 服务地址未配置或格式不正确。请打开「Copilot 设置」，确认服务商、服务地址、API Key 与模型已保存。',
  'chat.api_key_missing': 'Copilot Chat API Key 还没填写。请在「Copilot 设置」填写并保存后再试。',
  'chat.model_missing': 'Copilot Chat 模型还没填写。请在「Copilot 设置」选择或输入模型后再试。',
  'chat.provider_unsupported': '当前 Copilot Chat provider 暂不支持。请在「Copilot 设置」切换为 OpenAI 兼容服务后再试。',
  'embedding.endpoint_invalid': 'Copilot 知识库向量服务地址未配置或格式不正确。可改用 builtin embedding，或在「Copilot 设置」补齐 OpenAI 兼容 embedding 配置。',
  'embedding.api_key_missing': 'Copilot 知识库向量服务缺少 API Key。请补齐 embedding API Key，或改用 builtin embedding。',
  'embedding.model_missing': 'Copilot 知识库向量模型未配置。请填写 embedding 模型，或改用 builtin embedding。',
  'embedding.local_model_path_missing': '本地 embedding 模型路径未配置。请配置模型路径，或改用 builtin embedding。',
  'embedding.local_model_not_found': '本地 embedding 模型文件不存在。请检查模型路径，或改用 builtin embedding。',
  'embedding.provider_unsupported': '当前 embedding provider 暂不支持。请改用 builtin、local 或 openai。',
};

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function stringValue(value: unknown): string {
  return typeof value === 'string' ? value.trim() : '';
}

function parseErrorObject(value: unknown): ParsedErrorResponse | null {
  if (!isRecord(value)) return null;

  const nestedError = value.error;
  if (isRecord(nestedError)) {
    return {
      code: stringValue(nestedError.code) || stringValue(nestedError.type),
      message: stringValue(nestedError.message) || stringValue(value.message),
    };
  }

  return {
    code: stringValue(nestedError) || stringValue(value.code),
    message: stringValue(value.message),
  };
}

function parseErrorResponse(text: string): ParsedErrorResponse | null {
  const body = text.trim();
  if (!body) return null;

  try {
    return parseErrorObject(JSON.parse(body));
  } catch {
    const start = body.indexOf('{');
    const end = body.lastIndexOf('}');
    if (start < 0 || end <= start) return null;

    try {
      return parseErrorObject(JSON.parse(body.slice(start, end + 1)));
    } catch {
      return null;
    }
  }
}

function extractReadinessReason(message: string): string {
  return /(?:chat|embedding)\.[a-z0-9_.]+|disabled/i.exec(message)?.[0].toLowerCase() ?? '';
}

function formatReadinessError(reason: string): string {
  return CopilotReadinessHints[reason]
    ?? 'Copilot 还没准备好，请检查「Copilot 设置」中的服务地址、API Key、模型和知识库配置。';
}

function formatProviderStatusError(status: number, providerMessage: string): string {
  if (status === 401 || status === 403) {
    return 'Copilot 模型服务拒绝了请求，请检查「Copilot 设置」中的 API Key、服务商权限和模型访问权限。';
  }

  if (status === 404) {
    return 'Copilot 模型服务找不到当前模型，请检查「Copilot 设置」中的模型名是否正确。';
  }

  if (status === 429) {
    return 'Copilot 模型服务额度或频率已达上限，请稍后重试，或切换到可用额度的模型。';
  }

  if (status >= 500) {
    return providerMessage
      ? `Copilot 模型服务暂时不可用：${providerMessage}`
      : 'Copilot 模型服务暂时不可用，请稍后重试。';
  }

  return providerMessage
    ? `Copilot 模型服务返回错误：${providerMessage}`
    : 'Copilot 模型服务返回错误，请检查配置后再试。';
}

function toUserFacingCopilotError(message: string): string {
  const raw = message.trim();
  if (!raw) return 'Copilot 请求失败，请稍后重试。';

  const readinessReason = extractReadinessReason(raw);
  if (readinessReason) return formatReadinessError(readinessReason);

  if (/endpoint is not configured correctly/i.test(raw)) {
    return formatReadinessError('chat.endpoint_invalid');
  }

  if (/api key is missing/i.test(raw)) {
    return formatReadinessError('chat.api_key_missing');
  }

  if (/model is missing/i.test(raw)) {
    return formatReadinessError('chat.model_missing');
  }

  if (/empty completion/i.test(raw)) {
    return 'Copilot 模型服务没有返回内容，请稍后重试，或切换到另一个模型。';
  }

  const providerMatch = /Copilot chat provider returned\s+(\d+):\s*([\s\S]*)$/i.exec(raw);
  if (providerMatch) {
    const status = Number(providerMatch[1]);
    const payload = parseErrorResponse(providerMatch[2] ?? '');
    return formatProviderStatusError(status, payload?.message ?? '');
  }

  const payload = parseErrorResponse(raw);
  if (payload?.message) return payload.message;

  return raw;
}

async function readCopilotHttpError(resp: Response, action: string): Promise<string> {
  const text = await resp.text();
  const payload = parseErrorResponse(text);
  const code = payload?.code ?? '';
  const serverMessage = payload?.message ?? '';

  if (code === 'copilot_not_ready') {
    return formatReadinessError(extractReadinessReason(serverMessage));
  }

  if (code === 'copilot_disabled') {
    return formatReadinessError('disabled');
  }

  if (serverMessage) {
    return toUserFacingCopilotError(serverMessage);
  }

  if (resp.status === 401) return '登录状态已失效，请重新登录后再试。';
  if (resp.status === 403) return '当前账号没有执行此 Copilot 操作的权限。';
  if (resp.status === 404) return 'Copilot 请求的资源不存在，请刷新页面后再试。';
  if (resp.status === 409) return 'Copilot 当前不可用，请在「Copilot 设置」中确认已启用。';
  if (resp.status === 503) return 'Copilot 暂时不可用，请稍后重试，或检查「Copilot 设置」中的服务配置。';

  return `${action}失败（HTTP ${resp.status}）。请稍后重试。`;
}

/** 读取知识库状态（embedding provider / 已索引文件数 / 最近摄入时间 等）。 */
export async function fetchCopilotKnowledgeStatus(token: string): Promise<CopilotKnowledgeStatus> {
  const resp = await fetch('/v1/copilot/knowledge/status', {
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!resp.ok) {
    throw new Error(await readCopilotHttpError(resp, '读取知识库状态'));
  }
  return (await resp.json()) as CopilotKnowledgeStatus;
}

/** 触发知识库重新索引（force=true 时忽略增量指纹强制全量摄入）。 */
export async function triggerCopilotDocsIngest(token: string, force = false): Promise<void> {
  const resp = await fetch('/v1/copilot/docs/ingest', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify({ force, dryRun: false }),
  });
  if (!resp.ok) {
    throw new Error(await readCopilotHttpError(resp, '触发知识库索引'));
  }
}

/** 服务端可用 chat 模型（M8）。 */
export interface CopilotModels {
  default: string;
  candidates: string[];
}

/** 拉取 Copilot 可用 chat 模型列表（默认 + 候选）。 */
export async function fetchCopilotModels(token: string): Promise<CopilotModels> {
  const resp = await fetch('/v1/copilot/models', {
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!resp.ok) {
    throw new Error(await readCopilotHttpError(resp, '读取 Copilot 模型列表'));
  }
  return (await resp.json()) as CopilotModels;
}


export interface CopilotCitation {
  id: string;
  kind: string;
  title: string;
  source: string;
  snippet: string;
}

export interface CopilotChatEvent {
  type: string;
  message?: string;
  answer?: string;
  toolName?: string;
  toolArguments?: string;
  toolResult?: string;
  skillNames?: string[];
  toolNames?: string[];
  citations?: CopilotCitation[];
  attempt?: number;
}

export interface CopilotChatRequest {
  db?: string;
  messages: CopilotMessage[];
  docsK?: number;
  skillsK?: number;
  /**
   * M7：权限模式。
   * - `read-only`（默认）：服务端将 `canWrite` 强制置为 false，agent 不会调用 execute_sql 写入。
   * - `read-write`：在凭据本身具备写权限的前提下允许 execute_sql 写入。
   */
  mode?: 'read-only' | 'read-write';
  /**
   * M8：本次请求使用的 chat 模型名；为空时使用服务端 `CopilotChatOptions.Model` 默认。
   */
  model?: string;
}

export async function* streamCopilotChat(
  token: string,
  request: CopilotChatRequest,
  signal?: AbortSignal,
): AsyncGenerator<CopilotChatEvent, void, unknown> {
  let resp: Response;
  try {
    resp = await fetch('/v1/copilot/chat/stream', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${token}`,
      },
      body: JSON.stringify(request),
      signal,
    });
  } catch (e) {
    if (signal?.aborted) throw e;
    throw new Error('无法连接 Copilot 服务，请确认 SonnetDB 服务仍在运行并稍后重试。');
  }

  if (!resp.ok) {
    throw new Error(await readCopilotHttpError(resp, 'Copilot 请求'));
  }

  const reader = resp.body?.getReader();
  if (!reader) throw new Error('无法读取 Copilot 响应流');

  const decoder = new TextDecoder();
  let buffer = '';

  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });
      const lines = buffer.split('\n');
      buffer = lines.pop() ?? '';

      for (const line of lines) {
        if (!line.startsWith('data: ')) continue;
        const data = line.slice('data: '.length);
        if (!data || data === '[DONE]') {
          if (data === '[DONE]') return;
          continue;
        }

        try {
          const event = JSON.parse(data) as CopilotChatEvent;
          if (event.type === 'error' && event.message) {
            yield { ...event, message: toUserFacingCopilotError(event.message) };
          } else {
            yield event;
          }
        } catch {
          // 忽略无法解析的中间行
        }
      }
    }
  } finally {
    reader.releaseLock();
  }
}
