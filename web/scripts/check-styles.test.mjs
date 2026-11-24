import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
import { checkCss, checkHtml, checkModuleClasses, checkProject, checkScript, formatViolation } from './check-styles.mjs';

const COMPONENT = 'src/components/Example/Example.module.css';

function messagesOf(violations) {
  return violations.map((violation) => violation.message);
}

describe('顏色', () => {
  it('擋下變數檔以外的十六進位色與色彩函式', () => {
    // Arrange
    const css = '.a {\n  color: #fff;\n  background: rgb(0 0 0 / 10%);\n  border-color: light-dark(#000, #fff);\n}';

    // Act
    const violations = checkCss(COMPONENT, css);

    // Assert
    expect(violations.map((violation) => violation.line)).toEqual([2, 3, 4]);
    expect(messagesOf(violations)[0]).toContain('#fff');
  });

  it('擋下顏色屬性裡的顏色名稱，但不誤判屬性名稱與其他值', () => {
    // Arrange
    const css = '.a { color: white; white-space: nowrap; border: 1px solid red; outline: none; }';

    // Act
    const violations = checkCss(COMPONENT, css);

    // Assert
    expect(messagesOf(violations)).toEqual([expect.stringContaining('white'), expect.stringContaining('red')]);
  });

  it('變數、currentcolor、transparent 都可以', () => {
    // Arrange
    const css = '.a { color: var(--color-text); fill: currentcolor; background: transparent; }';

    // Act
    const violations = checkCss(COMPONENT, css);

    // Assert
    expect(violations).toEqual([]);
  });

  it('區域變數也不能藏顏色', () => {
    // Arrange
    const css = '.a { --local-tint: #123456; }';

    // Act
    const violations = checkCss(COMPONENT, css);

    // Assert
    expect(violations).toHaveLength(1);
  });

  it('變數檔本身可以寫顏色', () => {
    // Arrange
    const css = ':root { --color-text: light-dark(#1a1d23, #e7e9ee); }';

    // Act
    const violations = checkCss('src/styles/tokens.css', css);

    // Assert
    expect(violations).toEqual([]);
  });

  it('註解與字串裡的內容不算', () => {
    // Arrange
    const css = '/* color: #fff; */\n.a::before { content: "#1"; }';

    // Act
    const violations = checkCss(COMPONENT, css);

    // Assert
    expect(violations).toEqual([]);
  });
});

describe('字級、圓角、層級', () => {
  it('字級一定用變數；inherit 與百分比可以', () => {
    // Arrange
    const css = '.a { font-size: 14px; }\n.b { font-size: var(--text-sm); }\n.c { font-size: inherit; }\n.d { font: 600 12px/1 sans-serif; }\n.e { font: inherit; }';

    // Act
    const violations = checkCss(COMPONENT, css);

    // Assert
    expect(violations.map((violation) => violation.line)).toEqual([1, 4]);
  });

  it('圓角一定用變數；0、百分比與細線的寬度可以', () => {
    // Arrange
    const css = '.a { border-radius: 4px; }\n.b { border-radius: 0; }\n.c { border-top-left-radius: 50%; }\n.d { border-radius: calc(var(--radius-md) - 1px); }';

    // Act
    const violations = checkCss(COMPONENT, css);

    // Assert
    expect(violations.map((violation) => violation.line)).toEqual([1]);
  });

  it('層級用變數；元件內部的 1、2 可以', () => {
    // Arrange
    const css = '.a { z-index: 100; }\n.b { z-index: 1; }\n.c { z-index: calc(var(--z-drawer) - 1); }\n.d { z-index: auto; }';

    // Act
    const violations = checkCss(COMPONENT, css);

    // Assert
    expect(violations.map((violation) => violation.line)).toEqual([1]);
  });
});

