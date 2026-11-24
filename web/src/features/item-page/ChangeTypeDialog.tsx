import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useId, useState, type SubmitEvent } from 'react';
import { errorMessage } from '../../api/http';
import { changeItemType } from '../../api/items';
import { useItem } from '../../api/queries';
import type { ItemDetail } from '../../api/types';
import { Dialog, DialogFormActions } from '../../components/Dialog/Dialog';
import { SelectField } from '../../components/Field/Field';
import { FormAlert } from '../../components/FormAlert/FormAlert';
import { itemTypeName } from '../../domain/blueprint';
import { previewTypeChange, type TypeChangeKnowledge } from '../../domain/itemTypeChange';
import { replaceItemInCache } from '../item-table/itemCache';
import { useProjectTable } from '../item-table/ProjectTableContext';
import { blockedReasonIds, ChangeTypePreview } from './ChangeTypePreview';
import { ChildLinks } from './ChildLinks';
import { childKeysOf, useKnownChildren } from './knownChildren';
import styles from './ItemDialogs.module.css';

/** 上層的類型（對話框打開時才讀上層）與已經載入的下層（可能只有第一頁）。 */
function useKnowledge(item: ItemDetail): TypeChangeKnowledge {
  const { detail } = useProjectTable();
  const parent = useItem(item.parentKey ?? '', item.parentKey !== null);
  const { children, hasMore } = useKnownChildren(item, detail.blueprint);
  return { parentType: item.parentKey === null ? null : parent.data?.type, children, hasMoreChildren: hasMore };
}

/** 成功時和其他修改一樣換進每一份快取（清單、看板的欄、項目頁），活動重抓。 */
function useChangeType(item: ItemDetail) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (type: string) => changeItemType(item.key, { type }),
    onSuccess: (updated) => {
      replaceItemInCache(queryClient, updated);
    },
  });
}

interface ServerProblemProps {
  readonly item: ItemDetail;
  readonly error: unknown;
  readonly known: TypeChangeKnowledge['children'];
}

/** 伺服器擋下的原因；details 裡有下層的路徑（不管代碼是什麼）就列出是哪些（已經載入的帶標題）。 */
function ServerProblem({ item, error, known }: ServerProblemProps) {
  const keys = childKeysOf(error);
  return (
    <>
      <FormAlert message={errorMessage(error)} />
      {keys.length > 0 && <ChildLinks projectKey={item.projectKey} keys={keys} known={known} />}
    </>
  );
}

/**
 * 改類型（頁列「更多動作」的「改類型」，有 item.update、而且專案有兩種以上的類型才有）：選新類型（其他的類型），先說明會發生什麼；前端看得出來會被擋下的（上層、已經載入的下層）不能送出：
 * 送出鈕停用但停得到，原因用 aria-describedby 連過去。
 * 狀態由伺服器決定，這裡照同一套規則先算給使用者看；最後以伺服器為準，擋下的原因留在對話框裡。
 */
export function ChangeTypeDialog({ item, onClose }: { readonly item: ItemDetail; readonly onClose: () => void }) {
  const { detail, can } = useProjectTable();
  const previewId = useId();
  const [type, setType] = useState('');
  const knowledge = useKnowledge(item);
  const mutation = useChangeType(item);
  const context = { blueprint: detail.blueprint, members: detail.members, canTransition: can.transition };
  const preview = type === '' ? null : previewTypeChange(context, item, type, knowledge);
  const reasonIds = preview === null ? [] : blockedReasonIds(previewId, preview);
  const isBlocked = reasonIds.length > 0;
  const options = detail.blueprint.itemTypes
    .filter((candidate) => candidate.key !== item.type)
    .map((candidate) => ({ value: candidate.key, label: candidate.name }));

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    if (type === '' || isBlocked) return;
    mutation.mutate(type, { onSuccess: onClose });
  }

  const description = `目前是「${itemTypeName(detail.blueprint, item.type)}」。狀態由系統照新類型的流程決定，之後要往前走，照一般的方式改狀態。`;
  return (
    <Dialog title={`改 ${item.key} 的類型`} description={description} onClose={mutation.isPending ? () => undefined : onClose}>
      <form className={styles.body} noValidate onSubmit={handleSubmit}>
        {/* 還沒選是 placeholder，不是一個選項（規範 7.3）：選了只填好表單，和「改類型」一起送出。 */}
        <SelectField
          label="新的類型"
          value={type}
          placeholder="選擇類型"
          options={options}
          aria-describedby={previewId}
          onChange={(next) => {
            mutation.reset();
            setType(next);
          }}
        />
        <ChangeTypePreview item={item} preview={preview} id={previewId} />
        {mutation.isError && <ServerProblem item={item} error={mutation.error} known={knowledge.children} />}
        <DialogFormActions
          submitLabel="改類型"
          pendingLabel="改類型中…"
          isPending={mutation.isPending}
          canSubmit={type !== ''}
          blockedReasonIds={reasonIds}
          onCancel={onClose}
        />
      </form>
    </Dialog>
  );
}
