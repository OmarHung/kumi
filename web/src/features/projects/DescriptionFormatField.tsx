import { useState, type Ref } from 'react';
import { useDescriptionTemplates } from '../../api/projectDescription';
import { SelectField } from '../../components/Field/Field';

const NO_FORMAT = '__none__';

/**
 * 「專案說明」下拉的狀態：預先選預設的那一份（isDefault）；使用者選過就照他選的；選「不套用」或讀不到時 description 是 undefined（不送）。
 * 值從清單推出來，不必等載入完再用 effect 補值。
 */
export function useDescriptionFormat() {
  const templates = useDescriptionTemplates();
  const [choice, setChoice] = useState<string | null>(null);
  const list = templates.data ?? [];
  const value = choice ?? list.find((template) => template.isDefault)?.id ?? NO_FORMAT;
  const body = list.find((template) => template.id === value)?.body;
  return { templates, value, setChoice, description: body === undefined || body.trim() === '' ? undefined : body };
}

export type DescriptionFormat = ReturnType<typeof useDescriptionFormat>;

interface DescriptionFormatFieldProps {
  readonly ref: Ref<HTMLButtonElement>;
  readonly format: DescriptionFormat;
  readonly error: string | undefined;
  /** 選了別的格式之後（例如清掉上一次送出的錯誤）。 */
  readonly onChosen: () => void;
}

/**
 * 開新專案時預先填進「專案說明」的格式。讀不到時只剩「不套用」並寫一行說明，不擋開專案；載入中先停用、不先顯示「不套用」再跳成預設。
 */
export function DescriptionFormatField({ ref, format, error, onChosen }: DescriptionFormatFieldProps) {
  const { templates, value } = format;
  const list = templates.data ?? [];
  const none = { value: NO_FORMAT, label: '不套用', startsGroup: list.length > 0 };
  return (
    <SelectField
      ref={ref}
      label="專案說明"
      value={templates.isPending ? '' : value}
      placeholder={templates.isPending ? '載入中…' : undefined}
      options={templates.isPending ? [] : [...list.map((template) => ({ value: template.id, label: template.name })), none]}
      disabled={templates.isPending}
      hint={templates.isError ? '說明格式讀不到' : '建立時把這個格式的內容寫進專案說明，之後可以在專案的「資訊」改。'}
      error={error}
      onChange={(next) => {
        format.setChoice(next);
        onChosen();
      }}
    />
  );
}
