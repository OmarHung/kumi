import { describe, expect, it } from 'vitest';
import { stripPlaceholders } from '../../attachments/editorUploadText';
import { docToMarkdown, markdownToDoc } from './markdownDoc';
import { GUID, PERSON, bold, br, code, doc, expectStable, firstBlock, italic, link, node, p, roundTrip, strike, t } from './markdownDocTestKit';

describe('標記', () => {
  it.each([
    ['**粗**', t('粗', bold)],
    ['*斜*', t('斜', italic)],
    ['~~刪~~', t('刪', strike)],
    ['`碼`', t('碼', code)],
  ])('%s 讀成標記、寫回去不變', (markdown, expected) => {
    expect(firstBlock(markdown)).toEqual(p(expected));
    expect(roundTrip(markdown)).toBe(markdown);
  });

  it('單一的 ~ 也是刪除線，寫回去是兩個', () => {
    expect(firstBlock('~刪~')).toEqual(p(t('刪', strike)));
    expect(roundTrip('~刪~')).toBe('~~刪~~');
  });

  it('斜體裡有粗體：標記照固定的順序排，寫回去還是同一份文件', () => {
    expect(firstBlock('*甲 **乙***')).toEqual(p(t('甲 ', italic), t('乙', bold, italic)));
    expectStable('*甲 **乙***');
  });

  it('連結：href 與 title 照原文', () => {
    expect(firstBlock('[文字](https://example.com/a?b=1 "標題")')).toEqual(p(t('文字', link('https://example.com/a?b=1', '標題'))));
    expect(roundTrip('[文字](https://example.com/a?b=1 "標題")')).toBe('[文字](https://example.com/a?b=1 "標題")');
  });

  it('連結的網址不做白名單，顯示時才擋', () => {
    expect(firstBlock('[x](javascript:alert(1))')).toEqual(p(t('x', link('javascript:alert(1)'))));
  });

  it('連結裡有程式碼與粗體', () => {
    const markdown = '[**粗** 與 `碼`](https://example.com)';
    const l = link('https://example.com');
    expect(firstBlock(markdown)).toEqual(p(t('粗', l, bold), t(' 與 ', l), t('碼', l, code)));
    expect(roundTrip(markdown)).toBe(markdown);
  });

  it('文字等於網址的連結寫成 <網址>，讀回來一樣', () => {
    const { written } = expectStable('<https://www.example.com/a_b>');
    expect(written).toBe('<https://www.example.com/a_b>');
  });

  it('單獨的網址留在文字裡，不變成連結，也不加跳脫', () => {
    expect(firstBlock('見 https://www.example.com/a_b。')).toEqual(p(t('見 https://www.example.com/a_b。')));
    expect(roundTrip('見 https://www.example.com/a_b。')).toBe('見 https://www.example.com/a_b。');
  });

  it('每一行頭尾的半形空白拿掉，不寫成 &#x20;（提及後面補的空白、打到一半的空白）', () => {
    const mention = node('mention', { id: PERSON, label: '王小明' });
    expect(docToMarkdown(doc(p(t('請 '), mention, t(' ')), p(t('下一段'))))).toBe(`請 @[王小明](mention:${PERSON})\n\n下一段`);
    expect(docToMarkdown(doc(p(t('  前面 '), br, t(' 後面 '))))).toBe('前面\n後面');
    expect(docToMarkdown(doc(p(t('粗 ', bold), br, t('x'))))).toBe('**粗**\nx');
  });

  it('全形空白與行中間的空白不動', () => {
    expect(roundTrip('網域註冊在：　到期：')).toBe('網域註冊在：　到期：');
    expect(docToMarkdown(doc(p(t('甲　'))))).toBe('甲　');
  });

  it('標記的開頭與結尾是空白時，空白移到標記外面', () => {
    expect(docToMarkdown(doc(p(t('前'), t(' 粗 ', bold), t('後'))))).toBe('前 **粗** 後');
    expect(docToMarkdown(doc(p(t('前'), t('  ', bold), t('後'))))).toBe('前  後');
  });

  it('連結裡的標記：空白只移出粗體，留在連結文字裡', () => {
    expect(docToMarkdown(doc(p(t('甲 '), t('乙 ', link('https://example.com'), bold))))).toBe('甲 [**乙** ](https://example.com)');
  });

  it('連結跨過不同標記的文字', () => {
    const l = link('https://example.com');
    expect(docToMarkdown(doc(p(t('甲', l), t('乙', l, bold), t('丙', l))))).toBe('[甲**乙**丙](https://example.com)');
  });

  it('文件裡有不認得的標記（底線）就略過標記，文字留著', () => {
    expect(docToMarkdown(doc(p(t('底線', { type: 'underline' }))))).toBe('底線');
  });

  it('程式碼加粗體：程式碼在裡面', () => {
    expect(docToMarkdown(doc(p(t('x', bold, code))))).toBe('**`x`**');
  });

  it('文字的 Markdown 符號只在會被誤讀時才跳脫', () => {
    expect(roundTrip('a\\*b 與 \\[x] 與 1\\. y')).toBe('a\\*b 與 \\[x] 與 1. y');
    expect(docToMarkdown(doc(p(t('2 * 3 = 6'))))).toBe('2 \\* 3 = 6');
  });
});

