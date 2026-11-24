import { useId, useRef, type ComponentProps } from 'react';
import { Button } from '../../components/Button/Button';
import { IconButton } from '../../components/IconButton/IconButton';
import { Notice } from '../../components/Notice/Notice';
import type { EditorUploads } from './useEditorUploads';
import { UPLOADING_REASON } from './useEditorUploads';
import styles from './EditorUploadControls.module.css';

/** 放進編輯器的「格式」工具列時：一個停駐點的 tabIndex 與 onFocus（左右鍵、Home、End 移得到，data-toolbar-item）。 */
export interface ToolbarItemProps {
  readonly tabIndex: number;
  readonly onFocus: () => void;
}

interface InsertFileButtonProps {
  readonly uploads: EditorUploads;
  /** 送出中、儲存中：停住（aria-disabled，焦點不掉），按了沒有反應。 */
  readonly isBusy?: boolean;
  /** 所見即所得的工具列（role="toolbar"）裡的一個按鈕；Markdown 原文那一列沒有工具列，不給。 */
  readonly toolbarItem?: ToolbarItemProps;
}

/**
 * 「插入圖片或檔案」：貼上與拖曳不是每個人都用得到，這顆一直找得到。可以多選；選好的檔案和貼上的走同一條路。
 * 在編輯器上方那一列（docs/design/08-interaction.md 8.3「貼圖與附加檔案」），由編輯器依 uploads.canUpload 決定放不放
 * （沒有 item.update 或專案封存時不放）。按下去不搶走編輯器的焦點與游標（和格式按鈕一樣）：檔案插在游標的位置，
 * 留言框也不會因為焦點離開而收起來。
 */
export function InsertFileButton({ uploads, isBusy = false, toolbarItem }: InsertFileButtonProps) {
  const inputRef = useRef<HTMLInputElement>(null);
  return (
    <span className={styles.insert}>
      <IconButton
        icon="fileImage"
        size="sm"
        label="插入圖片或檔案"
        data-toolbar-item={toolbarItem === undefined ? undefined : ''}
        tabIndex={toolbarItem?.tabIndex}
        onFocus={toolbarItem?.onFocus}
        aria-disabled={isBusy || undefined}
        onMouseDown={(event) => {
          event.preventDefault();
        }}
        onClick={() => {
          if (!isBusy) inputRef.current?.click();
        }}
      />
      <input
        ref={inputRef}
        type="file"
        multiple
        hidden
        aria-label="選擇要插入的圖片或檔案"
        onChange={(event) => {
          uploads.addFiles(Array.from(event.target.files ?? []));
          event.target.value = '';
        }}
      />
    </span>
  );
}

interface UploadAwareSubmitProps extends ComponentProps<typeof Button> {
  readonly isUploading: boolean;
}

/**
 * 送出、儲存的按鈕：還在上傳時停住並寫出原因（「還有檔案在上傳」，aria-describedby 連到按鈕）。
 * 用 aria-disabled 不用 disabled（docs/design 7 節 Button）：焦點在按鈕上時不會掉，點了由呼叫端擋下。
 * 其他的停用（空白、送出中）照呼叫端給的 disabled。
 */
export function UploadAwareSubmit({ isUploading, children, ...rest }: UploadAwareSubmitProps) {
  const reasonId = useId();
  const reason = isUploading && (
    <span id={reasonId} className={styles.reason}>
      {UPLOADING_REASON}
    </span>
  );
  return (
    <>
      {reason}
      <Button type="submit" variant="primary" size="sm" aria-disabled={isUploading || undefined} aria-describedby={isUploading ? reasonId : undefined} {...rest}>
        {children}
      </Button>
    </>
  );
}

/**
 * 文字框下面：沒傳成功或沒有送出的原因（可以關掉），以及即時區域，完成與失敗用說的。
 * 即時區域用到上傳才掛（hasAnnouncer）、先掛空的再填字（報讀軟體才唸得到）；提示本身不再唸（role="none"），不然同一句話唸兩次。
 */
export function EditorUploadNotes({ uploads }: { readonly uploads: EditorUploads }) {
  const { problems } = uploads;
  return (
    <>
      {uploads.hasAnnouncer && (
        <p role="status" className="visually-hidden">
          {uploads.announcement}
        </p>
      )}
      {problems.length > 0 && (
        <Notice tone="warning" role="none" dismissLabel="知道了（檔案的問題）" onDismiss={uploads.dismissProblems}>
          {problems.length === 1 ? (
            problems[0]
          ) : (
            <ul className={styles.problems}>
              {problems.map((problem, index) => (
                // 同名的檔案可能不只一個；順序固定，用位置當 key。
                <li key={index}>{problem}</li>
              ))}
            </ul>
          )}
        </Notice>
      )}
    </>
  );
}
