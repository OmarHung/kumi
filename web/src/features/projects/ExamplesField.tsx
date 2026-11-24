import { useState } from 'react';
import type { Template } from '../../api/types';
import { CheckboxField } from '../../components/CheckboxField/CheckboxField';

/** 「附上範例項目」的勾選狀態。換到沒有範例的模板時勾選框消失，送出的值不能還記著之前的勾選。 */
export function useExamplesChoice(selected: Template | undefined) {
  const [isChecked, setIsChecked] = useState(false);
  const exampleCount = selected?.exampleCount ?? 0;
  return { exampleCount, isChecked, includeExamples: isChecked && exampleCount > 0, setIsChecked };
}

interface ExamplesFieldProps {
  readonly exampleCount: number;
  readonly isChecked: boolean;
  readonly onChange: (isChecked: boolean) => void;
}

/** 模板有範例（exampleCount > 0）才出現。 */
export function ExamplesField({ exampleCount, isChecked, onChange }: ExamplesFieldProps) {
  if (exampleCount === 0) {
    return null;
  }
  return (
    <CheckboxField
      label={`附上範例項目（${String(exampleCount)} 筆）`}
      hint="放幾筆示範用的項目，第一筆說明這個專案怎麼用；之後可以逐筆刪掉。"
      checked={isChecked}
      onChange={(event) => {
        onChange(event.target.checked);
      }}
    />
  );
}
