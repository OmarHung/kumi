// 樣式規範的自動檢查（docs/DESIGN.md 第 13 節）。npm run lint 會跑：只用 Node 內建的模組，不加套件。
// 擋下：變數檔以外的顏色、沒用變數的字級／圓角／層級／動畫時間／有模糊的陰影、清單以外的斷點、global.css 以外的 !important、
// 允許清單以外的 style 屬性與用程式寫樣式、元件裡原生的下拉（<select>、<option>），
// HTML 裡行內的 <script>、<style>（內容安全政策是 default-src 'self'），
// 以及元件用到、樣式檔裡卻沒有定義的 class（CSS Modules 拿不存在的名稱只會得到 undefined，畫面默默少一段樣式）。
// 區域變數（同一個檔案裡定義的 --x）會展開成它的值再檢查：寫死的尺寸不能只是換個名字藏起來。
import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join, posix, sep } from 'node:path';
import process from 'node:process';
import { fileURLToPath, pathToFileURL } from 'node:url';

const TOKENS_FILE = 'src/styles/tokens.css';
const GLOBAL_FILE = 'src/styles/global.css';

// React 的 style 物件與用程式寫樣式只准用在執行時才算得出來的數值：浮層的座標（拖曳的位移由 dnd-kit 自己設）、
// 時間軸上依日期排的東西的位置與寬度（只設 --col、--span、--days 三個變數），
// 項目面板拖拉調整的寬度（SidePanelLayout 設一個 CSS 變數）、表格使用者拉的欄寬（Table.tsx 的 col 寬度與捲動區塊上的 CSS 變數）、
// 項目的浮動視窗拖拉調整的寬度（ModalFrame 的外框上一個 CSS 變數，FloatingPanel 給）、
// 右下角 AI 助理面板拖拉調整的大小（PanelWindow 在根元素上設兩個 CSS 變數，新通知的提示要讀），
// 右下角 AI 助理按鈕被使用者拖到的位置（useLauncherAnchor 在根元素上設 --launcher-right、--launcher-bottom；位置執行時才有，面板要讀同一份），
// 整頁 AI 助理的對話列表拖拉調整的寬度（AssistantWorkspace 在外框上設一個 CSS 變數；寬度由使用者拖，執行時才算得出來），
// 專案總覽的狀態分段橫條（StateBar 在每一段上設 --count：段的長度是項目數的比例，執行時才算得出來）。
const STYLE_ALLOWED = new Set([
  'src/components/Dialog/ModalFrame.tsx',
  'src/components/Floating/Floating.tsx',
  'src/components/SidePanel/SidePanelLayout.tsx',
  'src/components/Table/Table.tsx',
  'src/features/assistant-panel/PanelWindow.tsx',
  'src/features/assistant-panel/useLauncherAnchor.ts',
  'src/features/assistant/AssistantWorkspace.tsx',
  'src/features/overview/StateBar.tsx',
  'src/features/timeline/At.tsx',
]);
// 原生的下拉（<select>、<option>）：選項清單是作業系統畫的，樣式與深色模式管不到，一律用 components/Select（規範 7.3）。
// 允許清單是空的：要加先改規範。
const NATIVE_SELECT_ALLOWED = new Set([]);
const NATIVE_SELECT_TAG = /<(select|option)(?=[\s/>])/g;

const ALLOWED_BREAKPOINTS = new Set(['max-width:48rem', 'min-width:64rem', 'min-width:80rem']);
const CSS_KEYWORDS = new Set(['inherit', 'initial', 'unset', 'revert', 'revert-layer']);
const Z_LITERALS = new Set(['auto', '-1', '0', '1', '2', '3']);
const THIN_LINES = new Set(['1px', '2px', '3px']);
const MAX_LOCAL_DEPTH = 5;
const MAX_VARIANTS = 16;

