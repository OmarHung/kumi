import { describe, expect, it } from 'vitest';
import { docToMarkdown, markdownToDoc, contentToMarkdown } from './markdownDoc';
import { GUID, PERSON, doc, expectStable, firstBlock, node, p, roundTrip, t } from './markdownDocTestKit';

describe('正規化的例子（合約 4.5）', () => {
  it.each([
    ['web_01 的 C:\\path\\x', 'web_01 的 C:\\path\\x'],
    ['網址 https://www.example.com/a_b?x=1 結束。', '網址 https://www.example.com/a_b?x=1 結束。'],
    ['第一行\n第二行', '第一行\n第二行'],
    [`請 @[王小明](mention:${PERSON}) 看`, `請 @[王小明](mention:${PERSON}) 看`],
    [`![截圖.png](attachment:${GUID})`, `![截圖.png](attachment:${GUID})`],
    ['* 一\n* 二', '- 一\n- 二'],
    ['說明：\n- 一', '說明：\n\n- 一'],
    ['<div>x</div>', '<div>x</div>'],
  ])('%j 存回去是 %j', (original, expected) => {
    expect(roundTrip(original)).toBe(expected);
  });
});

describe('空內容', () => {
  it.each(['', '   ', '\n\n  \n'])('%j 讀成一個空的段落', (input) => {
    expect(markdownToDoc(input)).toEqual(doc(p()));
  });

  it('只有一個空段落寫成空字串', () => {
    expect(docToMarkdown(doc(p()))).toBe('');
  });

  it('沒有 content 的文件寫成空字串', () => {
    expect(docToMarkdown({ type: 'doc' })).toBe('');
  });

  it('段落之間的空段落不會多出空行', () => {
    expect(docToMarkdown(doc(p(t('a')), p(), p(t('b'))))).toBe('a\n\nb');
  });

  it('Windows 的換行當一般換行', () => {
    expect(markdownToDoc('a\r\nb\r\n\r\nc')).toEqual(markdownToDoc('a\nb\n\nc'));
  });
});

describe('標題與段落', () => {
  it.each([1, 2, 3, 4, 5, 6])('第 %i 級標題讀與寫', (level) => {
    const markdown = `${'#'.repeat(level)} 標題`;
    expect(firstBlock(markdown)).toEqual(node('heading', { level }, t('標題')));
    expect(docToMarkdown(doc(node('heading', { level }, t('標題'))))).toBe(markdown);
  });

  it('底線式標題讀成 ATX 標題，多行的接成一行', () => {
    expect(firstBlock('標題\n===')).toEqual(node('heading', { level: 1 }, t('標題')));
    expect(roundTrip('甲\n乙\n---')).toBe('## 甲 乙');
  });

  it('標題裡的換行寫成空白', () => {
    expect(docToMarkdown(doc(node('heading', { level: 2 }, t('甲'), { type: 'hardBreak' }, t('乙'))))).toBe('## 甲 乙');
  });

  it('標題的 level 不合法時當 1', () => {
    expect(docToMarkdown(doc(node('heading', { level: 9 }, t('x'))))).toBe('# x');
  });

  it('兩個段落', () => {
    expect(markdownToDoc('甲\n\n乙')).toEqual(doc(p(t('甲')), p(t('乙'))));
  });
});

describe('引用', () => {
  it('讀與寫，含巢狀', () => {
    const markdown = '> 外層\n>\n> > 內層';
    expect(firstBlock(markdown)).toEqual(node('blockquote', undefined, p(t('外層')), node('blockquote', undefined, p(t('內層')))));
    expect(roundTrip(markdown)).toBe(markdown);
  });

  it('空的引用補一個空段落', () => {
    expect(firstBlock('>')).toEqual(node('blockquote', undefined, p()));
    expect(docToMarkdown(doc(node('blockquote', undefined, p())))).toBe('>');
  });
});

