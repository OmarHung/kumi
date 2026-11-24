import type { ReactNode } from 'react';
import { FieldShell } from '../Field/Field';
import { DatePicker, type DatePickerProps } from './DatePicker';

export type DateFieldProps = Omit<DatePickerProps, 'id' | 'aria-label' | 'aria-labelledby' | 'aria-invalid'> & {
  readonly label: string;
  /** 欄位下方的說明。 */
  readonly hint?: ReactNode;
  /** 有值時欄位標成無效，訊息透過 aria-describedby 和輸入框連在一起。 */
  readonly error?: string;
  /** 標籤只給報讀軟體（工具列那一列），名稱照舊是 label。 */
  readonly isLabelHidden?: boolean;
  /** 輸入框的 id；沒給時自己產生。 */
  readonly id?: string;
  /** 加在欄位的外框上（和 TextField、SelectField 一樣），不是輸入框。 */
  readonly className?: string;
};

/**
 * 表單裡的日期欄位：看得到的標籤加 DatePicker（像 SelectField 之於 Select，docs/DESIGN.md 第 7 節）。標籤指到輸入框
 * （點標籤等於點輸入框：打開日曆），說明與錯誤用 aria-describedby 連過去。表單的日期一律用它，不用 TextField 的 type="date"。
 */
export function DateField({ label, hint, error, isLabelHidden = false, id, className, ...pickerProps }: DateFieldProps) {
  return (
    <FieldShell label={label} hint={hint} error={error} isLabelHidden={isLabelHidden} id={id} describedBy={pickerProps['aria-describedby']} className={className}>
      {(control) => <DatePicker {...pickerProps} id={control.id} aria-invalid={error ? true : undefined} aria-describedby={control.describedBy} />}
    </FieldShell>
  );
}