const NAMED_COLORS = new Set(
  (
    'aliceblue antiquewhite aqua aquamarine azure beige bisque black blanchedalmond blue blueviolet brown burlywood ' +
    'cadetblue chartreuse chocolate coral cornflowerblue cornsilk crimson cyan darkblue darkcyan darkgoldenrod darkgray ' +
    'darkgreen darkgrey darkkhaki darkmagenta darkolivegreen darkorange darkorchid darkred darksalmon darkseagreen ' +
    'darkslateblue darkslategray darkslategrey darkturquoise darkviolet deeppink deepskyblue dimgray dimgrey dodgerblue ' +
    'firebrick floralwhite forestgreen fuchsia gainsboro ghostwhite gold goldenrod gray green greenyellow grey honeydew ' +
    'hotpink indianred indigo ivory khaki lavender lavenderblush lawngreen lemonchiffon lightblue lightcoral lightcyan ' +
    'lightgoldenrodyellow lightgray lightgreen lightgrey lightpink lightsalmon lightseagreen lightskyblue lightslategray ' +
    'lightslategrey lightsteelblue lightyellow lime limegreen linen magenta maroon mediumaquamarine mediumblue ' +
    'mediumorchid mediumpurple mediumseagreen mediumslateblue mediumspringgreen mediumturquoise mediumvioletred ' +
    'midnightblue mintcream mistyrose moccasin navajowhite navy oldlace olive olivedrab orange orangered orchid ' +
    'palegoldenrod palegreen paleturquoise palevioletred papayawhip peachpuff peru pink plum powderblue purple ' +
    'rebeccapurple red rosybrown royalblue saddlebrown salmon sandybrown seagreen seashell sienna silver skyblue ' +
    'slateblue slategray slategrey snow springgreen steelblue tan teal thistle tomato turquoise violet wheat white ' +
    'whitesmoke yellow yellowgreen'
  ).split(' '),
);