describe('清單', () => {
  it('項目清單，tight', () => {
    expect(firstBlock('- 一\n- 二')).toEqual(
      node('bulletList', { spread: false }, node('listItem', undefined, p(t('一'))), node('listItem', undefined, p(t('二')))),
    );
  });

  it('項目之間有空行的清單 spread 是 true，寫回去保留空行', () => {
    const markdown = '- 一\n\n- 二';
    expect(firstBlock(markdown)).toMatchObject({ type: 'bulletList', attrs: { spread: true } });
    expect(roundTrip(markdown)).toBe(markdown);
  });

  it('編號清單帶起始編號，寫回去之後的編號遞增', () => {
    expect(firstBlock('3. 甲\n4. 乙')).toMatchObject({ type: 'orderedList', attrs: { start: 3, spread: false } });
    expect(roundTrip('3. 甲\n1. 乙')).toBe('3. 甲\n4. 乙');
    expect(firstBlock('1. 甲')).toMatchObject({ attrs: { start: 1 } });
  });

  it('編號清單沒有 start 屬性時當 1', () => {
    expect(docToMarkdown(doc(node('orderedList', undefined, node('listItem', undefined, p(t('甲'))))))).toBe('1. 甲');
  });

  it('巢狀清單', () => {
    const markdown = '- 一\n  - 一之一\n- 二';
    const written = roundTrip(markdown);
    expect(written).toBe(markdown);
    expect(firstBlock(markdown)).toMatchObject({
      content: [{ type: 'listItem', content: [{ type: 'paragraph' }, { type: 'bulletList' }] }, { type: 'listItem' }],
    });
  });

  it('項目裡有多個段落', () => {
    const markdown = '- 甲\n\n  乙';
    expect(firstBlock(markdown)).toMatchObject({ content: [{ content: [{ type: 'paragraph' }, { type: 'paragraph' }] }] });
    expect(roundTrip(markdown)).toBe('- 甲\n\n  乙');
  });

  it('空的項目補一個空段落', () => {
    expect(firstBlock('-')).toEqual(node('bulletList', { spread: false }, node('listItem', undefined, p())));
    expect(docToMarkdown(doc(node('bulletList', { spread: false }, node('listItem', undefined, p()))))).toBe('-');
  });

  it('項目以程式碼區塊開頭時前面補一個空段落，寫回去之後還是同一份文件', () => {
    const { first, written } = expectStable('- ```js\n  x\n  ```');
    expect(first.content?.[0]?.content?.[0]?.content?.[0]).toEqual(p());
    expect(written).toBe('- ```js\n  x\n  ```');
  });

  it('沒有項目的清單寫成空', () => {
    expect(docToMarkdown(doc(node('bulletList', { spread: false })))).toBe('');
  });
});

describe('待辦清單', () => {
  it('每一項都有勾選框才是 taskList', () => {
    expect(firstBlock('- [ ] 甲\n- [x] 乙')).toEqual(
      node('taskList', { spread: false }, node('taskItem', { checked: false }, p(t('甲'))), node('taskItem', { checked: true }, p(t('乙')))),
    );
  });

  it('寫回去保留勾選的狀態', () => {
    expect(roundTrip('- [ ] 甲\n- [X] 乙')).toBe('- [ ] 甲\n- [x] 乙');
  });

  it('混著的清單照一般清單，勾選框在段落前面變成文字，而且不會又被讀成待辦', () => {
    const first = firstBlock('- [x] 甲\n- 乙');
    expect(first.type).toBe('bulletList');
    expect(first.content?.[0]).toEqual(node('listItem', undefined, p(t('[x] 甲'))));
    expectStable('- [x] 甲\n- 乙');
  });

  it('待辦項目的 checked 沒給時當未勾', () => {
    expect(docToMarkdown(doc(node('taskList', { spread: false }, node('taskItem', undefined, p(t('甲'))))))).toBe('- [ ] 甲');
  });

  it('空的待辦項目不寫（寫出來會被讀成一般文字），整份清單都是空的就整個不寫', () => {
    const empty = node('taskItem', { checked: true });
    expect(docToMarkdown(doc(node('taskList', { spread: false }, empty)))).toBe('');
    expect(docToMarkdown(doc(node('taskList', { spread: false }, empty, node('taskItem', { checked: false }, p(t('甲'))))))).toBe('- [ ] 甲');
  });

  it('待辦項目裡的空段落後面接著別的區塊時，空段落留著（勾選框寫在第一個段落裡）', () => {
    const written = docToMarkdown(
      doc(node('taskList', { spread: false }, node('taskItem', { checked: true }, p(), node('codeBlock', { language: null, meta: null }, t('x'))))),
    );
    expect(written).toContain('[x]');
    expect(written).toContain('```');
  });
});

