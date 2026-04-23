import { SQLDialect, StandardSQL } from '@codemirror/lang-sql';

/**
 * SonnetDB SQL 方言（M9）。
 *
 * 在 `StandardSQL` 的基础上追加 SonnetDB 特有关键字、类型与内建函数：
 * - 关键字：`MEASUREMENT` / `MEASUREMENTS` / `TAG` / `FIELD` / `BUCKET`
 * - 类型：`VECTOR`（含 `VECTOR(N)`）、`FLOAT` / `INT` / `BOOL` / `STRING`
 * - 内建函数：`knn` / `time` / `time_bucket` / `forecast` / `pid_compute` / `pid_tune`
 *
 * 实现：通过 `SQLDialect.define` 合并 `StandardSQL.spec` 与 SonnetDB 关键字字符串，
 * 这样语法高亮（keyword/type/builtinName 三种 token）与 lang-sql 内置的关键字补全
 * 都会自动覆盖 SonnetDB 方言。
 */
export const SonnetDbSQL: SQLDialect = SQLDialect.define({
  ...StandardSQL.spec,
  keywords:
    (StandardSQL.spec.keywords ?? '') +
    ' measurement measurements tag field bucket show describe explain knn',
  types:
    (StandardSQL.spec.types ?? '') + ' vector float int bool string',
  builtin:
    (StandardSQL.spec.builtin ?? '') +
    ' knn time time_bucket forecast pid_compute pid_tune',
});
