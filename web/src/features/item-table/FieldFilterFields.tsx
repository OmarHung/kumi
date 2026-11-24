import { useCurrentUser } from '../../auth/CurrentUser';
import type { FieldDefinition } from '../../api/blueprint';
import type { Member } from '../../api/types';
import { SelectField } from '../../components/Field/Field';
import type { SelectOption } from '../../components/Select/Select';
import { FIELD_NONE, filterableFields, type FieldFilters } from '../../domain/fieldFilters';
import { EveryoneMark, NobodyMark, PersonMark, personOption } from '../work-items/selectOptionMarks';
import { useProjectTable } from './ProjectTableContext';
import { toolbarFieldProps, type ToolbarFieldProps } from './toolbarField';

interface FieldFilterContext {
  readonly members: readonly Member[];
  readonly me: { readonly personId: string; readonly displayName: string };
}

/**
 * 一個欄位的篩選選項。第一個一律是「所有<欄位名>」（不篩選，是真的選項，規範 7.3）。
 * 單選與多選：沒有填、再列各個選項；人員：我、沒有填、成員（和負責人篩選同一個長相）；勾選：已勾選、沒勾選。
 */
export function fieldFilterOptions(definition: FieldDefinition, { members, me }: FieldFilterContext): SelectOption[] {
  const all = { value: '', label: `所有${definition.name}` };
  switch (definition.type) {
    case 'single_select':
    case 'multi_select':
      return [all, { value: FIELD_NONE, label: '沒有填' }, ...(definition.options ?? []).map((option) => ({ value: option.key, label: option.name }))];
    case 'person':
      return [
        { ...all, leading: <EveryoneMark /> },
        { value: 'me', label: '我', leading: <PersonMark name={me.displayName} /> },
        { value: FIELD_NONE, label: '沒有填', leading: <NobodyMark /> },
        ...members.filter((member) => member.personId !== me.personId).map((member, index) => personOption(member, index === 0)),
      ];
    case 'checkbox':
      return [all, { value: 'true', label: '已勾選' }, { value: 'false', label: '沒勾選' }];
    default:
      return [all];
  }
}

// 網址或檢視指到已經不在的選項、或自己的人員編號：選單上要看得到，也選得回「所有…」。
// 人員不在成員清單裡時（離開專案、手打的編號）不顯示整串編號。
function withCurrent(
  options: SelectOption[],
  value: string,
  definition: FieldDefinition,
  { members, me }: FieldFilterContext,
): { options: SelectOption[]; value: string } {
  const current = value === me.personId ? 'me' : value;
  if (current === '' || options.some((option) => option.value === current)) return { options, value: current };
  const memberName = members.find((member) => member.personId === current)?.displayName;
  const label = definition.type === 'person' ? (memberName ?? '已不在的人員') : current;
  return { options: [...options, { value: current, label }], value: current };
}

interface FieldFilterFieldsProps extends ToolbarFieldProps {
  readonly fields: FieldFilters;
  readonly onChange: (fields: FieldFilters) => void;
}

/** 專案裡每個能篩的自訂欄位一個下拉，照藍圖的順序（「更多條件」與窄螢幕的「篩選」裡）。選了就換。 */
export function FieldFilterFields({ fields, onChange, isToolbar }: FieldFilterFieldsProps) {
  const { detail } = useProjectTable();
  const me = useCurrentUser();
  const context = { members: detail.members, me };
  return (
    <>
      {filterableFields(detail.blueprint).map((definition) => {
        const { options, value } = withCurrent(fieldFilterOptions(definition, context), fields[definition.key] ?? '', definition, context);
        return (
          <SelectField
            key={definition.key}
            {...toolbarFieldProps(isToolbar)}
            label={definition.name}
            value={value}
            options={options}
            onChange={(next) => {
              const rest = Object.fromEntries(Object.entries(fields).filter(([key]) => key !== definition.key));
              onChange(next === '' ? rest : { ...rest, [definition.key]: next });
            }}
          />
        );
      })}
    </>
  );
}
