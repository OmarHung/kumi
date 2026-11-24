import { useState } from 'react';
import type { Member } from '../../api/types';
import { Button } from '../../components/Button/Button';
import fill from '../project-info/editorFill.module.css';
import type { EditorUploads } from '../attachments/useEditorUploads';
import { RichTextEditor, SUBMIT_SHORTCUT, type RichTextEditorProps } from '../rich-text/RichTextEditor';
import { Demo, DemoSection } from './DemoSection';
import styles from './Design.module.css';

// 假資料（沒有真的人）。
const MEMBERS: readonly Member[] = [
  { personId: '00000000-0000-0000-0000-000000000001', displayName: '範例甲', role: 'editor' },
  { personId: '00000000-0000-0000-0000-000000000002', displayName: '範例乙', role: 'viewer' },
  { personId: '00000000-0000-0000-0000-000000000003', displayName: 'Example Chen', role: 'editor' },
];

const SAMPLE = [
  '## 本週重點',
  '',
  '請 @[範例甲](mention:00000000-0000-0000-0000-000000000001) 看一下，**先處理**驗收，再看 [設計稿](https://example.com/design)。',
  '',
  '- [x] 備份資料庫',
  '- [ ] 更新憑證',
  '',
  '> 發布前要有第二個人看過。',
  '',
  '| 主機 | 角色 |',
  '| :-- | --: |',
  '| web_01 | 前台 |',
  '',
  '```bash',
  'sudo systemctl restart example.service',
  '```',
  '',
  '<div>不認得的原始 HTML 照原文保留</div>',
].join('\n');

type DemoProps = Partial<Omit<RichTextEditorProps, 'value' | 'onChange' | 'label'>> & {
  readonly label: string;
  readonly initial?: string;
};

function ignore(): void {
  // 展示用：選了檔案不真的上傳（真的上傳要項目與權限）。
}

/** 展示用的「可以上傳」：工具列出現「插入圖片或檔案」，選了檔案什麼都不做。 */
const DEMO_UPLOADS: EditorUploads = {
  canUpload: true,
  isUploading: false,
  addFiles: ignore,
  announceUploading: ignore,
  problems: [],
  dismissProblems: ignore,
  announcement: '',
  hasAnnouncer: false,
  binding: {
    canUpload: true,
    isDragging: false,
    attach: ignore,
    handlers: { onPaste: ignore, onDragEnter: ignore, onDragOver: ignore, onDragLeave: ignore, onDrop: ignore },
  },
};

/** 展示用的外殼：自己管內容，和呼叫端一樣是受控的。 */
function EditorDemo({ label, initial = '', ...rest }: DemoProps) {
  const [value, setValue] = useState(initial);
  return (
    <RichTextEditor
      maxLength={50_000}
      rows={6}
      hint={`${SUBMIT_SHORTCUT} 儲存`}
      onSubmit={() => undefined}
      isBusy={false}
      error={null}
      {...rest}
      label={label}
      value={value}
      onChange={setValue}
    />
  );
}

/** 留言框平常是一行，點了才展開。 */
function CollapsibleDemo() {
  const [isCollapsed, setIsCollapsed] = useState(true);
  return (
    <div>
      <EditorDemo label="留言" isLabelHidden isCollapsed={isCollapsed} rows={isCollapsed ? 1 : 4} placeholder="寫下留言…" mentionMembers={MEMBERS} />
      <Button
        size="sm"
        variant="ghost"
        onClick={() => {
          setIsCollapsed((current) => !current);
        }}
      >
        {isCollapsed ? '展開' : '收起來'}
      </Button>
    </div>
  );
}

/** 呼叫端都把編輯器放在 <form> 裡（說明、留言、專案說明）：連結的小框在裡面按 Enter 不能送出整個表單。 */
function FormDemo() {
  const [submits, setSubmits] = useState(0);
  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        setSubmits((current) => current + 1);
      }}
    >
      <EditorDemo label="在表單裡" initial="選了字按 Ctrl+K，網址欄按 Enter。" rows={2} />
      <p aria-live="polite">表單送出了 {submits} 次</p>
    </form>
  );
}

/** 所見即所得的編輯器（features/rich-text）：一般、提及、送出中、錯誤、收起來的一行、填滿畫面高度；兩種編輯方式（所見即所得、Markdown 原文）、淺色與深色各看一遍。 */
export function EditorSection() {
  return (
    <DemoSection title="編輯器">
      <Demo name="RichTextEditor">
        <p className={`${styles.note} ${styles.noteRow}`}>
          工具列最右邊的「編輯方式」切換是全站共用的偏好（記在這個瀏覽器的 editor.mode）：這一頁每個編輯器一起換。Markdown 原文模式不下載編輯器，格式按鈕拿掉只留切換，高度不變；淺色與深色都要看過。
        </p>
        <EditorDemo label="說明" initial={SAMPLE} />
      </Demo>
      <Demo name="uploads">
        <EditorDemo label="可以插入檔案" initial="工具列最後一組是「插入圖片或檔案」（展示用，選了不上傳）。" uploads={DEMO_UPLOADS} rows={2} />
      </Demo>
      <Demo name="mentionMembers">
        <EditorDemo label="留言（打 @ 提及）" initial="打 @ 試試看" mentionMembers={MEMBERS} placeholder="寫下留言…" rows={3} />
      </Demo>
      <Demo name="isBusy">
        <EditorDemo label="送出中" initial="內容不能改，焦點留得住。" isBusy rows={2} />
      </Demo>
      <Demo name="error">
        <EditorDemo label="有錯誤" initial="寫得太長了。" error="說明不能超過 50,000 個字。" rows={2} />
      </Demo>
      <Demo name="isCollapsed">
        <CollapsibleDemo />
      </Demo>
      <Demo name="form">
        <FormDemo />
      </Demo>
      <Demo name="fillsViewport">
        <div className={`${fill.fill} ${styles.editorFill}`}>
          <EditorDemo label="專案說明" fillsViewport hasAttachments={false} initial="高度固定，打字時不長高，更長的內容在框裡捲。" rows={4} />
        </div>
      </Demo>
    </DemoSection>
  );
}