describe('& 符號', () => {
  it('網址裡的 &y=2 不跳脫', () => {
    expect(roundTrip('https://example.com/a?x=1&y=2')).toBe('https://example.com/a?x=1&y=2');
  });

  it('看起來像字元參照的 &amp; 還是跳脫，讀回來是同樣的文字', () => {
    const doc1 = docToMarkdown({ type: 'doc', content: [p(t('a &amp; b &#35; c'))] });
    expect(doc1).toBe('a \\&amp; b \\&#35; c');
    expectStable(doc1);
  });
});

describe('提及', () => {
  it('前面的 @ 屬於提及', () => {
    expect(firstBlock(`請 @[王小明](mention:${PERSON}) 看`)).toEqual(
      p(t('請 '), node('mention', { id: PERSON, label: '王小明' }), t(' 看')),
    );
  });

  it('前面沒有 @ 的提及也讀成提及，寫回去補上 @', () => {
    expect(firstBlock(`[王小明](mention:${PERSON})`)).toEqual(p(node('mention', { id: PERSON, label: '王小明' })));
    expect(roundTrip(`[王小明](mention:${PERSON})`)).toBe(`@[王小明](mention:${PERSON})`);
  });

  it('@ 前面還有別的 @，只拿掉最後那一個', () => {
    expect(firstBlock(`a@@[x](mention:${PERSON})`)).toEqual(p(t('a@'), node('mention', { id: PERSON, label: 'x' })));
  });

  it('label 裡有特殊字：寫的時候拿掉', () => {
    const written = docToMarkdown(doc(p(node('mention', { id: PERSON, label: ' [王]\\小`明*_<\n ' }))));
    expect(written).toBe(`@[王小明](mention:${PERSON})`);
  });

  it('人名是空的（或拿完是空的）用「成員」', () => {
    expect(docToMarkdown(doc(p(node('mention', { id: PERSON, label: '' }))))).toBe(`@[成員](mention:${PERSON})`);
    expect(docToMarkdown(doc(p(node('mention', { id: PERSON, label: '[]' }))))).toBe(`@[成員](mention:${PERSON})`);
    expect(docToMarkdown(doc(p(node('mention', { id: PERSON }))))).toBe(`@[成員](mention:${PERSON})`);
  });

  it('編號有百分比編碼時解開', () => {
    expect(firstBlock('[x](mention:a%20b)')).toEqual(p(node('mention', { id: 'a b', label: 'x' })));
  });

  it('粗體裡的提及、提及後面緊接標點', () => {
    expectStable(`**請 @[王小明](mention:${PERSON})**`);
    expectStable(`@[王小明](mention:${PERSON})，請看。`);
  });
});

