import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Markdown, PlainText } from './Markdown';

const MEMBERS = new Map([['p-1', '王小明']]);

async function renderMarkdown(text: string, mentionNames: ReadonlyMap<string, string> = MEMBERS, hasAttachments = true) {
  const view = render(<Markdown text={text} mentionNames={mentionNames} hasAttachments={hasAttachments} />);
  // 渲染器是延後載入的：先是純文字的備用畫面（data-state="plain"），載入好之後換成 Markdown（"rendered"）。
  await waitFor(() => {
    expect(view.container.querySelector('[data-state]')).toHaveAttribute('data-state', 'rendered');
  });
  return view;
}

describe('Markdown 顯示', () => {
  it('標題、清單、粗體、程式碼、引用、刪除線都畫成對應的元素', async () => {
    // Arrange & Act
    await renderMarkdown('## 目標\n\n- 第一項\n- **第二項**\n\n`npm test`\n\n> 引用\n\n~~舊的~~');

    // Assert
    expect(screen.getByRole('heading', { name: '目標' })).toBeInTheDocument();
    expect(within(screen.getByRole('list')).getAllByRole('listitem')).toHaveLength(2);
    expect(screen.getByText('第二項').tagName).toBe('STRONG');
    expect(screen.getByText('npm test').tagName).toBe('CODE');
    expect(screen.getByText('引用').closest('blockquote')).not.toBeNull();
    expect(screen.getByText('舊的').tagName).toBe('DEL');
  });

  it('內容裡的標題不搶頁面的層級：最高從 h4 開始', async () => {
    // Arrange & Act
    await renderMarkdown('# 一\n\n## 二\n\n### 三\n\n###### 六');

    // Assert
    const levels = screen.getAllByRole('heading').map((heading) => heading.tagName);
    expect(levels).toEqual(['H4', 'H4', 'H5', 'H6']);
  });

  it('單一換行就是換行', async () => {
    // Arrange & Act
    const { container } = await renderMarkdown('第一行\n第二行');

    // Assert
    expect(container.querySelectorAll('br')).toHaveLength(1);
  });

  it('表格：有表頭與儲存格，外面包一層可以橫向捲動的容器', async () => {
    // Arrange & Act
    await renderMarkdown('| 名稱 | 數量 |\n| --- | --- |\n| 蘋果 | 3 |');

    // Assert
    const table = screen.getByRole('table');
    expect(within(table).getAllByRole('columnheader').map((cell) => cell.textContent)).toEqual(['名稱', '數量']);
    expect(within(table).getByRole('cell', { name: '蘋果' })).toBeInTheDocument();
    expect(table.parentElement).toHaveAttribute('tabindex', '0');
  });

  it('工作清單是唯讀的：沒有可以按的勾選框，用「已完成」「未完成」說出狀態', async () => {
    // Arrange & Act
    const { container } = await renderMarkdown('- [x] 做完了\n- [ ] 還沒做');

    // Assert
    expect(container.querySelector('input')).toBeNull();
    expect(screen.getByRole('img', { name: '已完成' }).closest('li')).toHaveTextContent('做完了');
    expect(screen.getByRole('img', { name: '未完成' }).closest('li')).toHaveTextContent('還沒做');
  });

  it('還沒載入好之前的備用畫面是純文字，換行與縮排照原樣', () => {
    // Arrange & Act
    const { container } = render(<PlainText text={'第一行\n  第二行'} />);

    // Assert
    expect(container.firstElementChild).toHaveAttribute('data-state', 'plain');
    expect(container.firstElementChild?.textContent).toBe('第一行\n  第二行');
  });

  it('http、https、mailto 的連結在新分頁開，帶 noopener noreferrer', async () => {
    // Arrange & Act
    await renderMarkdown('[網站](https://example.com/a) [信](mailto:user@example.com) [舊的](http://example.com)');

    // Assert
    for (const name of ['網站', '信', '舊的']) {
      const link = screen.getByRole('link', { name });
      expect(link).toHaveAttribute('target', '_blank');
      expect(link).toHaveAttribute('rel', 'noopener noreferrer');
    }
    expect(screen.getByRole('link', { name: '網站' })).toHaveAttribute('href', 'https://example.com/a');
  });

  it('單獨貼上的網址也變成連結（GFM）', async () => {
    // Arrange & Act
    await renderMarkdown('看這裡 https://example.com/path 就好');

    // Assert
    expect(screen.getByRole('link', { name: 'https://example.com/path' })).toHaveAttribute('href', 'https://example.com/path');
  });

  it('單獨貼上的網址後面緊接中文標點時，連結停在標點前，後面的字不算進網址', async () => {
    // Arrange & Act
    const view = await renderMarkdown('（Google Sheet：https://example.com/sheet/edit），麻煩照這份改：\n網址 https://example.com/a。看完回我');

    // Assert
    expect(screen.getByRole('link', { name: 'https://example.com/sheet/edit' })).toHaveAttribute('href', 'https://example.com/sheet/edit');
    expect(screen.getByRole('link', { name: 'https://example.com/a' })).toHaveAttribute('href', 'https://example.com/a');
    expect(view.container).toHaveTextContent('https://example.com/sheet/edit），麻煩照這份改：');
    expect(view.container).toHaveTextContent('https://example.com/a。看完回我');
  });

  it('www. 開頭的網址與 Email 後面緊接中文標點時一樣切開，補上的 http:// 與 mailto: 留著', async () => {
    // Arrange & Act
    await renderMarkdown('先看 www.example.com/docs，再寫信到 help@example.com。');

    // Assert
    expect(screen.getByRole('link', { name: 'www.example.com/docs' })).toHaveAttribute('href', 'http://www.example.com/docs');
    expect(screen.getByRole('link', { name: 'help@example.com' })).toHaveAttribute('href', 'mailto:help@example.com');
  });

  it('網址路徑裡的中文字照舊是網址的一部分；明確寫出的連結不切', async () => {
    // Arrange & Act
    await renderMarkdown('https://example.com/wiki/台灣 與 <https://example.com/x，y> 與 [說明](https://example.com/z)');

    // Assert
    expect(screen.getByRole('link', { name: 'https://example.com/wiki/台灣' })).toHaveAttribute(
      'href',
      'https://example.com/wiki/%E5%8F%B0%E7%81%A3',
    );
    expect(screen.getByRole('link', { name: 'https://example.com/x，y' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '說明' })).toHaveAttribute('href', 'https://example.com/z');
  });
});

