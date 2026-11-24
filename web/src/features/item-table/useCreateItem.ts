import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState, type RefObject } from 'react';
import { createItem } from '../../api/items';
import type { Item, NewItemRequest } from '../../api/types';
import { mapApiErrorToFields, type FieldErrors, type FieldMapping } from '../../lib/formErrors';
import type { CreatedItem } from './CreatedStatus';
import type { ItemDefaults } from './createDefaults';
import { addItemToCache, type ListPosition } from './itemCache';
import { useProjectTable } from './ProjectTableContext';

export type CreateItemField = 'type' | 'parent' | 'title';

const ERROR_CODES: FieldMapping<CreateItemField>['codes'] = {
  'item.unknown_type': 'type',
  'item.parent_not_allowed': 'parent',
  'item.unknown_parent': 'parent',
  'item.title_required': 'title',
  'item.title_too_long': 'title',
};

const NO_DEFAULTS: ItemDefaults = {};

export interface CreateItemDraft {
  readonly type: string;
  readonly parentKey: string;
  readonly title: string;
}

interface CreateItemOptions {
  /** 新的項目要放進哪一份已經載入的清單（查詢鍵），以及放在最前面還是最後面。 */
  readonly listKey: readonly unknown[];
  readonly position: ListPosition;
  /** 畫面上有的欄位：後端的錯誤對得到的放在欄位旁，對不到的放在表單上方。 */
  readonly fields: readonly CreateItemField[];
  readonly titleRef: RefObject<HTMLInputElement | null>;
  /** 沿用清單篩選的負責人（createDefaults.ts 的 assigneeFromFilter）；沒有就不帶。 */
  readonly assigneeId?: string;
  /** 沿用清單篩選的優先度與到期日（createDefaults.ts 的 itemDefaultsFromFilters）；沒有就不帶。 */
  readonly defaults?: ItemDefaults;
  /** 新的項目符不符合清單目前的條件；不符合時「已新增」的訊息多說一句。沒給就當成符合（例如下層項目）。 */
  readonly matches?: (item: Item) => boolean;
}

function requestOf(draft: CreateItemDraft, title: string, assigneeId: string | undefined, defaults: ItemDefaults): NewItemRequest {
  const { fieldsFor, ...rest } = defaults;
  const fields = fieldsFor?.(draft.type) ?? {};
  return {
    type: draft.type,
    title,
    ...(draft.parentKey !== '' && { parentKey: draft.parentKey }),
    ...(assigneeId !== undefined && { assigneeId }),
    ...rest,
    ...(Object.keys(fields).length > 0 && { fields }),
  };
}

/** 快速新增（表格上方、看板、項目頁的下層項目）共用：檢查標題、送出、把新的項目放進清單、錯誤對到欄位。 */
export function useCreateItem({ listKey, position, fields, titleRef, assigneeId, defaults = NO_DEFAULTS, matches }: CreateItemOptions) {
  const { detail } = useProjectTable();
  const queryClient = useQueryClient();
  const [fieldErrors, setFieldErrors] = useState<FieldErrors<CreateItemField>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [created, setCreated] = useState<CreatedItem | null>(null);
  const mutation = useMutation({
    mutationFn: (request: NewItemRequest) => createItem(detail.project.key, request),
    onSuccess: (item) => {
      addItemToCache(queryClient, item, listKey, position);
      setCreated({ key: item.key, isOutsideConditions: matches !== undefined && !matches(item) });
      titleRef.current?.focus();
    },
    onError: (error) => {
      const mapped = mapApiErrorToFields(error, { fields, codes: ERROR_CODES });
      setFieldErrors(mapped.fieldErrors);
      setFormError(mapped.formError);
    },
  });

  function submit(draft: CreateItemDraft, onCreated: () => void) {
    setCreated(null);
    setFormError(null);
    const title = draft.title.trim();
    if (title === '') {
      setFieldErrors({ title: '請輸入標題。' });
      titleRef.current?.focus();
      return;
    }
    setFieldErrors({});
    mutation.mutate(requestOf(draft, title, assigneeId, defaults), { onSuccess: onCreated });
  }

  /** 使用者改了這個欄位：它的錯誤（「請輸入標題。」）已經不成立，拿掉。錯誤畫在輸入框裡時才不會和打的字並排。 */
  function clearFieldError(field: CreateItemField) {
    setFieldErrors((current) => (current[field] === undefined ? current : { ...current, [field]: undefined }));
  }

  return { submit, fieldErrors, formError, created, createdKey: created?.key ?? null, isPending: mutation.isPending, clearFieldError };
}
