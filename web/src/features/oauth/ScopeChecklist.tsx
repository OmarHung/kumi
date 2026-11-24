import type { AccessTokenScope } from '../../api/types';
import { CheckboxField, CheckboxGroup } from '../../components/CheckboxField/CheckboxField';

interface ScopeChecklistProps {
  readonly legend: string;
  readonly options: readonly { readonly scope: AccessTokenScope; readonly label: string; readonly hint?: string }[];
  readonly selected: readonly AccessTokenScope[];
  readonly disabled: boolean;
  readonly onChange: (next: readonly AccessTokenScope[]) => void;
}

/** 範圍的勾選（OAuth 與 kumi 的瀏覽器登入共用）：勾與取消都交給呼叫端存，這裡不留狀態。 */
export function ScopeChecklist({ legend, options, selected, disabled, onChange }: ScopeChecklistProps) {
  return (
    <CheckboxGroup legend={legend} layout="column">
      {options.map(({ scope, label, hint }) => (
        <CheckboxField
          key={scope}
          label={label}
          hint={hint}
          checked={selected.includes(scope)}
          disabled={disabled}
          onChange={(event) => {
            const { checked } = event.currentTarget;
            onChange(checked ? [...selected, scope] : selected.filter((value) => value !== scope));
          }}
        />
      ))}
    </CheckboxGroup>
  );
}