describe('Markdown 的安全', () => {
  it('<script> 與其他原始的 HTML 不會變成元素，照文字顯示', async () => {
    // Arrange & Act
    const { container } = await renderMarkdown('前 <script>window.__pwned = 1</script> 後 <img src=x onerror="window.__pwned = 2"> <b>粗</b>');

    // Assert
    expect(container.querySelector('script')).toBeNull();
    expect(container.querySelector('img')).toBeNull();
    expect(container.querySelector('b')).toBeNull();
    expect(container.querySelector('[onerror]')).toBeNull();
    expect(container).toHaveTextContent('<script>window.__pwned = 1</script>');
    expect('__pwned' in window).toBe(false);
  });

  it.each([
    ['javascript:', '[點我](javascript:alert(1))'],
    ['大小寫混合的 javascript:', '[點我](JaVaScRiPt:alert(1))'],
    ['夾著空白字元的 javascript:', '[點我](java&#x09;script:alert(1))'],
    ['data:', '[點我](data:text/html;base64,PHNjcmlwdD4=)'],
    ['vbscript:', '[點我](vbscript:msgbox(1))'],
    ['file:', '[點我](file:///etc/passwd)'],
    ['相對路徑', '[點我](/v1/session)'],
    ['協定相對網址', '[點我](//example.com/x)'],
  ])('%s 的連結不是連結，只剩文字', async (_name, text) => {
    // Arrange & Act
    const { container } = await renderMarkdown(text);

    // Assert
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
    expect(container.querySelector('[href]')).toBeNull();
    expect(screen.getByText('點我')).toBeInTheDocument();
  });

  it('參照式的連結一樣只認允許的協定', async () => {
    // Arrange & Act
    const { container } = await renderMarkdown('[點我][1]\n\n[1]: javascript:alert(1)');

    // Assert
    expect(container.querySelector('[href]')).toBeNull();
  });

  it('圖片不載入：畫成連結，圖片的說明文字是連結的名稱', async () => {
    // Arrange & Act
    const { container } = await renderMarkdown('![設計稿](https://example.com/a.png)');

    // Assert
    expect(container.querySelector('img')).toBeNull();
    const link = screen.getByRole('link', { name: '設計稿' });
    expect(link).toHaveAttribute('href', 'https://example.com/a.png');
    expect(link).toHaveAttribute('target', '_blank');
  });

  it('沒有說明文字的圖片，連結的名稱是網址；網址不被允許的圖片只剩說明文字', async () => {
    // Arrange & Act
    const { container } = await renderMarkdown('![](https://example.com/b.png) ![壞的](javascript:alert(1))');

    // Assert
    expect(container.querySelector('img')).toBeNull();
    expect(screen.getByRole('link', { name: 'https://example.com/b.png' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: '壞的' })).not.toBeInTheDocument();
    expect(screen.getByText('壞的')).toBeInTheDocument();
  });

  it('程式碼區塊裡的 HTML 與連結語法照原樣顯示', async () => {
    // Arrange & Act
    const { container } = await renderMarkdown('```\n<script>alert(1)</script>\n[x](javascript:alert(1))\n```');

    // Assert
    expect(container.querySelector('script')).toBeNull();
    expect(container.querySelector('a')).toBeNull();
    expect(container.querySelector('pre')).toHaveTextContent('<script>alert(1)</script>');
  });
});

