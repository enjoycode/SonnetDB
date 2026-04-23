import { defineStore } from 'pinia';
import { ref } from 'vue';

export interface PendingSqlExecution {
  db: string;
  sql: string;
  runImmediately: boolean;
}

export const useSqlConsoleStore = defineStore('sqlConsole', () => {
  const pendingExecution = ref<PendingSqlExecution | null>(null);

  function queueExecution(execution: PendingSqlExecution): void {
    pendingExecution.value = execution;
  }

  function consumeExecution(): PendingSqlExecution | null {
    const current = pendingExecution.value;
    pendingExecution.value = null;
    return current;
  }

  return { pendingExecution, queueExecution, consumeExecution };
});
