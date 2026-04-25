export interface SonnetDbConnectionProfile {
  id: string;
  label: string;
  kind: 'remote' | 'managed-local';
  baseUrl: string;
  defaultDatabase?: string;
  tokenSecretKey?: string;
  dataRoot?: string;
}

export interface DatabaseListResponse {
  databases: string[];
}

export interface ColumnInfo {
  name: string;
  role: string;
  dataType: string;
}

export interface MeasurementInfo {
  name: string;
  columns: ColumnInfo[];
}

export interface SchemaResponse {
  measurements: MeasurementInfo[];
}

export interface SqlEnd {
  type: 'end';
  rowCount: number;
  recordsAffected: number;
  elapsedMs: number;
}

export interface SqlError {
  type?: 'error';
  code?: string;
  message: string;
}

export interface SqlResultSet {
  columns: string[];
  rows: unknown[][];
  end: SqlEnd | null;
  error: SqlError | null;
  hasColumns: boolean;
}

export interface HealthResponse {
  status: string;
  databases: number;
  uptimeSeconds: number;
  copilotEnabled: boolean;
  copilotReady: boolean;
}

export interface CopilotModelsResponse {
  default: string;
  candidates: string[];
}

export interface CopilotChatEvent {
  type: string;
  message?: string | null;
  answer?: string | null;
  toolName?: string | null;
  toolArguments?: string | null;
  toolResult?: string | null;
}