describe('Markdown 的提及', () => {
  it('@[名稱](mention:編號) 畫成「@名稱」，名稱用目前成員的名字', async () => {
    // Arrange & Act
    const { container } = await renderMarkdown('請 @[舊名字](mention:p-1) 看一下');

    // Assert
    expect(screen.getByText('@王小明')).toBeInTheDocument();
    expect(container).toHaveTextContent('請 @王小明 看一下');
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });

  it('找不到這個人（不是成員了）就用連結的文字', async () => {
    // Arrange & Act
    const { container } = await renderMarkdown('@[陳小華](mention:p-9) 你好');

    // Assert
    expect(container).toHaveTextContent('@陳小華 你好');
  });

  it('提及前面的 @ 不會重複', async () => {
    // Arrange & Act
    const { container } = await renderMarkdown('@[王小明](mention:p-1)');

    // Assert
    expect(container).not.toHaveTextContent('@@');
  });

  it('只有 [名稱](mention:…) 沒有前面的 @ 時也是提及', async () => {
    // Arrange & Act
    const { container } = await renderMarkdown('[王小明](mention:p-1)');

    // Assert
    expect(container).toHaveTextContent('@王小明');
  });

  it('提及的名稱只當文字：名稱裡的 HTML 不會變成元素', async () => {
    // Arrange & Act
    const { container } = await renderMarkdown('@[<b>壞</b>](mention:p-9)');

    // Assert
    expect(container.querySelector('b')).toBeNull();
  });

  it('找不到人的提及是沒有底色的普通文字，找得到的才有提及的樣子', async () => {
    // Arrange & Act
    const { container } = await renderMarkdown('@[王小明](mention:p-1) 與 @[陳小華](mention:p-9)');

    // Assert
    expect(screen.getByText('@王小明').className).toMatch(/mention/);
    const unknown = screen.getByText('@陳小華');
    expect(unknown.className).not.toMatch(/mention/);
    expect(container).toHaveTextContent('@王小明 與 @陳小華');
  });
});