describe('程式碼區塊', () => {
  it('帶語言與 meta', () => {
    const markdown = '```ts title="a.ts"\nconst a = 1;\n```';
    expect(firstBlock(markdown)).toEqual(node('codeBlock', { language: 'ts', meta: 'title="a.ts"' }, t('const a = 1;')));
    expect(roundTrip(markdown)).toBe(markdown);
  });

  it('沒有語言就是 null', () => {
    expect(firstBlock('```\nx\n```')).toEqual(node('codeBlock', { language: null, meta: null }, t('x')));
  });

  it('空的程式碼區塊', () => {
    expect(firstBlock('```\n```')).toEqual(node('codeBlock', { language: null, meta: null }));
    expect(docToMarkdown(doc(node('codeBlock', { language: null, meta: null })))).toBe('```\n```');
  });

  it('縮排式的程式碼區塊寫成圍欄', () => {
    expect(roundTrip('    a = 1')).toBe('```\na = 1\n```');
  });

  it('裡面有三個反引號時圍欄加長，內容不被截斷', () => {
    const body = '```\ninner\n```';
    const written = docToMarkdown(doc(node('codeBlock', { language: null, meta: null }, t(body))));
    expect(markdownToDoc(written)).toEqual(doc(node('codeBlock', { language: null, meta: null }, t(body))));
  });

  it('語言是空字串時當沒有語言', () => {
    expect(docToMarkdown(doc(node('codeBlock', { language: '', meta: '' }, t('x'))))).toBe('```\nx\n```');
  });

  it('Windows 路徑與 Markdown 符號不跳脫', () => {
    expect(roundTrip('```\nC:\\path *x* _y_\n```')).toBe('```\nC:\\path *x* _y_\n```');
  });
});

describe('分隔線', () => {
  it('讀與寫', () => {
    expect(markdownToDoc('甲\n\n***\n\n乙')).toEqual(doc(p(t('甲')), node('horizontalRule', undefined), p(t('乙'))));
    expect(roundTrip('甲\n\n***\n\n乙')).toBe('甲\n\n---\n\n乙');
  });
});

describe('表格', () => {
  const markdown = '| 名稱 | 數量 | 備註 |\n| :- | :-: | -: |\n| 甲 | 1 | 一 |';

  it('第一列是表頭，欄位的對齊放在每個儲存格上', () => {
    const table = firstBlock(markdown);
    expect(table).toEqual(
      node(
        'table',
        undefined,
        node(
          'tableRow',
          undefined,
          node('tableHeader', { align: 'left' }, p(t('名稱'))),
          node('tableHeader', { align: 'center' }, p(t('數量'))),
          node('tableHeader', { align: 'right' }, p(t('備註'))),
        ),
        node(
          'tableRow',
          undefined,
          node('tableCell', { align: 'left' }, p(t('甲'))),
          node('tableCell', { align: 'center' }, p(t('1'))),
          node('tableCell', { align: 'right' }, p(t('一'))),
        ),
      ),
    );
  });

  it('寫回去不補空白對齊直線', () => {
    expect(roundTrip(markdown)).toBe(markdown);
  });

  it('沒有指定對齊就是 null，空的儲存格補空段落', () => {
    const table = firstBlock('| a | b |\n|---|---|\n| | x |');
    expect(table.content?.[1]?.content?.[0]).toEqual(node('tableCell', { align: null }, p()));
    expect(roundTrip('| a | b |\n|---|---|\n| | x |')).toBe('| a | b |\n| - | - |\n| | x |');
  });

  it('儲存格裡的直線跳脫，行內程式碼裡的也是', () => {
    const written = roundTrip('| a |\n|---|\n| 1 \\| 2 `x\\|y` |');
    expect(written).toContain('1 \\| 2');
    expectStable('| a |\n|---|\n| 1 \\| 2 `x\\|y` |');
  });

  it('儲存格裡的粗體與連結', () => {
    expectStable('| a |\n|---|\n| **粗** [連結](https://example.com) |');
  });

  it('儲存格裡的換行寫成空白，多個段落用空白接起來', () => {
    const written = docToMarkdown(
      doc(
        node(
          'table',
          undefined,
          node('tableRow', undefined, node('tableHeader', { align: null }, p(t('甲'), { type: 'hardBreak' }, t('乙')))),
          node('tableRow', undefined, node('tableCell', { align: null }, p(t('一')), p(t('二')))),
        ),
      ),
    );
    expect(written).toBe('| 甲 乙 |\n| - |\n| 一 二 |');
  });

  it('儲存格的內容直接是行內節點時也寫得出來', () => {
    const written = docToMarkdown(
      doc(node('table', undefined, node('tableRow', undefined, node('tableHeader', { align: 'bad' }, t('甲'))))),
    );
    expect(written).toBe('| 甲 |\n| - |');
  });

  it('沒有任何列的表格不寫', () => {
    expect(docToMarkdown(doc(node('table', undefined)))).toBe('');
  });
});

