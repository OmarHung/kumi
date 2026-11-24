import type { FieldSize } from '../../components/Field/Field';

/**
 * 工具列與表格上方那一列的欄位長相（docs/DESIGN.md 6.1、第 5 節）：控制項矮一號（--control-height-sm），
 * 標籤視覺上隱藏、名稱照舊，意思由選項文字（「所有類型」）、提示文字與圖示帶出來。
 * 表單、浮層、對話框裡的欄位不用它（標籤看得到、高度是一般的）。
 */
export interface ToolbarFieldProps {
  readonly isToolbar?: boolean;
}

export function toolbarFieldProps(isToolbar: boolean | undefined): { readonly size?: FieldSize; readonly isLabelHidden?: boolean } {
  return isToolbar === true ? { size: 'sm', isLabelHidden: true } : {};
}
