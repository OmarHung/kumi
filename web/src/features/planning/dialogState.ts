import { useState } from 'react';

/** 一節裡同時只會開一個對話框：新增、編輯某一列、刪除某一列。 */
export type RowDialog<T> =
  | { readonly kind: 'create' }
  | { readonly kind: 'edit'; readonly row: T }
  | { readonly kind: 'delete'; readonly row: T };

export function useRowDialog<T>() {
  const [dialog, setDialog] = useState<RowDialog<T> | null>(null);
  return {
    dialog,
    openCreate: () => {
      setDialog({ kind: 'create' });
    },
    openEdit: (row: T) => {
      setDialog({ kind: 'edit', row });
    },
    openDelete: (row: T) => {
      setDialog({ kind: 'delete', row });
    },
    close: () => {
      setDialog(null);
    },
  };
}