describe('動畫時間', () => {
  it('擋下寫死的時間，0 秒可以', () => {
    // Arrange
    const css =
      '.a { animation: spin 0.8s linear infinite; }\n.b { transition: opacity var(--duration-fast) var(--ease-out); }\n.c { transition: visibility 0s linear var(--duration-normal); }\n.d { transition-duration: 150ms; }';

    // Act
    const violations = checkCss(COMPONENT, css);

    // Assert
    expect(violations.map((violation) => violation.line)).toEqual([1, 4]);
  });

  it('曲線的數字不是時間', () => {
    // Arrange
    const css = '.a { transition: transform var(--duration-fast) cubic-bezier(0.2, 0.8, 0.2, 1); }';

    // Act
    const violations = checkCss(COMPONENT, css);

    // Assert
    expect(violations).toEqual([]);
  });

  it('global.css 的減少動態可以寫死時間與 !important', () => {
    // Arrange
    const css = '@media (prefers-reduced-motion: reduce) { * { animation-duration: 0.01ms !important; } }';

    // Act
    const violations = checkCss('src/styles/global.css', css);

    // Assert
    expect(violations).toEqual([]);
  });
});

describe('!important', () => {
  it('只准出現在 global.css', () => {
    // Arrange
    const css = '.a { display: none !important; }';

    // Act
    const violations = checkCss(COMPONENT, css);

    // Assert
    expect(messagesOf(violations)).toEqual([expect.stringContaining('!important')]);
  });
});

describe('斷點', () => {
  it('只允許 max-width: 48rem 與 min-width: 64rem／80rem', () => {
    // Arrange
    const css = [
      '@media (max-width: 48rem) { .a { display: none; } }',
      '@media (min-width: 64rem) { .a { display: none; } }',
      '@media (min-width: 80rem) { .a { display: none; } }',
      '@media (max-width: 40rem) { .a { display: none; } }',
      '@media (min-width: 48rem) { .a { display: none; } }',
      '@media (width <= 48rem) { .a { display: none; } }',
    ].join('\n');

    // Act
    const violations = checkCss(COMPONENT, css);

    // Assert
    expect(violations.map((violation) => violation.line)).toEqual([4, 5, 6]);
  });

  it('寬度以外的媒體查詢與容器查詢不限', () => {
    // Arrange
    const css =
      '@media (pointer: coarse) { .a { display: none; } }\n@media (prefers-color-scheme: dark) { .a { display: none; } }\n@container (min-width: 52rem) { .a { display: none; } }';

    // Act
    const violations = checkCss(COMPONENT, css);

    // Assert
    expect(violations).toEqual([]);
  });
});

describe('style 屬性', () => {
  it('允許清單以外的元件不能用 style', () => {
    // Arrange
    const source = 'export function A() {\n  return <div style={{ color: "red" }} />;\n}';

    // Act
    const violations = checkScript('src/features/example/A.tsx', source);

    // Assert
    expect(violations.map((violation) => violation.line)).toEqual([2]);
  });

  it('浮層的座標可以用 style', () => {
    // Arrange
    const source = 'export function Floating() {\n  return <div style={position} />;\n}';

    // Act
    const violations = checkScript('src/components/Floating/Floating.tsx', source);

    // Assert
    expect(violations).toEqual([]);
  });

  it('也擋下用 setAttribute 寫 style', () => {
    // Arrange
    const source = "element.setAttribute('style', 'top: 0');";

    // Act
    const violations = checkScript('src/lib/example.ts', source);

    // Assert
    expect(violations).toHaveLength(1);
  });

  it('className 之類的名稱不算', () => {
    // Arrange
    const source = 'const styles = {}; const x = <div className={styles.a} data-style="x" />;';

    // Act
    const violations = checkScript('src/features/example/A.tsx', source);

    // Assert
    expect(violations).toEqual([]);
  });
});

describe('HTML', () => {
  it('擋下行內的 script、style 與 style 屬性；外部的 script 可以', () => {
    // Arrange
    const html = [
      '<html>',
      '<head><script src="/theme-init.js"></script></head>',
      '<script>alert(1)</script>',
      '<style>p {}</style>',
      '<div style="color: red"></div>',
      '</html>',
    ].join('\n');

    // Act
    const violations = checkHtml('index.html', html);

    // Assert
    expect(violations.map((violation) => violation.line)).toEqual([3, 4, 5]);
  });
});

