import type { AxiosInstance } from 'axios';

export interface AiStatusResponse {
  enabled: boolean;
  provider: string;
  model: string;
}

export interface AiConfigResponse {
  enabled: boolean;
  provider: string;
  model: string;
  timeoutSeconds: number;
  hasApiKey: boolean;
}

export interface AiMessage {
  role: string;
  content: string;
}

/** 获取 AI 助手启用状态（任何已认证用户）。 */
export async function fetchAiStatus(api: AxiosInstance): Promise<AiStatusResponse> {
  const resp = await api.get<AiStatusResponse>('/v1/ai/status');
  return resp.data;
}

/** 获取 AI 配置（admin only）。 */
export async function fetchAiConfig(api: AxiosInstance): Promise<AiConfigResponse> {
  const resp = await api.get<AiConfigResponse>('/v1/admin/ai-config');
  return resp.data;
}

/** 保存 AI 配置（admin only）。apiKey 为 undefined 时保留原密钥。 */
export async function saveAiConfig(
  api: AxiosInstance,
  config: {
    enabled: boolean;
    provider: string;
    apiKey?: string;
    model: string;
    timeoutSeconds: number;
  },
): Promise<void> {
  await api.put('/v1/admin/ai-config', {
    enabled: config.enabled,
    provider: config.provider,
    apiKey: config.apiKey ?? null,
    model: config.model,
    timeoutSeconds: config.timeoutSeconds,
  });
}

/**
 * 流式 AI 聊天：以 AsyncGenerator 逐 token yield。
 * 使用 fetch（而非 axios）以支持 ReadableStream。
 */
export async function* streamAiChat(
  token: string,
  messages: AiMessage[],
  db?: string,
  mode = 'chat',
): AsyncGenerator<string, void, unknown> {
  const resp = await fetch('/v1/ai/chat', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify({ messages, db, mode }),
  });

  if (!resp.ok) {
    const text = await resp.text();
    throw new Error(`AI 请求失败 ${resp.status}: ${text}`);
  }

  const reader = resp.body?.getReader();
  if (!reader) throw new Error('无法读取 AI 响应流');

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
        if (data === '[DONE]') return;

        try {
          const obj = JSON.parse(data) as { token?: string; error?: string };
          if (obj.error) throw new Error(obj.error);
          if (obj.token) yield obj.token;
        } catch {
          // 忽略解析失败的行
        }
      }
    }
  } finally {
    reader.releaseLock();
  }
}
