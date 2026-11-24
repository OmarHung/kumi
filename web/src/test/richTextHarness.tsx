import { useState } from 'react';
import { RichTextEditor, type RichTextEditorProps } from '../features/rich-text/RichTextEditor';

type HarnessProps = Partial<Omit<RichTextEditorProps, 'value' | 'onChange'>> & {
  readonly initial?: string;
  readonly onValue?: (value: string) => void;
};

/** 測試與展示頁用的外殼：把編輯器接到自己的狀態（和呼叫端一樣是受控的）。 */
export function EditorHarness({ initial = '', onValue, ...rest }: HarnessProps) {
  const [value, setValue] = useState(initial);
  return (
    <>
      <RichTextEditor
        label="說明"
        maxLength={1000}
        rows={3}
        hint="Ctrl+Enter 送出"
        onSubmit={() => undefined}
        isBusy={false}
        error={null}
        {...rest}
        value={value}
        onChange={(next) => {
          setValue(next);
          onValue?.(next);
        }}
      />
      <button
        type="button"
        onClick={() => {
          setValue('');
        }}
      >
        外面清空
      </button>
      <button
        type="button"
        onClick={() => {
          setValue('外面換的\n\n- 一');
        }}
      >
        外面換內容
      </button>
    </>
  );
}