describe('Markdown 重新渲染', () => {
  it('呼叫端重新渲染（成員的 Map 內容沒變）之後，同一個 DOM 節點還在，不會整段拆掉重建', async () => {
    // Arrange
    const names = new Map([['p-1', '王小明']]);
    const view = await renderMarkdown('**粗體** 與 @[王小明](mention:p-1)', names);
    const bold = screen.getByText('粗體');
    const mention = screen.getByText('@王小明');

    // Act
    view.rerender(<Markdown text="**粗體** 與 @[王小明](mention:p-1)" mentionNames={names} />);
    view.rerender(<Markdown text="**粗體** 與 @[王小明](mention:p-1)" mentionNames={new Map(names)} />);

    // Assert
    expect(screen.getByText('粗體')).toBe(bold);
    expect(screen.getByText('@王小明')).toBe(mention);
  });

  it('成員的名字變了：提及換成新的名字，其他的節點還是同一個', async () => {
    // Arrange
    const view = await renderMarkdown('**粗體** 與 @[王小明](mention:p-1)', new Map([['p-1', '王小明']]));
    const bold = screen.getByText('粗體');

    // Act
    view.rerender(<Markdown text="**粗體** 與 @[王小明](mention:p-1)" mentionNames={new Map([['p-1', '王大明']])} />);

    // Assert
    expect(screen.getByText('@王大明')).toBeInTheDocument();
    expect(screen.getByText('粗體')).toBe(bold);
  });
});

const ATTACHMENT_ID = '0190a100-0000-7000-8000-000000000001';

