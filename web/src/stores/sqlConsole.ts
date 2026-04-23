import { defineStore } from 'pinia';
import { ref } from 'vue';

export interface PendingSqlExecution {
  db: string;
  sql: string;
  runImmediately: boolean;
}

export const useSqlConsoleStore = defineStore('sqlConsole', () => {
  const pendingExecution = ref<PendingSqlExecution | null>(null);

  /** SQL Console 当前正在编辑的 SQL 文本（M6：供 CopilotDock 作为页面上下文）。 */
  const currentSql = ref<string>('');
  /** SQL Console 当前选中的数据库（M6：供 CopilotDock 同步数据库选择 + 上下文注入）。 */
  const currentDb = ref<string>('');

  function queueExecution(execution: PendingSqlExecution): void {
    pendingExecution.value = execution;
  }

  function consumeExecution(): PendingSqlExecution | null {
    const current = pendingExecution.value;
    pendingExecution.value = null;
    return current;
  }

  function setCurrent(db: string, sql: string): void {
    currentDb.value = db;
    currentSql.value = sql;
  }

  return {
    pendingExecution,
    currentSql,
    currentDb,
    queueExecution,
    consumeExecution,
    setCurrent,
  };
});
