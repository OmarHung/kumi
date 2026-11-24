import type { ItemTypeDraft } from '../../domain/settingsDraft';
import { Checklist, checkedNames, type ChecklistOption } from './Checklist';

interface TypeChecklistProps {
  /** 草稿裡的類型（包含剛新增的），照順序。 */
  readonly types: readonly ItemTypeDraft[];
  readonly legend: string;
  /** 勾選的類型 key。 */
  readonly value: readonly string[];
  readonly onChange: (next: readonly string[]) => void;
  /** 這一組勾選框的說明（例如取消套用後送出會被擋下的提醒）的 id。 */
  readonly describedBy?: string;
}

function typeOptions(types: readonly ItemTypeDraft[]): readonly ChecklistOption[] {
  return types.map((type) => ({ key: type.key, label: type.name.trim() === '' ? type.key : type.name }));
}

/** 欄位套用到哪些類型：一組勾選框，照草稿裡類型的順序。勾了就寫進那個類型的 fields。 */
export function TypeChecklist({ types, legend, value, onChange, describedBy }: TypeChecklistProps) {
  return <Checklist legend={legend} options={typeOptions(types)} value={value} onChange={onChange} describedBy={describedBy} />;
}

/** 唯讀時的寫法：「套用到：任務、需求」。 */
export function typeNames(types: readonly ItemTypeDraft[], keys: readonly string[]): string {
  return checkedNames(typeOptions(types), keys, '沒有套用到任何類型');
}