describe('不認得的區塊原樣保留', () => {
  it('HTML 區塊，含多行', () => {
    const markdown = '<details>\n<summary>詳細</summary>\n\n內容\n\n</details>';
    const doc1 = markdownToDoc(markdown);
    expect(doc1.content?.[0]).toEqual(node('rawBlock', { source: '<details>\n<summary>詳細</summary>' }));
    expect(docToMarkdown(doc1)).toBe(markdown);
  });

  it('註腳：參照與定義都原文照存', () => {
    const markdown = '內文[^1]\n\n[^1]: 註腳內容';
    const result = markdownToDoc(markdown);
    expect(result).toEqual(doc(p(t('內文'), node('rawInline', { source: '[^1]' })), node('rawBlock', { source: '[^1]: 註腳內容' })));
    expect(docToMarkdown(result)).toBe(markdown);
  });

  it('沒用到的連結定義變 rawBlock', () => {
    expect(markdownToDoc('[x]: https://example.com "標題"')).toEqual(
      doc(node('rawBlock', { source: '[x]: https://example.com "標題"' })),
    );
    expect(roundTrip('甲\n\n[x]: https://example.com')).toBe('甲\n\n[x]: https://example.com');
  });

  it('同一個編號的第二個定義沒人用，原文留著', () => {
    const result = markdownToDoc('[甲][a]\n\n[a]: https://example.com/1\n[a]: https://example.com/2');
    expect(result.content?.[1]).toEqual(node('rawBlock', { source: '[a]: https://example.com/2' }));
  });

  it('清單裡的 HTML 區塊也保留', () => {
    expectStable('- <div>x</div>');
  });

  it('不認得的節點類型：有行內內容當段落，不然攤開裡面的區塊', () => {
    const written = docToMarkdown(
      doc(
        node('mystery', undefined, t('行內')),
        node('mystery', undefined, p(t('區塊'))),
        t('孤兒文字'),
        { type: 'custom' },
      ),
    );
    expect(written).toBe('行內\n\n區塊\n\n孤兒文字');
  });
});

describe('contentToMarkdown', () => {
  it('一段內容包成文件再寫', () => {
    expect(contentToMarkdown([p(t('甲')), p(t('乙'))])).toBe('甲\n\n乙');
    expect(contentToMarkdown([])).toBe('');
  });
});

describe('穩定性：同一份文件', () => {
  it.each([
    '甲\n\n> 引用\n\n- 一\n- 二\n\n1. 甲\n2. 乙\n\n---\n\n```\ncode\n```',
    '- [ ] 一\n- [x] 二\n  - [ ] 二之一',
    '[x]: https://example.com\n\n[甲][x] 與 ![圖][x]',
  ])('%j', (markdown) => {
    expectStable(markdown);
  });
});

describe('註腳的定義裡的參照式連結', () => {
  it('連結的定義留著（註腳是原文照存，裡面的參照不會被換成連結）', () => {
    const markdown = 'Text[^1]\n\n[^1]: see [a][x]\n\n[x]: https://e.com';

    const written = roundTrip(markdown);

    expect(written).toContain('[x]: https://e.com');
    expect(written).toContain('[^1]: see [a][x]');
    expectStable(markdown);
  });
});
