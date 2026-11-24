/**
 * 編輯中的草稿值不值得留：不是空白、也不等於原文。編輯表單（useDraft）與「掛上時要不要直接開著編輯器」
 * （useCommentEntry）用同一條規則，不然等於原文的舊草稿會讓編輯器自己打開、卻沒有「已還原」的提示。
 */
export function isEditDraftWorthKeeping(original: string, value: string): boolean {
  return value.trim() !== '' && value !== original;
}
