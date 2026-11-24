import { useState } from 'react';
import { useDeleteDescriptionTemplate, useUpdateDescriptionTemplate } from '../../api/descriptionTemplates';
import { errorMessage } from '../../api/http';
import type { DescriptionTemplate } from '../../api/types';

interface Feedback {
  readonly clear: () => void;
  readonly succeed: (text: string) => void;
  readonly fail: (text: string) => void;
}

/** 設成預設：直接送出，不必確認（可以再換回去）；失敗寫在頁面上方的 alert。 */
function useSetDefault({ clear, succeed, fail }: Feedback) {
  const update = useUpdateDescriptionTemplate();
  function setDefault(template: DescriptionTemplate) {
    if (update.isPending) return;
    clear();
    update.mutate(
      { id: template.id, input: { name: template.name, body: template.body, isDefault: true } },
      {
        onSuccess: () => { succeed(`已把「${template.name}」設成預設。`); },
        onError: (error) => { fail(`沒有把「${template.name}」設成預設：${errorMessage(error)}`); },
      },
    );
  }
  return { setDefault, isPending: update.isPending };
}

/** 刪除：先確認；失敗寫在確認對話框裡（對話框留著），成功後關上、寫結果、要求把焦點放到提示（按的那一列已經不見了）。 */
function useDeleteTemplate({ clear, succeed }: Feedback, onDeleted: () => void) {
  const remove = useDeleteDescriptionTemplate();
  const [deleting, setDeleting] = useState<DescriptionTemplate | null>(null);
  return {
    deleting,
    isDeleting: remove.isPending,
    deleteError: remove.isError ? errorMessage(remove.error) : null,
    askDelete: (template: DescriptionTemplate) => {
      clear();
      remove.reset();
      setDeleting(template);
    },
    cancelDelete: () => { setDeleting(null); },
    confirmDelete: (template: DescriptionTemplate) => {
      remove.mutate(template.id, {
        onSuccess: () => {
          setDeleting(null);
          succeed(`已刪除「${template.name}」。`);
          onDeleted();
        },
      });
    },
  };
}

/**
 * 清單上的動作。結果寫在頁面上方的提示（成功是 status，設成預設失敗是 alert）；
 * 開始下一個動作時先清掉上一個結果，留著的話看起來像這一次的。focusRequest 每次刪除成功加一，頁面據此把焦點放到提示。
 */
export function useTemplateAdmin() {
  const [message, setMessage] = useState('');
  const [failure, setFailure] = useState<string | null>(null);
  const [focusRequest, setFocusRequest] = useState(0);
  const feedback: Feedback = {
    clear: () => {
      setMessage('');
      setFailure(null);
    },
    succeed: setMessage,
    fail: setFailure,
  };
  const set = useSetDefault(feedback);
  const del = useDeleteTemplate(feedback, () => { setFocusRequest((count) => count + 1); });
  return {
    message,
    failure,
    focusRequest,
    isBusy: set.isPending || del.isDeleting,
    deleting: del.deleting,
    isDeleting: del.isDeleting,
    deleteError: del.deleteError,
    handlers: { onSetDefault: set.setDefault, onDelete: del.askDelete },
    confirmDelete: del.confirmDelete,
    cancelDelete: del.cancelDelete,
  };
}

export type TemplateAdmin = ReturnType<typeof useTemplateAdmin>;