describe('Markdown 的附件', () => {
  it('沒有附件可以對（hasAttachments 是 false）：附件的圖片與連結只留文字，不載入也不連', async () => {
    // Arrange & Act
    const { container } = await renderMarkdown(
      `![截圖.png](attachment:${ATTACHMENT_ID}) [報告](attachment:${ATTACHMENT_ID}) [官網](https://www.example.com)`,
      MEMBERS,
      false,
    );

    // Assert
    expect(container.querySelector('img')).toBeNull();
    expect(screen.getByText('截圖.png')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: '報告' })).not.toBeInTheDocument();
    expect(screen.getByText('報告')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '官網' })).toHaveAttribute('href', 'https://www.example.com');
  });

  it('attachment: 的圖片用附件的下載端點載入：lazy，點了在新分頁開原圖', async () => {
    // Arrange & Act
    const { container } = await renderMarkdown(`![截圖.png](attachment:${ATTACHMENT_ID})`);

    // Assert
    const image = screen.getByRole('img', { name: '截圖.png' });
    expect(image).toHaveAttribute('src', `/v1/attachments/${ATTACHMENT_ID}/content`);
    expect(image).toHaveAttribute('loading', 'lazy');
    const link = container.querySelector('a');
    expect(link).toHaveAttribute('href', `/v1/attachments/${ATTACHMENT_ID}/content`);
    expect(link).toHaveAttribute('target', '_blank');
    expect(link).toHaveAttribute('rel', 'noopener noreferrer');
  });

  it('給了 attachmentUrl 時，附件的圖片與連結都用它組網址（知識庫的頁面）', async () => {
    // Arrange
    const attachmentUrl = (id: string) => `/v1/knowledge/attachments/${id}/content`;
    const view = render(<Markdown text={`![截圖.png](attachment:${ATTACHMENT_ID}) [報告](attachment:${ATTACHMENT_ID})`} mentionNames={MEMBERS} attachmentUrl={attachmentUrl} />);

    // Act
    await waitFor(() => {
      expect(view.container.querySelector('[data-state]')).toHaveAttribute('data-state', 'rendered');
    });

    // Assert
    expect(screen.getByRole('img', { name: '截圖.png' })).toHaveAttribute('src', `/v1/knowledge/attachments/${ATTACHMENT_ID}/content`);
    expect(screen.getByRole('link', { name: '報告' })).toHaveAttribute('href', `/v1/knowledge/attachments/${ATTACHMENT_ID}/content`);
  });

  it('圖片載入失敗：換成一行「圖片已刪除或無法顯示：檔名」', async () => {
    // Arrange
    await renderMarkdown(`![截圖.png](attachment:${ATTACHMENT_ID})`);

    // Act
    fireEvent.error(screen.getByRole('img', { name: '截圖.png' }));

    // Assert
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
    expect(screen.getByText('圖片已刪除或無法顯示：截圖.png')).toBeInTheDocument();
  });

  it('圖片包在連結裡：不再自己包一層 <a>（不產生巢狀的連結），點圖片走外層的連結', async () => {
    // Arrange & Act
    const { container } = await renderMarkdown(`[![截圖.png](attachment:${ATTACHMENT_ID})](https://example.com/page)`);

    // Assert
    expect(container.querySelectorAll('a a')).toHaveLength(0);
    const links = container.querySelectorAll('a');
    expect(links).toHaveLength(1);
    expect(links[0]).toHaveAttribute('href', 'https://example.com/page');
    expect(within(links[0] as HTMLElement).getByRole('img', { name: '截圖.png' })).toBeInTheDocument();
  });

  it('說明文字是空的：名稱退回「圖片」，載入失敗也這樣說', async () => {
    // Arrange
    await renderMarkdown(`![](attachment:${ATTACHMENT_ID})`);
    expect(screen.getByRole('img', { name: '圖片' })).toBeInTheDocument();

    // Act
    fireEvent.error(screen.getByRole('img', { name: '圖片' }));

    // Assert
    expect(screen.getByText('圖片已刪除或無法顯示：圖片')).toBeInTheDocument();
  });

  it('換成另一個附件時，前一張的「載入失敗」不帶過來', async () => {
    // Arrange
    const other = '0190a100-0000-7000-8000-000000000002';
    const view = await renderMarkdown(`![一](attachment:${ATTACHMENT_ID})`);
    fireEvent.error(screen.getByRole('img', { name: '一' }));
    expect(screen.queryByRole('img')).not.toBeInTheDocument();

    // Act
    view.rerender(<Markdown text={`![二](attachment:${other})`} mentionNames={MEMBERS} />);

    // Assert
    expect(await screen.findByRole('img', { name: '二' })).toBeInTheDocument();
  });

  it('載入之前標成載入中（保留高度，捲到這裡之後不被推走），載入完成就拿掉', async () => {
    // Arrange
    await renderMarkdown(`![截圖.png](attachment:${ATTACHMENT_ID})`);
    const image = screen.getByRole('img', { name: '截圖.png' });
    expect(image).toHaveAttribute('data-loading', 'true');

    // Act
    fireEvent.load(image);

    // Assert
    expect(image).not.toHaveAttribute('data-loading');
  });

  it('attachment: 的連結指到下載端點', async () => {
    // Arrange & Act
    await renderMarkdown(`[規格.pdf](attachment:${ATTACHMENT_ID})`);

    // Assert
    expect(screen.getByRole('link', { name: '規格.pdf' })).toHaveAttribute('href', `/v1/attachments/${ATTACHMENT_ID}/content`);
  });

  it('檔名裡跳脫過的中括號，顯示時還原', async () => {
    // Arrange & Act
    await renderMarkdown(`[報告 \\[v2\\].pdf](attachment:${ATTACHMENT_ID})`);

    // Assert
    expect(screen.getByRole('link', { name: '報告 [v2].pdf' })).toBeInTheDocument();
  });

  it.each([
    ['後面不是 GUID', 'attachment:abc'],
    ['GUID 後面還有東西', `attachment:${ATTACHMENT_ID}/../x`],
    ['路徑', `attachment:/v1/session`],
  ])('attachment: %s：不認，只剩文字', async (_name, url) => {
    // Arrange & Act
    const { container } = await renderMarkdown(`[點我](${url}) ![圖](${url})`);

    // Assert
    expect(container.querySelector('[href]')).toBeNull();
    expect(container.querySelector('img')).toBeNull();
  });
});