describe('陰影', () => {
  it('有模糊半徑的陰影一定用 --shadow-* 變數', () => {
    // Arrange
    const css = [
      '.a { box-shadow: 0 2px 8px var(--color-text); }',
      '.b { box-shadow: var(--shadow-popover); }',
      '.c { text-shadow: 0 1px 2px var(--color-text); }',
      '.d { box-shadow: 0 0 0 1px var(--color-danger), var(--shadow-raised); }',
      '.e { box-shadow: none; }',
    ].join('\n');

    // Act
    const violations = checkCss(COMPONENT, css);

    // Assert
    expect(violations.map((violation) => violation.line)).toEqual([1, 3]);
    expect(messagesOf(violations)[0]).toContain('--shadow-');
  });

  it('沒有模糊的環線可以直接寫，但顏色要用變數', () => {
    // Arrange
    const css = [
      '.a { box-shadow: inset 0 0 0 2px var(--color-accent); }',
      '.b { box-shadow: inset 3px 0 0 var(--color-accent), var(--shadow-raised); }',
      '.c { box-shadow: 0 0 0 1px currentcolor; }',
      '.d { box-shadow: 0 0 0 1px; }',
    ].join('\n');

    // Act
    const violations = checkCss(COMPONENT, css);

    // Assert
    expect(violations.map((violation) => violation.line)).toEqual([3, 4]);
  });
});

describe('補上的漏洞', () => {
  it('區域變數的值照樣檢查：字級、圓角、層級透過區域變數也擋得到', () => {
    // Arrange
    const css = [
      '.a { --label-size: 13px; font-size: var(--label-size); }',
      '.b { --corner: 4px; border-radius: calc(var(--corner) - 1px); }',
      '.c { --layer: 100; z-index: var(--layer); }',
      '.d { --title-size: var(--text-sm); font-size: var(--title-size); }',
      '.e { --wait: 300ms; }',
    ].join('\n');

    // Act
    const violations = checkCss(COMPONENT, css);

    // Assert
    expect(violations.map((violation) => violation.line)).toEqual([1, 2, 3, 5]);
  });

  it('字級只接受單一的 var(--text-*)：不接受 calc、備用值與百分比', () => {
    // Arrange
    const css = [
      '.a { font-size: calc(var(--text-sm) + 1px); }',
      '.b { font-size: var(--text-sm, 13px); }',
      '.c { font-size: 120%; }',
      '.d { font-size: var(--text-sm); }',
    ].join('\n');

    // Act
    const violations = checkCss(COMPONENT, css);

    // Assert
    expect(violations.map((violation) => violation.line)).toEqual([1, 2, 3]);
  });

  it('var() 的備用值裡的顏色也擋', () => {
    // Arrange
    const css = '.a { color: var(--color-text, #f00); }';

    // Act
    const violations = checkCss(COMPONENT, css);

    // Assert
    expect(messagesOf(violations)).toEqual([expect.stringContaining('#f00')]);
  });

  it('「! important」中間有空白也擋', () => {
    // Arrange
    const css = '.a { display: none ! important; }';

    // Act
    const violations = checkCss(COMPONENT, css);

    // Assert
    expect(messagesOf(violations)).toEqual([expect.stringContaining('!important')]);
  });

  it('擋下 device-width 的媒體查詢', () => {
    // Arrange
    const css = '@media (max-device-width: 48rem) { .a { display: none; } }';

    // Act
    const violations = checkCss(COMPONENT, css);

    // Assert
    expect(violations).toHaveLength(1);
  });

  it('擋下負的延遲（寫死的或用 calc 算出來的）', () => {
    // Arrange
    const css = [
      '.a { transition-delay: -0.2s; }',
      '.b { animation: pulse var(--duration-fast) calc(-1 * var(--duration-fast)); }',
      '.c { animation: pulse-2s var(--duration-fast); }',
    ].join('\n');

    // Act
    const violations = checkCss(COMPONENT, css);

    // Assert
    expect(violations.map((violation) => violation.line)).toEqual([1, 2]);
  });

  it('calc 裡的間距變數不是負的延遲：var(--space-1) 的「-1」、乘上 -1 的長度', () => {
    // Arrange
    const css = [
      '.a { --header-height: calc(var(--control-height-sm) + var(--space-1)); }',
      '.b { margin-inline: calc(-1 * var(--space-2)); }',
      '.c { --bleed: calc(-1 * var(--space-2)); }',
    ].join('\n');

    // Act
    const violations = checkCss(COMPONENT, css);

    // Assert
    expect(violations).toEqual([]);
  });

  it('區域變數裡算出來的負延遲照樣擋下', () => {
    // Arrange
    const css = [
      '.a { --delay: calc(-1 * var(--duration-fast)); }',
      '.b { --delay: -0.2s; }',
    ].join('\n');

    // Act
    const violations = checkCss(COMPONENT, css);

    // Assert
    expect(violations.map((violation) => violation.line)).toEqual([1, 2]);
  });

  it('url(#abc) 不是顏色', () => {
    // Arrange
    const css = '.a { mask: url(#abc); fill: url(#gradient-1); }';

    // Act
    const violations = checkCss(COMPONENT, css);

    // Assert
    expect(violations).toEqual([]);
  });

  it('程式裡的字串與註解不算 style 屬性；const style = … 也不算', () => {
    // Arrange
    const source = [
      "const html = 'style=\"x\"';",
      '// <div style={{}} /> 只是註解',
      'const style = { top: 0 };',
    ].join('\n');

    // Act
    const violations = checkScript('src/features/example/A.tsx', source);

    // Assert
    expect(violations).toEqual([]);
  });

  it('擋下用 .style.x =、.style.setProperty、cssText 設樣式；允許清單裡的檔案照舊可以', () => {
    // Arrange
    const source = ["element.style.top = '0';", "element.style.setProperty('top', '0');", "element.style.cssText = 'top: 0';"].join('\n');

    // Act
    const violations = checkScript('src/lib/example.ts', source);
    const allowed = checkScript('src/components/Floating/Floating.tsx', source);

    // Assert
    expect(violations.map((violation) => violation.line)).toEqual([1, 2, 3]);
    expect(allowed).toEqual([]);
  });
});

