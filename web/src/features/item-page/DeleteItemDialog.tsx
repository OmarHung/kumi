import { errorMessage } from '../../api/http';
import type { ItemDetail } from '../../api/types';
import { Button } from '../../components/Button/Button';
import { Dialog, DialogActions } from '../../components/Dialog/Dialog';
import { useProjectTable } from '../item-table/ProjectTableContext';
import { ChildLinks } from './ChildLinks';
import { childKeysOf, useKnownChildren } from './knownChildren';
import { isHasChildren } from './useDeleteItem';
import styles from './ItemDialogs.module.css';

const HOW_TO = '要先刪除它們，或把它們的上層項目換掉，才能刪除這個項目。';

interface DialogProps {
  readonly item: ItemDetail;
  readonly onClose: () => void;
}

interface BlockedProps extends DialogProps {
  readonly keys: readonly string[];
  /** 伺服器擋下的（409），用 alert 唸出來。 */
  readonly fromServer: boolean;
}

/** 還有下層：說明怎麼處理、列出是哪些，沒有刪除的按鈕。下層只載入了第一頁時，數量說「至少」。 */
function BlockedDialog({ item, onClose, keys, fromServer }: BlockedProps) {
  const { detail } = useProjectTable();
  const { children, hasMore } = useKnownChildren(item, detail.blueprint);
  const listed = keys.length > 0 ? keys : children.map((child) => child.key);
  const count = `${hasMore ? '至少' : ''} ${String(listed.length)}`;
  return (
    <Dialog title={`刪除 ${item.key}？`} onClose={onClose}>
      <div className={styles.body}>
        {fromServer ? (
          <p role="alert" className={styles.warning}>{`還有下層項目：${HOW_TO}`}</p>
        ) : (
          <p className={styles.text}>{`還有${count} 個下層項目：${HOW_TO}`}</p>
        )}
        <ChildLinks projectKey={item.projectKey} keys={listed} known={children} />
      </div>
      <DialogActions>
        <Button onClick={onClose}>關閉</Button>
      </DialogActions>
    </Dialog>
  );
}

/** 別的原因沒刪成（例如權限剛被拿掉）：對話框裡說伺服器的訊息，關掉就回到原本的地方。 */
function FailedDialog({ item, onClose, error }: DialogProps & { readonly error: unknown }) {
  return (
    <Dialog title={`沒有刪除 ${item.key}`} onClose={onClose}>
      <p role="alert" className={styles.warning}>
        {errorMessage(error)}
      </p>
      <DialogActions>
        <Button onClick={onClose}>關閉</Button>
      </DialogActions>
    </Dialog>
  );
}

interface DeleteItemDialogProps extends DialogProps {
  /** 項目頁已經讀到下層了：不必問伺服器，直接說明。 */
  readonly hasKnownChildren: boolean;
  /** 剛才直接送出的刪除失敗了（useDeleteItem 的 error）。 */
  readonly error: unknown;
}

/**
 * 刪除沒有確認框（可以復原：成功後離開、提示上有「復原」，docs/DESIGN.md 8.3「危險的操作」）。
 * 只有擋下的時候才有對話框：項目頁已經知道有下層時先說明要先處理下層；伺服器擋下（別人剛加了下層）時列出伺服器說的那些；
 * 其他失敗（例如權限剛被拿掉）說伺服器的訊息。
 */
export function DeleteItemDialog({ item, onClose, hasKnownChildren, error }: DeleteItemDialogProps) {
  const serverKeys = isHasChildren(error) ? childKeysOf(error) : [];
  if (serverKeys.length > 0 || hasKnownChildren) {
    return <BlockedDialog item={item} onClose={onClose} keys={serverKeys.length > 0 ? serverKeys : []} fromServer={serverKeys.length > 0} />;
  }
  return error === null || error === undefined ? null : <FailedDialog item={item} onClose={onClose} error={error} />;
}