const HEX_COLOR = /#[0-9a-f]{3,8}\b/i;
const COLOR_FUNCTION = /\b(rgba?|hsla?|hwb|lab|lch|oklab|oklch|color|color-mix|light-dark)\(/i;
const COLOR_PROPERTY = /(color|background|border|outline|shadow|fill|stroke|decoration|caret|accent|rule)/;
// 負號前面不能是字（動畫名稱裡的「pulse-2s」不算時間）。
const TIME_LITERAL = /(?<![\w.-])(-?\d*\.?\d+)(ms|s)\b/g;
// 「-1」前面是字或連字號時是變數名稱的一部分（var(--space-1)），不是負號。
const NEGATIVE_TIME_CALC = /calc\([^;]*(\*\s*-|(?<![\w-])-\s*1\b|-\s*var\(--duration)/;
// 區域變數只有值裡提到時間（--duration-* 或寫死的 ms／s）才當成動畫時間檢查：calc(-1 * var(--space-2)) 是長度。
const MENTIONS_TIME = /var\(--duration|(?<![\w.-])-?\d*\.?\d+m?s\b/;
const LENGTH_LITERAL = /(?<![\w.-])\d*\.?\d+(px|rem|em|pt|vw|vh)\b/g;
const SHADOW_LENGTH = /(?<![\w.-])-?\d*\.?\d+(px|rem|em)?(?![\w.%])/g;
const IMPORTANT = /!\s*important\s*$/i;
// var(--x) 或 var(--x, 備用值)；備用值裡最多再一層括號（例如另一個 var()）。
const VAR_REFERENCE = /var\(\s*(--[\w-]+)\s*(?:,((?:[^()]|\([^()]*\))*))?\)/g;

function violation(file, line, message) {
  return { file, line, message };
}

/** 註解換成空白（換行留著，行號才對得上）。 */
function blankComments(source) {
  return source.replace(/\/\*[\s\S]*?\*\//g, (comment) => comment.replace(/[^\n]/g, ' '));
}

/** 去掉 var()：沒有備用值的整個拿掉，有備用值的換成備用值（備用值裡寫死的東西也要擋）。 */
function withoutVars(value) {
  let current = value;
  for (let depth = 0; depth < MAX_LOCAL_DEPTH; depth += 1) {
    const next = current.replace(VAR_REFERENCE, (_, _name, fallback) => ` ${fallback ?? ''} `);
    if (next === current) break;
    current = next;
  }
  return current;
}

/** 字串與 url() 換成空的：url(#abc) 不是顏色，字串裡的文字也不是值。 */
function withoutLiterals(value) {
  return value.replace(/(['"])(?:\\.|(?!\1).)*\1/g, '""').replace(/url\([^)]*\)/gi, 'url()');
}

/**
 * 把同一個檔案裡定義的區域變數換成它的值（換到沒有區域變數為止）；變數檔的變數留著。
 * 同一個名字在不同的規則裡定義了不同的值時，每一種都展開，每一種都要通過。
 */
function expandLocals(value, locals, depth = 0) {
  if (depth >= MAX_LOCAL_DEPTH) return [value];
  for (const match of value.matchAll(VAR_REFERENCE)) {
    const definitions = locals.get(match[1]);
    if (definitions !== undefined) {
      const before = value.slice(0, match.index);
      const after = value.slice(match.index + match[0].length);
      return definitions
        .flatMap((definition) => expandLocals(`${before}(${definition})${after}`, locals, depth + 1))
        .slice(0, MAX_VARIANTS);
    }
  }
  return [value];
}

function lineAt(source, index) {
  return source.slice(0, index).split('\n').length;
}

/** 把 CSS 切成一段一段：結尾是 { 的是選擇器或 @ 規則，結尾是 ; 或 } 的是宣告。 */
function cssStatements(source) {
  const statements = [];
  let start = 0;
  let quote = null;
  let parens = 0;
  for (let index = 0; index < source.length; index += 1) {
    const char = source[index];
    if (quote !== null) {
      if (char === quote && source[index - 1] !== '\\') quote = null;
    } else if (char === '"' || char === "'") {
      quote = char;
    } else if (char === '(' || char === ')') {
      parens += char === '(' ? 1 : -1;
    } else if (parens === 0 && (char === '{' || char === '}' || char === ';')) {
      const text = source.slice(start, index);
      const offset = text.search(/\S/);
      if (offset >= 0) statements.push({ text: text.trim(), line: lineAt(source, start + offset), end: char });
      start = index + 1;
    }
  }
  return statements;
}

function splitDeclaration(text) {
  const colon = text.indexOf(':');
  if (text.startsWith('@') || colon < 0) return null;
  return { property: text.slice(0, colon).trim().toLowerCase(), rawValue: text.slice(colon + 1).trim() };
}

/** 同一個檔案裡定義的區域變數（--x: 值）。 */
function localVariables(statements) {
  const locals = new Map();
  for (const statement of statements) {
    const declaration = statement.end === '{' ? null : splitDeclaration(statement.text);
    if (declaration?.property.startsWith('--')) {
      const definitions = locals.get(declaration.property) ?? [];
      locals.set(declaration.property, [...definitions, declaration.rawValue.replace(IMPORTANT, '').trim()]);
    }
  }
  return locals;
}

function checkMedia(file, statement) {
  if (!statement.text.startsWith('@media')) return [];
  const condition = statement.text.slice('@media'.length);
  const problems = [];
  for (const match of condition.matchAll(/\((min|max)-width\s*:\s*([^)]+)\)/g)) {
    const key = `${match[1]}-width:${match[2].trim()}`;
    if (!ALLOWED_BREAKPOINTS.has(key)) {
      problems.push(`斷點只能用 max-width: 48rem 與 min-width: 64rem／80rem（寫的是 ${match[0]}）`);
    }
  }
  if (/[<>]=?\s*width\b|\bwidth\s*[<>]/.test(condition)) {
    problems.push('斷點不要用範圍的寫法：一律寫成 max-width: 48rem 或 min-width: 64rem／80rem');
  }
  if (/device-(width|height)/.test(condition)) {
    problems.push('斷點看的是視窗的寬度，不要用 device-width／device-height');
  }
  return problems.map((message) => violation(file, statement.line, message));
}

function colorProblem(property, bare) {
  const literal = bare.match(HEX_COLOR) ?? bare.match(COLOR_FUNCTION);
  if (literal) return `顏色只能寫在 tokens.css，這裡要用 --color-* 變數（寫的是 ${literal[0]}）`;
  if (!COLOR_PROPERTY.test(property)) return null;
  const named = (bare.toLowerCase().match(/[a-z]+/g) ?? []).find((word) => NAMED_COLORS.has(word));
  return named === undefined ? null : `顏色只能寫在 tokens.css，這裡要用 --color-* 變數（寫的是 ${named}）`;
}

/** 字級只接受單一的 var(--text-*)（或區域變數展開後是它）與 inherit 這類關鍵字。 */
function isTextSize(value, locals, depth = 0) {
  if (CSS_KEYWORDS.has(value)) return true;
  const single = value.match(/^var\((--[\w-]+)\)$/);
  if (single === null) return false;
  if (single[1].startsWith('--text-')) return true;
  const definitions = locals.get(single[1]);
  return definitions !== undefined && depth < MAX_LOCAL_DEPTH && definitions.every((definition) => isTextSize(definition, locals, depth + 1));
}

function fontProblem(property, value, locals) {
  if (property === 'font-size') {
    return isTextSize(value, locals) ? null : `字級只能用單一的 var(--text-*)（寫的是 ${value}）`;
  }
  if (property === 'font' && expandLocals(value, locals).some((variant) => withoutVars(variant).match(LENGTH_LITERAL))) {
    return `字級要用 --text-* 變數（寫的是 font: ${value}）`;
  }
  return null;
}

function radiusAndLayerProblem(property, value, expanded) {
  if (property.endsWith('radius')) {
    const lengths = (withoutVars(expanded).match(LENGTH_LITERAL) ?? []).filter((length) => !THIN_LINES.has(length));
    return lengths.length > 0 ? `圓角要用 --radius-* 變數（寫的是 ${value}）` : null;
  }
  if (property === 'z-index') {
    const trimmed = expanded.replace(/^\((.*)\)$/, '$1').trim();
    const isAllowed = expanded.includes('var(--z-') || Z_LITERALS.has(trimmed) || CSS_KEYWORDS.has(trimmed);
    return isAllowed ? null : `層級要用 --z-* 變數，元件內部只能寫 1、2、3（寫的是 ${value}）`;
  }
  return null;
}

function timeProblem(property, expanded) {
  const isTimed =
    /^(transition|animation)(-duration|-delay)?$/.test(property) || (property.startsWith('--') && MENTIONS_TIME.test(expanded));
  if (!isTimed) return null;
  if (NEGATIVE_TIME_CALC.test(expanded)) return '不要用負的延遲：動畫一律從頭開始';
  const literal = [...withoutVars(expanded).matchAll(TIME_LITERAL)].find((match) => Number(match[1]) !== 0);
  if (literal === undefined) return null;
  return Number(literal[1]) < 0 ? `不要用負的延遲（寫的是 ${literal[0]}）` : `動畫時間要用 --duration-* 變數（寫的是 ${literal[0]}）`;
}

/** 以最外層的逗號切開（括號裡的逗號不算）。 */
function splitTopLevel(value) {
  const parts = [];
  let depth = 0;
  let start = 0;
  [...value].forEach((char, index) => {
    if (char === '(') depth += 1;
    if (char === ')') depth -= 1;
    if (char === ',' && depth === 0) {
      parts.push(value.slice(start, index));
      start = index + 1;
    }
  });
  return [...parts, value.slice(start)].map((part) => part.trim());
}

/** 有模糊的陰影一定用 --shadow-*；沒有模糊的環線（inset 0 0 0 2px …）可以直接寫，顏色要用變數。 */
function shadowProblem(property, expanded) {
  if ((property !== 'box-shadow' && property !== 'text-shadow') || expanded === 'none' || CSS_KEYWORDS.has(expanded)) {
    return null;
  }
  for (const layer of splitTopLevel(expanded)) {
    if (layer.includes('var(--shadow-')) continue;
    const blur = (withoutVars(layer).match(SHADOW_LENGTH) ?? [])[2];
    if (blur !== undefined && Number.parseFloat(blur) !== 0) return `有模糊的陰影要用 --shadow-* 變數（寫的是 ${layer}）`;
    if (!layer.includes('var(--')) return `環線的顏色要用 --color-* 變數（寫的是 ${layer}）`;
  }
  return null;
}

/** 每一種展開的結果都檢查，回第一個問題。 */
function firstProblem(variants, check) {
  for (const variant of variants) {
    const problem = check(variant);
    if (problem !== null) return problem;
  }
  return null;
}

function tokenProblems(property, value, locals, file) {
  const variants = expandLocals(value, locals);
  const bare = withoutVars(withoutLiterals(value));
  const problems = [colorProblem(property, bare)];
  if (!property.startsWith('--')) {
    problems.push(
      fontProblem(property, value, locals),
      firstProblem(variants, (variant) => radiusAndLayerProblem(property, value, variant)),
      firstProblem(variants, (variant) => shadowProblem(property, variant)),
    );
  }
  if (file !== GLOBAL_FILE) problems.push(firstProblem(variants, (variant) => timeProblem(property, variant)));
  return problems;
}

function checkDeclaration(file, statement, locals) {
  const declaration = splitDeclaration(statement.text);
  if (declaration === null) return [];
  const { property, rawValue } = declaration;
  const value = rawValue.replace(IMPORTANT, '').trim();
  const problems = [];
  if (IMPORTANT.test(rawValue) && file !== GLOBAL_FILE) problems.push('!important 只准出現在 global.css');
  if (file !== TOKENS_FILE) problems.push(...tokenProblems(property, value, locals, file));
  return problems.filter((message) => message !== null).map((message) => violation(file, statement.line, message));
}

/** 一個 CSS 檔的問題；file 是相對於 web/ 的路徑，用來判斷變數檔與 global.css 的例外。 */
export function checkCss(file, source) {
  const statements = cssStatements(blankComments(source));
  const locals = localVariables(statements);
  return statements.flatMap((statement) =>
    statement.end === '{' ? checkMedia(file, statement) : checkDeclaration(file, statement, locals),
  );
}

function findAll(file, source, pattern, message) {
  return [...source.matchAll(pattern)].map((match) => violation(file, lineAt(source, match.index), message));
}

// 註解，或單行的字串（'…'、"…"）與樣板字串（`…`）：先遇到哪一個就是哪一個。
const SCRIPT_TOKENS = /\/\/[^\n]*|\/\*[\s\S]*?\*\/|'(?:\\.|[^'\\\n])*'|"(?:\\.|[^"\\\n])*"|`(?:\\.|[^`\\])*`/g;

/** 註解換成空白；字串留著引號、內容換成空白（JSX 的 style="…" 屬性名稱在引號外面，照樣找得到）。 */
function scriptCode(source, { keepStrings }) {
  return source.replace(SCRIPT_TOKENS, (token) => {
    const isComment = token.startsWith('//') || token.startsWith('/*');
    if (!isComment && keepStrings) return token;
    const blank = token.replace(/[^\n]/g, ' ');
    return isComment ? blank : `${token[0]}${blank.slice(1, -1)}${token.at(-1)}`;
  });
}

const STYLE_WRITES = [
  [/(?<![\w.-])(?<!(?:const|let|var)\s+)style\s*=\s*[{"']/g, 'style 屬性只准用在浮層的座標（允許清單在 scripts/check-styles.mjs），其他用 class'],
  [/\.style\.(?!setProperty\b)[A-Za-z]+\s*=(?!=)/g, '不要用程式寫 element.style：只准用在浮層的座標，其他用 class'],
  [/\.style\.setProperty\s*\(/g, '不要用 style.setProperty：只准用在浮層的座標，其他用 class'],
  [/\.style\s*=(?!=)/g, '不要用程式寫 element.style：只准用在浮層的座標，其他用 class'],
  [/\bcssText\s*=(?!=)/g, '不要用 cssText 設樣式：只准用在浮層的座標，其他用 class'],
];

/** 元件（.tsx）裡的 <select>、<option> 標籤；字串與註解裡的不算（code 已經換成空白）。 */
function nativeSelectProblems(file, code) {
  if (!file.endsWith('.tsx') || NATIVE_SELECT_ALLOWED.has(file)) return [];
  return findAll(file, code, NATIVE_SELECT_TAG, '不要直接寫 <select>／<option>：下拉一律用 components/Select（docs/DESIGN.md 7.3）');
}

/**
 * 元件與程式：style 屬性與用程式寫樣式只准出現在允許清單裡的檔案。setAttribute('style', …) 一律不行。
 * 元件裡不准有原生的下拉。
 */
export function checkScript(file, source) {
  const withStrings = scriptCode(source, { keepStrings: true });
  const code = scriptCode(source, { keepStrings: false });
  const problems = [
    ...findAll(file, withStrings, /setAttribute\(\s*['"]style['"]/g, '不要用 setAttribute 寫 style：內容安全政策不允許'),
    ...nativeSelectProblems(file, code),
  ];
  if (STYLE_ALLOWED.has(file)) return problems.sort((a, b) => a.line - b.line);
  const writes = STYLE_WRITES.flatMap(([pattern, message]) => findAll(file, code, pattern, message));
  const lines = new Set();
  // 同一行被兩條規則抓到時只報一次。
  const unique = writes.filter((item) => !lines.has(item.line) && lines.add(item.line));
  return [...unique, ...problems].sort((a, b) => a.line - b.line);
}

// 用相對路徑匯入的樣式檔：import styles from './A.module.css'（專案裡的樣式檔一律這樣匯入）。
const MODULE_IMPORT = /import\s+([A-Za-z_$][\w$]*)\s+from\s+(['"])(\.{1,2}\/[^'"]+\.module\.css)\2/g;
// 樣式檔裡出現過的 class 名稱（選取器的任何位置都算：.a .b、.a:hover、:global(.c)）。
const CSS_CLASS = /\.(-?[_a-zA-Z][\w-]*)/g;

/**
 * 元件用到、樣式檔裡卻沒有定義的 class：styles.x 拿到 undefined，className 少了一個，不會報錯也不會有警告，
 * 只有畫面默默少了那段樣式（2026-11-23 通知提示的樣式被誤刪，提示變成透明的就是這樣漏掉的）。
 * readCss 收相對於 web/ 的路徑、回傳內容，讀不到回 null（不檢查）。動態的 styles[x] 看不出名稱，不檢查。
 */
export function checkModuleClasses(file, source, readCss) {
  const imports = scriptCode(source, { keepStrings: true });
  const code = scriptCode(source, { keepStrings: false });
  return [...imports.matchAll(MODULE_IMPORT)].flatMap(([, name, , relative]) => {
    const cssPath = posix.join(posix.dirname(file), relative);
    const css = readCss(cssPath);
    if (css === null) return [];
    const defined = new Set([...blankComments(css).matchAll(CSS_CLASS)].map((match) => match[1]));
    const usage = new RegExp(`(?<![\\w$.])${name.replaceAll('$', '\\$')}\\.([A-Za-z_$][\\w$]*)`, 'g');
    return [...code.matchAll(usage)]
      .filter((match) => !defined.has(match[1]))
      .map((match) => violation(file, lineAt(code, match.index), `${name}.${match[1]} 在 ${relative} 裡沒有定義：畫面上不會有這段樣式`));
  });
}

/** HTML：不准有行內的 <script>、<style> 與 style 屬性。 */
export function checkHtml(file, source) {
  return [
    ...findAll(file, source, /<script(?![^>]*\bsrc=)[^>]*>/gi, '不能有行內的 <script>：內容安全政策不允許，改成獨立的檔案'),
    ...findAll(file, source, /<style\b/gi, '不能有行內的 <style>：內容安全政策不允許'),
    ...findAll(file, source, /\sstyle\s*=/gi, '不能有 style 屬性：內容安全政策不允許'),
  ].sort((a, b) => a.line - b.line);
}

function listFiles(root, directory) {
  let entries;
  try {
    entries = readdirSync(join(root, directory), { recursive: true });
  } catch {
    return [];
  }
  return entries
    .map((entry) => join(directory, String(entry)).split(sep).join('/'))
    .filter((path) => !path.split('/').includes('node_modules'));
}

function readOrNull(path) {
  try {
    return readFileSync(path, 'utf8');
  } catch {
    return null;
  }
}

function checkerFor(path) {
  if (path.endsWith('.css')) return checkCss;
  if (path.endsWith('.html')) return checkHtml;
  if (/\.(ts|tsx)$/.test(path) && !/\.test\.(ts|tsx)$/.test(path) && !path.startsWith('src/test/')) return checkScript;
  return null;
}

/** 掃過 root（web/）底下的 src、public 與 index.html。 */
export function checkProject(root) {
  const files = ['index.html', ...listFiles(root, 'src'), ...listFiles(root, 'public')];
  return files.flatMap((path) => {
    const check = checkerFor(path);
    if (check === null) return [];
    let source;
    try {
      source = readFileSync(join(root, path), 'utf8');
    } catch {
      return [];
    }
    const problems = check(path, source);
    if (check !== checkScript) return problems;
    return [...problems, ...checkModuleClasses(path, source, (cssPath) => readOrNull(join(root, cssPath)))];
  });
}

export function formatViolation({ file, line, message }) {
  return `${file}:${String(line)} ${message}`;
}

function main() {
  const root = join(dirname(fileURLToPath(import.meta.url)), '..');
  const violations = checkProject(root);
  if (violations.length === 0) {
    process.stdout.write('樣式檢查通過。\n');
    return;
  }
  process.stderr.write(`${violations.map(formatViolation).join('\n')}\n樣式檢查：${String(violations.length)} 個問題（規則見 docs/DESIGN.md 第 13 節）。\n`);
  process.exitCode = 1;
}

if (process.argv[1] !== undefined && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main();
}
