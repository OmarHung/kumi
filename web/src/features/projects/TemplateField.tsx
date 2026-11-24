import type { UseQueryResult } from '@tanstack/react-query';
import { useId, type Ref } from 'react';
import type { Template } from '../../api/types';
import { SelectField } from '../../components/Field/Field';
import { ErrorState } from '../../components/StatusViews/StatusViews';
import styles from './TemplateField.module.css';
import { TemplatePreview } from './TemplatePreview';

interface TemplateFieldProps {
  readonly ref: Ref<HTMLButtonElement>;
  readonly templates: UseQueryResult<readonly Template[]>;
  readonly value: string;
  readonly error: string | undefined;
  readonly onChange: (templateKey: string) => void;
}

/** 選模板，下面接著預覽。模板清單載入中（停用、灰字「載入中…」）、載入失敗也在這裡處理，對話框其他欄位照樣能填。 */
export function TemplateField({ ref, templates, value, error, onChange }: TemplateFieldProps) {
  const previewId = useId();
  if (templates.isError) {
    return (
      <ErrorState
        title="模板載入失敗"
        error={templates.error}
        onRetry={() => {
          void templates.refetch();
        }}
      />
    );
  }
  const selected = templates.data?.find((template) => template.key === value);
  return (
    <div className={styles.field}>
      <SelectField
        ref={ref}
        label="模板"
        value={value}
        placeholder={templates.isPending ? '載入中…' : undefined}
        options={(templates.data ?? []).map((template) => ({ value: template.key, label: template.name }))}
        disabled={templates.isPending}
        hint={selected?.description ?? undefined}
        error={error}
        isDescribedByAfterHint
        aria-describedby={selected === undefined ? undefined : previewId}
        onChange={onChange}
      />
      {selected !== undefined && <TemplatePreview id={previewId} template={selected} />}
    </div>
  );
}
