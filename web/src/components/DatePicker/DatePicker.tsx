import { useCallback, useId, useRef, type FocusEvent, type KeyboardEventHandler, type Ref } from 'react';
import { cx } from '../../lib/cx';
import { fieldControlClassName } from '../Field/Field';
import { Icon } from '../Icon/Icon';
import { DateCalendar } from './DateCalendar';
import { useDatePicker, type DatePickerDismissReason, type DatePickerState } from './useDatePicker';
import styles from './DatePicker.module.css';

export type { DatePickerDismissReason } from './useDatePicker';

export interface DatePickerProps {
  /** 'YYYY-MM-DD' 或 ''（沒有值）；打了解析不了的字時是那串字（呼叫端照 onChange 給回來的值）。 */
  readonly value: string;
  /**
   * 值變了：從日曆選了日期、「今天」、「清除」（''）、打字打成解析得了的日期（ISO）。打了解析不了的字時傳原始文字
   * （呼叫端自己判斷不合法，例如格子顯示錯誤），不默默吃掉。和目前的值一樣時不叫。
   */
  readonly onChange: (value: string) => void;
  /** 從日曆明確選了（點日期、日曆裡 Enter／空白鍵、「今天」、「清除」），在 onChange 之後：格子用它「選了就存」。打字不算。 */
  readonly onPick?: (value: string) => void;
  /** 沒選就關上日曆：Esc、點外面（焦點移到外面也算）、Tab、輸入框被捲出畫面（scrolled）。Tab 是在按鍵事件裡呼叫，焦點還沒移走。 */
  readonly onDismiss?: (reason: DatePickerDismissReason) => void;
  /** 掛上就打開日曆（格子用滑鼠進入編輯時）。不搶焦點。 */
  readonly isOpenInitially?: boolean;
  /** md：表單（--control-height）；sm：工具列與表格列上（--control-height-sm）。 */
  readonly size?: 'md' | 'sm';
  /** bare：和 Select 的 bare 一樣，沒有外框、底色與焦點光暈，撐滿外層——就地編輯的格子裡用，框由格子畫。 */
  readonly variant?: 'default' | 'bare';
  readonly id?: string;
  readonly disabled?: boolean;
  /** 唯讀：停得到、看得到值，打不開日曆、不能打字。 */
  readonly readOnly?: boolean;
  readonly 'aria-label'?: string;
  readonly 'aria-labelledby'?: string;
  readonly 'aria-describedby'?: string;
  readonly 'aria-invalid'?: boolean;
  /** 送出中：打得開，但選了不收新的值（和 Select 一樣）。 */
  readonly 'aria-busy'?: boolean;
  /** 焦點離開整個元件（輸入框與日曆）才叫；在兩者之間移動不算。 */
  readonly onBlur?: (event: FocusEvent<HTMLElement>) => void;
  /** 沒有被日期欄位自己處理的按鍵（關著時的 Esc、Enter…）。 */
  readonly onKeyDown?: KeyboardEventHandler<HTMLInputElement>;
  readonly ref?: Ref<HTMLInputElement>;
}

/** 呼叫端給的 ref（物件或函式）也指到輸入框。 */
function assignRef(ref: Ref<HTMLInputElement> | undefined, element: HTMLInputElement | null) {
  if (typeof ref === 'function') ref(element);
  else if (ref !== undefined && ref !== null) ref.current = element;
}

/** 輸入框自己的 ref（日曆的定位、「點在外面」、焦點的去處要用）加上呼叫端給的 ref。 */
function useInputRef(ref: Ref<HTMLInputElement> | undefined) {
  const inputRef = useRef<HTMLInputElement>(null);
  const setInput = useCallback(
    (element: HTMLInputElement | null) => {
      inputRef.current = element;
      assignRef(ref, element);
    },
    [ref],
  );
  return { inputRef, setInput };
}

interface DateInputProps {
  readonly picker: DatePickerState;
  readonly setInput: (element: HTMLInputElement | null) => void;
  readonly dialogId: string;
  readonly className: string | undefined;
  readonly attributes: Pick<DatePickerProps, 'id' | 'size' | 'variant' | 'disabled' | 'readOnly' | 'aria-label' | 'aria-labelledby' | 'aria-describedby' | 'aria-invalid' | 'aria-busy'>;
}

/** 輸入框（role="combobox"，彈出的是對話框）加右邊裝飾的日曆圖示。 */
function DateInput({ picker, setInput, dialogId, className, attributes }: DateInputProps) {
  const { size = 'md', variant = 'default', ...rest } = attributes;
  return (
    <span className={styles.field} data-variant={variant}>
      <input
        {...rest}
        ref={setInput}
        type="text"
        role="combobox"
        aria-haspopup="dialog"
        aria-expanded={picker.isOpen}
        aria-controls={picker.isOpen ? dialogId : undefined}
        className={className}
        data-size={size}
        data-variant={variant}
        autoComplete="off"
        spellCheck={false}
        placeholder="年/月/日"
        inputMode={picker.inputMode}
        value={picker.text}
        onChange={(event) => {
          picker.handleInputChange(event.target.value);
        }}
        onPointerDown={picker.handleInputPointerDown}
        onClick={picker.handleInputClick}
        onKeyDown={picker.handleInputKeyDown}
      />
      <Icon name="calendar" className={styles.icon} />
    </span>
  );
}

/**
 * 日期欄位（docs/DESIGN.md 7.3「日期」）：取代原生的 <input type="date">——瀏覽器的日曆是系統畫的，字體、顏色、深色模式都管不到。
 * 照 WAI-ARIA APG 的 Date Picker Combobox：輸入框是 role="combobox"（aria-haspopup="dialog"），可以直接打字
 * （2026/12/15、2026-12-15、2026/1/5、20261215 都認得）；點輸入框打開日曆、焦點留在輸入框，↓ 把焦點移進日曆。
 * 右邊的日曆圖示是裝飾（點它和點輸入框一樣）。沒有切換開關的按鈕，所以 onDismiss 的 toggle 目前不會出現。
 */
export function DatePicker({ ref, value, onChange, onPick, onDismiss, isOpenInitially = false, onBlur, onKeyDown, ...attributes }: DatePickerProps) {
  const { inputRef, setInput } = useInputRef(ref);
  const calendarRef = useRef<HTMLDivElement>(null);
  const dialogId = useId();
  const canOpen = attributes.disabled !== true && attributes.readOnly !== true;
  const isBusy = attributes['aria-busy'] === true;
  const picker = useDatePicker({ value, canOpen, isBusy, isOpenInitially, onChange, onPick, onDismiss, onKeyDown, onBlur }, inputRef, calendarRef);
  const { rootRef, isOpen } = picker;
  const className = attributes.variant === 'bare' ? styles.bare : cx(fieldControlClassName('input', attributes['aria-invalid'] === true), styles.input);
  return (
    <div ref={rootRef} className={styles.root} onBlur={picker.handleRootBlur}>
      <DateInput {...{ picker, setInput, dialogId, className, attributes }} />
      {isOpen && <DateCalendar id={dialogId} calendarRef={calendarRef} anchorRef={inputRef} value={value} picker={picker} />}
    </div>
  );
}
