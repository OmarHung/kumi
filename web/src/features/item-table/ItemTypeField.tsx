import type { ItemTypeDefinition } from '../../api/blueprint';
import { SelectField } from '../../components/Field/Field';
import { toolbarFieldProps, type ToolbarFieldProps } from './toolbarField';

interface ItemTypeFieldProps extends ToolbarFieldProps {
  /** 可以選的類型：表格是全部，下層項目是可以放在這個項目底下的，看板是用這個流程的。 */
  readonly types: readonly ItemTypeDefinition[];
  readonly value: string;
  readonly error: string | undefined;
  readonly onChange: (type: string) => void;
}

/** 快速新增的類型選單（表格上方、下層項目、看板的第一欄）。 */
export function ItemTypeField({ types, value, error, onChange, isToolbar }: ItemTypeFieldProps) {
  const options = types.map((itemType) => ({ value: itemType.key, label: itemType.name }));
  return <SelectField {...toolbarFieldProps(isToolbar)} label="類型" value={value} error={error} options={options} onChange={onChange} />;
}
