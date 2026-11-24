import { Badge, type BadgeTone } from '../../components/Badge/Badge';
import { Icon } from '../../components/Icon/Icon';
import type { DraftStatus } from '../../domain/settingsStatus';
import type { IssueBinding } from './SettingsContext';
import styles from './Settings.module.css';

export type RowStatus = DraftStatus | 'removed';

const STATUS_BADGES: Readonly<Record<Exclude<RowStatus, 'unchanged'>, { readonly label: string; readonly tone: BadgeTone }>> = {
  added: { label: '新增', tone: 'accent' },
  modified: { label: '已修改', tone: 'warning' },
  moved: { label: '已移動', tone: 'neutral' },
  removed: { label: '要刪除', tone: 'danger' },
};

/**
 * 每一列的淡底：還沒送出的修改一眼看得出來；意思另外寫在標籤的文字裡。
 * 新增的列不加底：強調色的淡底和「已選取」一樣，只靠「新增」的標籤。
 */
export const ROW_CLASSES: Readonly<Record<RowStatus, string | undefined>> = {
  unchanged: undefined,
  added: undefined,
  modified: styles.rowModified,
  moved: styles.rowMoved,
  removed: styles.rowRemoved,
};

export function StatusBadge({ status }: { readonly status: RowStatus }) {
  if (status === 'unchanged') {
    return null;
  }
  const badge = STATUS_BADGES[status];
  return <Badge tone={badge.tone}>{badge.label}</Badge>;
}

/** 一個問題：錯誤的圖示加文字（不只靠紅色，也不用「!」這種文字符號當圖示）。圖示 aria-hidden，說明只有文字。 */
export function IssueText({ children }: { readonly children: string }) {
  return (
    <p className={styles.issue}>
      <Icon name="error" className={styles.issueIcon} />
      {children}
    </p>
  );
}

/** 這一列的問題：前端的檢查與伺服器的說明。id 給輸入框的 aria-describedby。 */
export function IssueMessages({ binding }: { readonly binding: IssueBinding }) {
  if (!binding.isInvalid) {
    return null;
  }
  return (
    <div id={binding.errorId} className={styles.issues}>
      {binding.messages.map((message, index) => (
        // 同一列的問題照伺服器給的順序，不會重排。
        <IssueText key={index}>{message}</IssueText>
      ))}
    </div>
  );
}
