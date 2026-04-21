import type { AxiosInstance } from 'axios';

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

/** 获取指定数据库的 schema（measurement 列表及列定义），供 SQL 自动补全使用。 */
export async function fetchSchema(api: AxiosInstance, db: string): Promise<SchemaResponse> {
  const resp = await api.get<SchemaResponse>(`/v1/db/${encodeURIComponent(db)}/schema`);
  return resp.data;
}
