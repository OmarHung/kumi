import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { docToMarkdown, markdownToDoc } from './markdownDoc';
import { GUID, PERSON, expectStable } from './markdownDocTestKit';

const TEMPLATE_PATH = resolve(process.cwd(), '../src/Modules/PmTool.Work/Templates/Descriptions/web_project.md');

const EXAMPLE_DESCRIPTIONS = [
  '這個專案只有一種類型「缺陷」，流程是：新回報 → 已確認 → 修正中 → 待驗證 → 已關閉。\n\n- 回報時填嚴重度、重現步驟、環境與發現版本；「不處理」用來收重複與無法重現的回報。\n- 驗證沒過可以從「待驗證」退回「修正中」。\n- 表格裡的「嚴重度」一眼看得出哪些最急，先處理「致命」與「嚴重」。\n\n這幾筆是範例，看懂之後可以逐筆刪掉。\n',
  '## 流程\n\n1. 草稿\n2. 審核\n3. 發布\n\n> 提醒：發布前要有第二個人看過。\n',
];

const OWN_CONTENT = [
  '主機 web_01 的部署資料夾是 C:\\apps\\example\\site_a，網址 https://www.example.com/a_b?x=1&y=2。重新啟動後再看。',
  '見 https://www.example.com/path，請 @[王小明](mention:' + PERSON + ') 看一下；另一位 @[李小華](mention:' + PERSON + ')，也請確認。',
  '| 主機 | 角色 | 備註 |\n| :-- | :-: | --: |\n| web_01 | 前台 | 2 \\| 3 |\n| db_01 | 資料庫 | `a|b` |',
  '- [ ] 備份資料庫\n- [x] 更新憑證\n  - [ ] 通知客戶\n- [ ] 重新啟動 `example.service`',
  '```bash\nsudo systemctl restart example.service\ncd C:\\apps && echo "a_b *x* [y]"\n```',
  '截圖如下：\n\n![畫面.png](attachment:' + GUID + ')\n\n報告：[月報.pdf](attachment:' + GUID + ')',
  '第一行\n第二行\n- 看起來像清單\n# 看起來像標題\n1. 看起來像編號\n> 看起來像引用',
  '**重要：** *請* ~~不要~~ 動 `web_01`，詳見 [說明](https://example.com/doc "文件") 與 <https://example.com/x>。',
  '連結接中文標點：https://www.example.com/a，然後（https://www.example.com/b）。',
  '<div>原始 HTML</div>\n\n文字中的 <kbd>Ctrl</kbd> 鍵與註腳[^1]\n\n[^1]: 註腳內容\n\n[x]: https://example.com/unused',
  '*斜 **粗斜** 斜* 與 **粗 *斜粗* 粗** 與 a*b*c 與 2 * 3',
  '縮排\n\n    寫成程式碼\n\n* 星號清單\n* 第二項\n\n---\n\n+ 加號清單',
  '[參照][1] 與 ![圖][2]\n\n[1]: https://example.com/a\n[2]: https://example.com/b.png "標題"',
  '- 一\n\n- 二\n\n  第二段\n\n- 三',
  '5. 從五開始\n6. 六\n\n> 引用\n> 兩行\n\n> > 巢狀',
  '標題\n===\n\n子標題\n---\n\n###### 六級',
];

describe('穩定性：讀、寫、再讀得到同一份文件', () => {
  const inputs: [string, string][] = [
    ['說明格式（web_project.md）', readFileSync(TEMPLATE_PATH, 'utf8')],
    ...EXAMPLE_DESCRIPTIONS.map((text, index): [string, string] => [`範例項目的說明 ${String(index + 1)}`, text]),
    ...OWN_CONTENT.map((text, index): [string, string] => [`自己寫的內容 ${String(index + 1)}`, text]),
  ];

  it.each(inputs)('%s', (_name, markdown) => {
    const { first, written } = expectStable(markdown);
    expect(first.type).toBe('doc');
    // 寫兩次的結果相同，也沒有結尾的換行。
    expect(docToMarkdown(markdownToDoc(written))).toBe(written);
    expect(written.endsWith('\n')).toBe(false);
  });

  it('說明格式的標題、清單與全形空白都保留', () => {
    const written = docToMarkdown(markdownToDoc(readFileSync(TEMPLATE_PATH, 'utf8')));
    expect(written).toContain('## 部署環境');
    expect(written).toContain('- 網域註冊在：　到期：');
    expect(written).toContain('- 帳號密碼：寫密碼管理器裡的項目名稱，不要貼在這裡');
  });

  it('自己的內容裡不該被改的字原樣留著', () => {
    const written = docToMarkdown(markdownToDoc(OWN_CONTENT[0] ?? ''));
    expect(written).toBe(OWN_CONTENT[0]);
    expect(docToMarkdown(markdownToDoc(OWN_CONTENT[8] ?? ''))).toBe(OWN_CONTENT[8]);
  });
});
