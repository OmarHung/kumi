import { readdirSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { render } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { findEditor, tiptapOf } from '../../test/richText';
import { EditorHarness } from '../../test/richTextHarness';
import { docToMarkdown, markdownToDoc } from './markdown/markdownDoc';

const GUID = '0f8fad5b-d9cb-469f-a165-70867728950e';
const PERSON = '3b241101-e2bb-4255-8caf-4136c566a962';
const TEMPLATE_DIRECTORY = resolve(process.cwd(), '../src/Modules/PmTool.Work/Templates/Descriptions');

// 合約 4.1、4.2 的每一種節點與標記至少出現一次，加上正規化的例子（4.5）。
const SAMPLES: readonly (readonly [string, string])[] = [
  ['段落、換行、標題 1～6', '第一行\n第二行\n\n# 一\n\n## 二\n\n### 三\n\n#### 四\n\n##### 五\n\n###### 六'],
  ['粗體、斜體、刪除線、行內程式碼、連結（含 title）', '**粗** *斜* ~~刪~~ `code` [連結](https://example.com/a "標題") <https://example.com/x>'],
  ['引用與巢狀引用', '> 引用\n> 兩行\n\n> > 巢狀'],
  ['項目清單（緊密與鬆散）、編號清單（從 5 開始）', '- 一\n- 二\n  - 巢狀\n\n- 三\n\n- 四\n\n5. 五\n6. 六'],
  ['待辦清單（巢狀、勾選）', '- [ ] 備份\n- [x] 憑證\n  - [ ] 通知客戶\n- [ ] 重新啟動 `example.service`'],
  ['程式碼區塊（語言與 meta）、水平線', '```bash title="a"\nsudo systemctl restart example.service\n```\n\n---\n\n```\n無語言\n```'],
  ['表格（對齊、行內標記）', '| 主機 | 角色 | 備註 |\n| :-- | :-: | --: |\n| web_01 | **前台** | 2 \\| 3 |'],
  ['提及、附件圖片、附件檔案、外部圖片', `請 @[王小明](mention:${PERSON}) 看\n\n![截圖.png](attachment:${GUID})\n\n[報告.pdf](attachment:${GUID})\n\n![外部](https://example.com/a.png "標題")`],
  ['不認得的寫法：HTML 區塊、行內 HTML、註腳、沒用到的連結定義', '<div>原始 HTML</div>\n\n字 <kbd>Ctrl</kbd> 字[^1]\n\n[^1]: 註腳內容\n\n[x]: https://example.com/unused'],
  ['參照式連結換成一般連結', '[參照][1] 與 ![圖][2]\n\n[1]: https://example.com/a\n[2]: https://example.com/b.png'],
  ['正規化：* 清單、清單前沒有空行', '說明：\n- 一\n\n* 星號\n* 星號二'],
  ['不用跳脫的字：底線、反斜線、網址', '主機 web_01 的 C:\\apps\\site_a，網址 https://www.example.com/a_b?x=1 結束。'],
  ['空字串', ''],
];

function templates(): readonly (readonly [string, string])[] {
  const names = readdirSync(TEMPLATE_DIRECTORY).filter((name) => name.endsWith('.md'));
  return names.map((name): [string, string] => [`內建的說明格式 ${name}`, readFileSync(resolve(TEMPLATE_DIRECTORY, name), 'utf8')]);
}

describe('編輯器的結構收得下轉換產生的每一種節點', () => {
  it.each([...SAMPLES, ...templates()])('%s：放進編輯器再轉回 Markdown，和直接轉換一樣，而且沒有通知變動', async (_name, markdown) => {
    // Arrange
    const onValue = vi.fn();
    render(<EditorHarness initial={markdown} onValue={onValue} />);
    const editor = await findEditor('說明');

    // Act
    const loaded = tiptapOf(editor).getJSON();

    // Assert
    expect(() => {
      tiptapOf(editor).schema.nodeFromJSON(markdownToDoc(markdown)).check();
    }).not.toThrow();
    expect(docToMarkdown(loaded)).toBe(docToMarkdown(markdownToDoc(markdown)));
    expect(onValue).not.toHaveBeenCalled();
  });
});