describe('原生的下拉', () => {
  it('元件裡的 <select>、<option> 一律擋下：下拉用 components/Select', () => {
    // Arrange
    const source = [
      'export function A() {',
      '  return (',
      '    <select aria-label="類型">',
      '      <option value="task">任務</option>',
      '    </select>',
      '  );',
      '}',
    ].join('\n');

    // Act
    const violations = checkScript('src/features/example/A.tsx', source);

    // Assert
    expect(violations.map((violation) => violation.line)).toEqual([3, 4]);
    expect(messagesOf(violations)[0]).toContain('components/Select');
  });

  it('允許清單是空的：style 的允許清單裡的檔案也一樣擋', () => {
    // Arrange
    const source = 'export const a = <select />;';

    // Act
    const violations = checkScript('src/components/Floating/Floating.tsx', source);

    // Assert
    expect(violations).toHaveLength(1);
  });

  it('字串與註解裡的 <select 不算；role="option" 的 li、<optgroup> 以外的名稱、泛型也不算', () => {
    // Arrange
    const source = [
      "const hint = '不要寫 <select>';",
      'const html = `<option value="a">`;',
      '// <select> 只是註解',
      '/* <option> 也是 */',
      'const item = <li role="option" aria-selected="true">任務</li>;',
      'const label = <selectLabel />;',
      'const options: Array<optionLike> = [];',
    ].join('\n');

    // Act
    const violations = checkScript('src/components/Select/Select.tsx', source);

    // Assert
    expect(violations).toEqual([]);
  });

  it('只看元件（.tsx）：.ts 的程式不會有 JSX 標籤', () => {
    // Arrange
    const source = 'const isLess = count <select;';

    // Act
    const violations = checkScript('src/lib/example.ts', source);

    // Assert
    expect(violations).toEqual([]);
  });
});