describe('附件與圖片', () => {
  it('附件圖片：src 原文', () => {
    expect(firstBlock(`![截圖.png](attachment:${GUID})`)).toEqual(p(node('image', { src: `attachment:${GUID}`, alt: '截圖.png', title: null })));
  });

  it('附件檔案：GUID 小寫，name 是連結文字', () => {
    expect(firstBlock(`[報告.pdf](attachment:${GUID.toUpperCase()})`)).toEqual(p(node('attachmentFile', { attachmentId: GUID, name: '報告.pdf' })));
    expect(roundTrip(`[報告.pdf](attachment:${GUID})`)).toBe(`[報告.pdf](attachment:${GUID})`);
  });

  it('檔名裡的中括號跳脫後讀得回來', () => {
    const markdown = `[a\\[1\\].pdf](attachment:${GUID})`;
    expect(firstBlock(markdown)).toEqual(p(node('attachmentFile', { attachmentId: GUID, name: 'a[1].pdf' })));
    expectStable(markdown);
  });

  it('外部圖片：照樣是 image，title 保留', () => {
    expect(firstBlock('![圖](https://example.com/a.png "標題")')).toEqual(
      p(node('image', { src: 'https://example.com/a.png', alt: '圖', title: '標題' })),
    );
    expect(roundTrip('![圖](https://example.com/a.png "標題")')).toBe('![圖](https://example.com/a.png "標題")');
  });

  it('image 沒有 alt、title 時寫成最短的寫法', () => {
    expect(docToMarkdown(doc(p(node('image', { src: 'https://example.com/a.png' }))))).toBe('![](https://example.com/a.png)');
    expect(docToMarkdown(doc(p(node('image', undefined))))).toBe('![]()');
  });

  it('attachment: 後面不是 GUID 的連結與圖片就是一般的連結與圖片', () => {
    expect(firstBlock('[x](attachment:abc)')).toEqual(p(t('x', link('attachment:abc'))));
    expect(firstBlock('![x](attachment:abc)')).toEqual(p(node('image', { src: 'attachment:abc', alt: 'x', title: null })));
    expect(roundTrip('[x](attachment:abc) ![y](attachment:abc)')).toBe('[x](attachment:abc) ![y](attachment:abc)');
  });

  it('包在連結裡的附件圖片：連結標記放在圖片節點上', () => {
    const markdown = `[![x](attachment:${GUID})](https://example.com)`;
    expect(firstBlock(markdown)).toEqual(
      p({ ...node('image', { src: `attachment:${GUID}`, alt: 'x', title: null }), marks: [link('https://example.com')] }),
    );
    expect(roundTrip(markdown)).toBe(markdown);
  });

  it('附件檔案外面的連結標記拿掉（連結不能包連結）', () => {
    const file = { ...node('attachmentFile', { attachmentId: GUID, name: 'a.pdf' }), marks: [link('https://example.com'), bold] };
    expect(docToMarkdown(doc(p(file)))).toBe(`**[a.pdf](attachment:${GUID})**`);
  });

  it('參照式的圖片換成一般的圖片', () => {
    expect(roundTrip('![圖][a]\n\n[a]: https://example.com/a.png "t"')).toBe('![圖](https://example.com/a.png "t")');
  });
});

