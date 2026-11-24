import type { SavedView } from '../../api/types';
import { SaveViewDialog } from './SaveViewDialog';
import { DeleteViewDialog, UpdateFailedDialog } from './ViewConfirmDialogs';
import type { SavedViewMutations } from './useSavedViewMutations';

/** 開著的對話框：另存、重新命名、刪除前確認、更新失敗之後的「再試一次」。 */
export type OpenDialog =
  | { readonly kind: 'save' }
  | { readonly kind: 'rename'; readonly view: SavedView }
  | { readonly kind: 'delete'; readonly view: SavedView }
  | { readonly kind: 'update'; readonly view: SavedView; readonly message: string };

interface ViewDialogsProps {
  readonly dialog: OpenDialog | null;
  readonly mutations: SavedViewMutations;
  /** 另存時名稱的預設。 */
  readonly suggestedName: string;
  readonly onSave: (name: string) => void;
  readonly onRename: (view: SavedView, name: string) => void;
  readonly onDelete: (view: SavedView) => void;
  readonly onRetryUpdate: (view: SavedView) => void;
  readonly onClose: () => void;
}

/** 檢視選單開出來的對話框（沒有開著的時候不畫）。送出的結果與關上之後焦點回到哪裡，由呼叫端與 Dialog 處理。 */
export function ViewDialogs({ dialog, mutations, suggestedName, onSave, onRename, onDelete, onRetryUpdate, onClose }: ViewDialogsProps) {
  switch (dialog?.kind) {
    case undefined:
      return null;
    case 'save':
      return (
        <SaveViewDialog
          title="另存成檢視"
          description="存起來的是目前的版面與條件，只有你看得到；下次一點就套用。"
          submitLabel="儲存"
          pendingLabel="儲存中…"
          initialName={suggestedName}
          isPending={mutations.create.isPending}
          error={mutations.create.error}
          onEdit={mutations.create.reset}
          onSubmit={onSave}
          onClose={onClose}
        />
      );
    case 'rename':
      return (
        <SaveViewDialog
          title="重新命名檢視"
          description="只改名稱，版面與條件照舊。"
          submitLabel="儲存"
          pendingLabel="儲存中…"
          initialName={dialog.view.name}
          isPending={mutations.replace.isPending}
          error={mutations.replace.error}
          onEdit={mutations.replace.reset}
          onSubmit={(name) => {
            onRename(dialog.view, name);
          }}
          onClose={onClose}
        />
      );
    case 'delete':
      return <DeleteViewDialog view={dialog.view} mutations={mutations} onDelete={onDelete} onClose={onClose} />;
    case 'update':
      return <UpdateFailedDialog view={dialog.view} message={dialog.message} isPending={mutations.replace.isPending} onRetry={onRetryUpdate} onClose={onClose} />;
  }
}