describe('樣式檔裡的 class', () => {
  const FILE = 'src/features/a/A.tsx';

  it('擋下元件用到、樣式檔裡卻沒有定義的 class，標出用到的那一行', () => {
    // Arrange
    const css = '.toast { display: flex; }\n.region:hover .link { color: inherit; }';
    const source = [
      "import styles from './A.module.css';",
      '',
      'const a = <div className={styles.toast} />;',
      'const b = <a className={styles.link} />;',
      'const c = <span className={styles.excerpt} />;',
    ].join('\n');

    // Act
    const violations = checkModuleClasses(FILE, source, () => css);

    // Assert
    expect(violations.map((violation) => violation.line)).toEqual([5]);
    expect(messagesOf(violations)[0]).toContain('styles.excerpt');
  });

  it('照匯入的相對路徑讀那一份樣式檔，匯入的名稱不一定叫 styles', () => {
    // Arrange
    const read = [];
    const source = "import css from '../shared/Shared.module.css';\nconst a = <div className={css.missing} />;";

    // Act
    const violations = checkModuleClasses(FILE, source, (path) => {
      read.push(path);
      return '.present { display: flex; }';
    });

    // Assert
    expect(read).toEqual(['src/features/shared/Shared.module.css']);
    expect(messagesOf(violations)).toEqual([expect.stringContaining('css.missing')]);
  });

  it('註解與字串裡的不算；讀不到樣式檔、動態的 styles[x] 不檢查', () => {
    // Arrange
    const source = [
      "import styles from './A.module.css';",
      "import other from './Missing.module.css';",
      '// 規則見 check-styles.mjs',
      "const label = 'styles.nothing';",
      'const a = <div className={cx(styles.a, styles[tone], other.b)} />;',
    ].join('\n');

    // Act
    const violations = checkModuleClasses(FILE, source, (path) => (path.endsWith('A.module.css') ? '.a { }' : null));

    // Assert
    expect(violations).toEqual([]);
  });
});

describe('整個專案', () => {
  let root;

  afterEach(() => {
    rmSync(root, { recursive: true, force: true });
  });

  it('掃過 src 的 CSS 與元件、index.html，略過 node_modules 與測試', () => {
    // Arrange
    root = mkdtempSync(join(tmpdir(), 'check-styles-'));
    mkdirSync(join(root, 'src/features/a'), { recursive: true });
    mkdirSync(join(root, 'src/node_modules/pkg'), { recursive: true });
    writeFileSync(join(root, 'index.html'), '<div id="root"></div>');
    writeFileSync(join(root, 'src/features/a/A.module.css'), '.a { color: #fff; }');
    writeFileSync(join(root, 'src/features/a/A.tsx'), 'const a = <p style={{}} />;');
    writeFileSync(join(root, 'src/features/a/A.test.tsx'), 'const a = <p style={{}} />;');
    writeFileSync(join(root, 'src/node_modules/pkg/x.css'), '.a { color: #fff; }');

    // Act
    const violations = checkProject(root);

    // Assert
    expect(violations.map((violation) => violation.file).sort()).toEqual([
      'src/features/a/A.module.css',
      'src/features/a/A.tsx',
    ]);
    expect(formatViolation(violations[0])).toMatch(/^src\/features\/a\/A\.(module\.css|tsx):1 /);
  });

  it('原生的下拉：元件裡的擋下，測試與 src/test/ 裡的不算（測試可以拿原生的下拉當對照）', () => {
    // Arrange
    root = mkdtempSync(join(tmpdir(), 'check-styles-'));
    mkdirSync(join(root, 'src/features/a'), { recursive: true });
    mkdirSync(join(root, 'src/test'), { recursive: true });
    writeFileSync(join(root, 'index.html'), '<div id="root"></div>');
    writeFileSync(join(root, 'src/features/a/A.tsx'), 'const a = <select />;');
    writeFileSync(join(root, 'src/features/a/A.test.tsx'), 'const a = <select />;');
    writeFileSync(join(root, 'src/test/helper.tsx'), 'const a = <option />;');

    // Act
    const violations = checkProject(root);

    // Assert
    expect(violations.map((violation) => violation.file)).toEqual(['src/features/a/A.tsx']);
  });
  it('元件用到樣式檔裡沒有的 class 時擋下', () => {
    // Arrange
    root = mkdtempSync(join(tmpdir(), 'check-styles-'));
    mkdirSync(join(root, 'src/features/a'), { recursive: true });
    writeFileSync(join(root, 'index.html'), '<div id="root"></div>');
    writeFileSync(join(root, 'src/features/a/A.module.css'), '.a { display: flex; }');
    writeFileSync(join(root, 'src/features/a/A.tsx'), "import styles from './A.module.css';\nconst x = <div className={cx(styles.a, styles.b)} />;");

    // Act
    const violations = checkProject(root);

    // Assert
    expect(violations).toEqual([expect.objectContaining({ file: 'src/features/a/A.tsx', line: 2 })]);
  });
});
