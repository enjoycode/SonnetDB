/**
 * 把客户端粘贴的多条 SQL 语句按顶层分号 `;` 切分成单语句数组。
 *
 * 服务端 `SqlParser` 一次只接受一条语句（结尾的分号可选），
 * 因此 SQL Console 在发请求前先做切分。切分逻辑必须忽略：
 * - 单引号 `'...'` 字符串（包含两个连续单引号 `''` 的转义）
 * - 行注释 `-- ...`
 * - 块注释 `/* ... *\/`
 *
 * 标识符用反引号或双引号在 SonnetDB 暂不支持，这里也顺带忽略。
 *
 * 返回值：每个元素是去掉首尾空白、且非空的单条 SQL（不带尾随分号）。
 */
export function splitSqlStatements(input: string): string[] {
  if (!input) return [];
  const out: string[] = [];
  let buf = '';
  let i = 0;
  const n = input.length;

  while (i < n) {
    const ch = input[i];

    // 行注释：到行尾
    if (ch === '-' && input[i + 1] === '-') {
      buf += ch;
      i += 1;
      while (i < n && input[i] !== '\n') {
        buf += input[i];
        i += 1;
      }
      continue;
    }

    // 块注释：到 */（不嵌套）
    if (ch === '/' && input[i + 1] === '*') {
      buf += ch;
      buf += input[i + 1];
      i += 2;
      while (i < n && !(input[i] === '*' && input[i + 1] === '/')) {
        buf += input[i];
        i += 1;
      }
      if (i < n) {
        buf += input[i];
        buf += input[i + 1];
        i += 2;
      }
      continue;
    }

    // 单引号字符串：内部两个单引号 '' 视为转义，不结束字符串
    if (ch === "'") {
      buf += ch;
      i += 1;
      while (i < n) {
        if (input[i] === "'" && input[i + 1] === "'") {
          buf += "''";
          i += 2;
          continue;
        }
        if (input[i] === "'") {
          buf += "'";
          i += 1;
          break;
        }
        buf += input[i];
        i += 1;
      }
      continue;
    }

    // 顶层分号 → 切分
    if (ch === ';') {
      const stmt = buf.trim();
      if (stmt.length > 0) out.push(stmt);
      buf = '';
      i += 1;
      continue;
    }

    buf += ch;
    i += 1;
  }

  const tail = buf.trim();
  if (tail.length > 0) out.push(tail);
  return out;
}