describe('hardBreak', () => {
  it('段落裡的換行是 hardBreak，寫回去是單一的 \\n', () => {
    expect(firstBlock('甲\n乙')).toEqual(p(t('甲'), br, t('乙')));
    expect(docToMarkdown(doc(p(t('甲'), br, t('乙'))))).toBe('甲\n乙');
  });

  it('兩個空白或反斜線結尾的換行也是 hardBreak', () => {
    expect(firstBlock('甲  \n乙')).toEqual(p(t('甲'), br, t('乙')));
    expect(firstBlock('甲\\\n乙')).toEqual(p(t('甲'), br, t('乙')));
  });

  it('連續的換行：第二個起寫成反斜線加換行，讀回來還是同樣個數', () => {
    const two = docToMarkdown(doc(p(t('甲'), br, br, t('乙'))));
    expect(two).toBe('甲\n\\\n乙');
    expect(markdownToDoc(two)).toEqual(doc(p(t('甲'), br, br, t('乙'))));
    const three = docToMarkdown(doc(p(t('甲'), br, br, br, t('乙'))));
    expect(markdownToDoc(three)).toEqual(doc(p(t('甲'), br, br, br, t('乙'))));
  });

  it.each(['- 一', '# 標題', '1. 一', '> 引用', '+ 一', '```', '---'])('換行後面接 %j：寫回去被跳脫，讀回來還是同一份文件', (next) => {
    const original = doc(p(t('前'), br, t(next)));
    const written = docToMarkdown(original);
    expect(written).not.toBe(`前\n${next}`);
    expect(markdownToDoc(written)).toEqual(original);
  });

  it('段落開頭與結尾的換行寫不出來，拿掉', () => {
    expect(docToMarkdown(doc(p(br, t('甲'), br)))).toBe('甲');
    expect(docToMarkdown(doc(p(br)))).toBe('');
  });

  it('標記跨過換行：換行跟著前後共有的標記', () => {
    expect(firstBlock('*甲\n乙*')).toEqual(p(t('甲', italic), { type: 'hardBreak', marks: [italic] }, t('乙', italic)));
    expect(docToMarkdown(doc(p(t('甲', italic), br, t('乙', italic))))).toBe('*甲\n乙*');
    expectStable('*甲*\n*乙*');
  });

  it('前後的標記不同時，換行沒有標記', () => {
    expect(firstBlock('**甲**\n*乙*')).toEqual(p(t('甲', bold), br, t('乙', italic)));
  });

  it('程式碼之間的換行沒有程式碼標記', () => {
    expect(firstBlock('`甲`\n`乙`')).toEqual(p(t('甲', code), br, t('乙', code)));
  });
});

describe('不認得的行內寫法原樣保留', () => {
  it('行內 HTML', () => {
    expect(firstBlock('甲 <b>粗</b> 乙')).toEqual(
      p(t('甲 '), node('rawInline', { source: '<b>' }), t('粗'), node('rawInline', { source: '</b>' }), t(' 乙')),
    );
    expect(roundTrip('甲 <b>粗</b> 乙')).toBe('甲 <b>粗</b> 乙');
  });

  it('註腳的參照', () => {
    expect(roundTrip('甲[^a]\n\n[^a]: 說明')).toBe('甲[^a]\n\n[^a]: 說明');
  });

  it('參照式的連結：用到的換成一般連結，用到的定義拿掉', () => {
    const markdown = '[甲][a] [乙][] [丙]\n\n[a]: https://example.com/a\n[乙]: https://example.com/b "t"\n[丙]: https://example.com/c';
    expect(roundTrip(markdown)).toBe('[甲](https://example.com/a) [乙](https://example.com/b "t") [丙](https://example.com/c)');
  });

  it('參照式的連結：沒用到的定義留成原文', () => {
    expect(roundTrip('[甲][a]\n\n[a]: https://example.com/a\n\n[b]: https://example.com/b')).toBe(
      '[甲](https://example.com/a)\n\n[b]: https://example.com/b',
    );
  });

  it('沒有定義的參照式寫法就是一般文字', () => {
    expect(firstBlock('[甲][x]')).toEqual(p(t('[甲][x]')));
    expectStable('[甲][x]');
  });
});

describe('uploadPlaceholder', () => {
  const imagePlaceholder = '![上傳中：截圖.png…]()';
  const filePlaceholder = '[上傳中：報告.pdf…]()';

  it('原文輸出，不跳脫', () => {
    const written = docToMarkdown(
      doc(
        p(t('前'), br, node('uploadPlaceholder', { placeholder: imagePlaceholder, name: '截圖.png', isImage: true })),
        p(node('uploadPlaceholder', { placeholder: filePlaceholder, name: '報告.pdf', isImage: false })),
      ),
    );
    expect(written).toBe(`前\n${imagePlaceholder}\n\n${filePlaceholder}`);
  });

  it('寫出來的記號 stripPlaceholders 拿得掉', () => {
    const written = docToMarkdown(
      doc(p(t('前'), br, node('uploadPlaceholder', { placeholder: imagePlaceholder, name: '截圖.png', isImage: true }), br, t('後'))),
    );
    const stripped = stripPlaceholders(written);
    expect(stripped.removed).toBe(1);
    expect(stripped.value).toBe('前\n後');
  });

  it('標記包著的記號不加標記（原文不能被改）', () => {
    const placeholder = { ...node('uploadPlaceholder', { placeholder: filePlaceholder, name: '報告.pdf', isImage: false }), marks: [bold] };
    expect(docToMarkdown(doc(p(placeholder)))).toBe(filePlaceholder);
  });
});

