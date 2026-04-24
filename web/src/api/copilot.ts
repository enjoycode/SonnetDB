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

/** 读取知识库状态（embedding provider / 已索引文件数 / 最近摄入时间 等）。 */
export async function fetchCopilotKnowledgeStatus(token: string): Promise<CopilotKnowledgeStatus> {
  const resp = await fetch('/v1/copilot/knowledge/status', {
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!resp.ok) {
    const text = await resp.text();
    throw new Error(`知识库状态读取失败 ${resp.status}: ${text}`);
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
    const text = await resp.text();
    throw new Error(`触发知识库索引失败 ${resp.status}: ${text}`);
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
    const text = await resp.text();
    throw new Error(`Copilot 模型列表读取失败 ${resp.status}: ${text}`);
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
  const resp = await fetch('/v1/copilot/chat/stream', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify(request),
    signal,
  });

  if (!resp.ok) {
    const text = await resp.text();
    throw new Error(`Copilot 请求失败 ${resp.status}: ${text}`);
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
          yield JSON.parse(data) as CopilotChatEvent;
        } catch {
          // 忽略无法解析的中间行
        }
      }
    }
  } finally {
    reader.releaseLock();
  }
}
