<template>
  <div ref="editorContainer" class="sql-editor" />
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { EditorView, keymap, lineNumbers, highlightActiveLine } from '@codemirror/view';
import { EditorState } from '@codemirror/state';
import { defaultKeymap, history, historyKeymap } from '@codemirror/commands';
import { bracketMatching, indentOnInput } from '@codemirror/language';
import {
  closeBrackets, closeBracketsKeymap, autocompletion, completionKeymap,
} from '@codemirror/autocomplete';
import { sql } from '@codemirror/lang-sql';
import { SonnetDbSQL } from './sonnetdb-dialect';

export interface ColumnInfo { name: string; role: string; dataType: string; }
export interface MeasurementInfo { name: string; columns: ColumnInfo[]; }

const props = defineProps<{
  modelValue: string;
  schema?: MeasurementInfo[];
  placeholder?: string;
}>();

const emit = defineEmits<{
  (e: 'update:modelValue', v: string): void;
}>();

const editorContainer = ref<HTMLElement | null>(null);
let view: EditorView | null = null;

function buildSqlSchema(measurements?: MeasurementInfo[]) {
  if (!measurements?.length) return {};
  const tables: Record<string, string[]> = {};
  for (const m of measurements) {
    tables[m.name] = m.columns.map((c) => c.name);
  }
  return tables;
}

function createView(el: HTMLElement) {
  const tables = buildSqlSchema(props.schema);
  const sqlLang = sql({
    dialect: SonnetDbSQL,
    schema: tables,
    upperCaseKeywords: true,
  });

  const startState = EditorState.create({
    doc: props.modelValue,
    extensions: [
      lineNumbers(),
      history(),
      highlightActiveLine(),
      bracketMatching(),
      closeBrackets(),
      indentOnInput(),
      autocompletion(),
      sqlLang,
      keymap.of([
        ...closeBracketsKeymap,
        ...defaultKeymap,
        ...historyKeymap,
        ...completionKeymap,
      ]),
      EditorView.updateListener.of((update) => {
        if (update.docChanged) {
          emit('update:modelValue', update.state.doc.toString());
        }
      }),
      EditorView.theme({
        '&': {
          minHeight: '120px',
          maxHeight: '400px',
          border: '1px solid #e0e0e6',
          borderRadius: '3px',
          fontSize: '13px',
          fontFamily: '"JetBrains Mono", "Fira Code", "Cascadia Code", Consolas, monospace',
        },
        '.cm-scroller': { overflow: 'auto' },
        '.cm-content': { padding: '8px 0' },
        '.cm-focused': { outline: 'none' },
        '&.cm-focused': { borderColor: '#18a058' },
      }),
    ],
  });

  view = new EditorView({ state: startState, parent: el });
}

onMounted(() => {
  if (editorContainer.value) {
    createView(editorContainer.value);
  }
});

onBeforeUnmount(() => {
  view?.destroy();
  view = null;
});

// 外部修改 modelValue 时同步到编辑器（如 AI 生成 SQL 填充）
watch(
  () => props.modelValue,
  (val) => {
    if (!view) return;
    const current = view.state.doc.toString();
    if (current !== val) {
      view.dispatch({
        changes: { from: 0, to: current.length, insert: val },
      });
    }
  },
);

// schema 变化时重建编辑器（数据库切换）
watch(
  () => props.schema,
  () => {
    if (!editorContainer.value) return;
    const content = view?.state.doc.toString() ?? '';
    view?.destroy();
    createView(editorContainer.value);
    if (content) {
      view?.dispatch({
        changes: { from: 0, to: 0, insert: content },
      });
    }
  },
  { deep: true },
);
</script>

<style scoped>
.sql-editor {
  width: 100%;
}
</style>