describe('標點在標記內側、字在標記外側', () => {
  it.each([
    ['粗體結尾的全形冒號', p(t('重點：', bold), t('這是')), '**重點**：這是', p(t('重點', bold), t('：這是'))],
    ['粗體包著全形引號', p(t('「引用」', bold), t('文字')), '**「引用**」文字', p(t('「引用', bold), t('」文字'))],
    ['斜體結尾的句號', p(t('說明。', italic), t('下一句')), '*說明*。下一句', p(t('說明', italic), t('。下一句'))],
    ['刪除線結尾的句號', p(t('刪除。', strike), t('下一句')), '~~刪除~~。下一句', p(t('刪除', strike), t('。下一句'))],
    ['標記前面是字、內側開頭是標點', p(t('文字'), t('（括號）', bold), t('後面')), '文字（**括號**）後面', p(t('文字（'), t('括號', bold), t('）後面'))],
  ])('%s：寫出來沒有字元參照，讀回來標點在外面', (_name, input, expectedMarkdown, expectedDoc) => {
    const written = docToMarkdown(doc(input));

    expect(written).toBe(expectedMarkdown);
    expect(written).not.toContain('&#x');
    expect(markdownToDoc(written).content?.[0]).toEqual(expectedDoc);
  });

  it('外側是空白或標點時不動', () => {
    expect(docToMarkdown(doc(p(t('重點：', bold), t(' 這是'))))).toBe('**重點：** 這是');
    expect(docToMarkdown(doc(p(t('重點：', bold), t('（註）'))))).toBe('**重點：**（註）');
  });

  it('整段都是標點時不動', () => {
    expect(docToMarkdown(doc(p(t('（）', bold), t('字'))))).toBe('**（）**&#x5B57;');
  });
});

describe('標記裡的原始行內 HTML', () => {
  it('粗體裡的 HTML 帶著粗體，寫回去標記不被拆開', () => {
    const markdown = '**a <kbd>x</kbd> b**';
    const block = firstBlock(markdown);

    expect(block.content?.map((child) => child.type)).toEqual(['text', 'rawInline', 'text', 'rawInline', 'text']);
    expect(block.content?.every((child) => child.marks?.[0]?.type === 'bold')).toBe(true);
    expect(roundTrip(markdown)).toBe(markdown);
  });

  it('連結裡的 HTML 還是同一個連結', () => {
    const markdown = '[a <b>x</b> b](https://e.com)';

    expect(roundTrip(markdown)).toBe(markdown);
  });
});

describe('附件連結的內容不是純文字', () => {
  it('包著圖片的附件連結：圖片留著，當一般的連結', () => {
    const markdown = `[![shot](attachment:${GUID})](attachment:${GUID})`;
    const block = firstBlock(markdown);

    expect(block.content?.[0]?.type).toBe('image');
    expect(roundTrip(markdown)).toBe(markdown);
  });

  it('裡面有粗體：粗體留著', () => {
    const markdown = `[**bold** name](attachment:${GUID})`;

    expect(roundTrip(markdown)).toBe(markdown);
  });

  it('檔名有星號與底線：跳脫過的寫法讀回原本的檔名，寫回去不變', () => {
    const markdown = `[a\\*b\\*c\\_d.pdf](attachment:${GUID})`;
    const file = firstBlock(markdown).content?.[0];

    expect(file?.attrs?.name).toBe('a*b*c_d.pdf');
    expect(roundTrip(markdown)).toBe(`[a\\*b\\*c_d.pdf](attachment:${GUID})`);
  });

  it('純文字的附件連結還是附件檔案', () => {
    expect(firstBlock(`[報告.pdf](attachment:${GUID})`).content?.[0]?.type).toBe('attachmentFile');
  });
});
