import { useId, type ComponentPropsWithRef, type MouseEvent, type ReactNode } from 'react';
import { cx } from '../../lib/cx';
import { preventComposingSubmit } from '../../lib/keyboard';
import { Icon, type IconName } from '../Icon/Icon';
import { Select, type SelectProps } from '../Select/Select';
import styles from './Field.module.css';

/** md：表單（--control-height）；sm：工具列與表格列上的控制項（--control-height-sm，docs/DESIGN.md 第 5 節）。 */
export type FieldSize = 'md' | 'sm';

interface FieldOwnProps {
  readonly label: string;
  /** 欄位下方的說明，例如格式。 */
  readonly hint?: ReactNode;
  /** 有值時欄位標成無效，訊息透過 aria-describedby 和欄位連在一起。 */
  readonly error?: string;
  readonly size?: FieldSize;
  /**
   * 標籤只給報讀軟體（global.css 的 visually-hidden），名稱照舊是 label。只用在工具列這類
   * 「意思由選項文字、提示文字或圖示帶出來」的地方（docs/DESIGN.md 第 7 節的 Toolbar）。
   */
  readonly isLabelHidden?: boolean;
}

interface FieldIds {
  readonly controlId: string;
  /** 標籤的 id：Select 的清單（listbox）用 aria-labelledby 拿到和按鈕一樣的名稱。 */
  readonly labelId: string;
  readonly hintId: string;
  readonly errorId: string;
  readonly describedBy: string | undefined;
}

function useFieldIds(hint: ReactNode, error: string | undefined, extra?: string, givenId?: string, isExtraAfterHint = false): FieldIds {
  const generatedId = useId();
  const controlId = givenId ?? generatedId;
  const hintId = `${controlId}-hint`;
  const errorId = `${controlId}-error`;
  const hintIds = [hint ? hintId : undefined, error ? errorId : undefined];
  const ids = (isExtraAfterHint ? [hintIds[0], extra, hintIds[1]] : [extra, ...hintIds]).filter(Boolean);
  return { controlId, labelId: `${controlId}-label`, hintId, errorId, describedBy: ids.length > 0 ? ids.join(' ') : undefined };
}

interface FieldFrameProps extends Omit<FieldOwnProps, 'size'> {
  readonly ids: FieldIds;
  readonly className: string | undefined;
  /** 控制項自己帶看得到的標籤（一組勾選的 legend、勾選框旁邊的字）：不再畫 label。 */
  readonly hasOwnLabel?: boolean;
  readonly children: ReactNode;
}

function FieldFrame({ label, hint, error, isLabelHidden = false, hasOwnLabel = false, ids, className, children }: FieldFrameProps) {
  return (
    <div className={cx(styles.field, className)}>
      {!hasOwnLabel && (
        <label id={ids.labelId} htmlFor={ids.controlId} className={isLabelHidden ? 'visually-hidden' : styles.label}>
          {label}
        </label>
      )}
      {children}
      {hint && (
        <p id={ids.hintId} className={styles.hint}>
          {hint}
        </p>
      )}
      {error && (
        <p id={ids.errorId} className={styles.error}>
          <Icon name="error" className={styles.errorIcon} />
          {error}
        </p>
      )}
    </div>
  );
}

// 原生的 size（輸入框的字數）這裡用不到，名字讓給控制項的高度。
type TextFieldProps = Omit<ComponentPropsWithRef<'input'>, 'id' | 'size'> &
  FieldOwnProps & {
    /**
     * 輸入框開頭的圖示（搜尋框的放大鏡）。裝飾（aria-hidden）：意思要由標籤、提示文字帶出來。
     * 疊在輸入框裡、垂直置中，高度跟著 size；輸入框的左內距讓出它的位置。
     */
    readonly leadingIcon?: IconName;
    /**
     * 錯誤畫在輸入框的框裡（右端），不在欄位下面多一行：給工具列與表格上方那一列（列的高度固定，多一行會把下面的表格往下推）。
     * 訊息照舊用 aria-describedby 連到輸入框。不和 leadingIcon 一起用。
     */
    readonly isErrorInside?: boolean;
  };

/** 有開頭的圖示時，輸入框和圖示包在一起（圖示的定位基準）；沒有時輸入框照舊直接放在欄位裡。 */
function withLeadingIcon(icon: IconName | undefined, control: ReactNode): ReactNode {
  if (icon === undefined) return control;
  return (
    <span className={styles.leading}>
      <Icon name={icon} className={styles.leadingIcon} />
      {control}
    </span>
  );
}

/** 點到框裡的錯誤訊息（不是輸入框本身）：焦點進輸入框，和點輸入框一樣。 */
function focusInputInside(event: MouseEvent<HTMLElement>) {
  if (event.target instanceof HTMLInputElement) return;
  event.preventDefault();
  event.currentTarget.querySelector('input')?.focus();
}

/**
 * 錯誤畫在框裡：外面那一層畫成輸入框的樣子（外框、高度、焦點環、錯誤色都是同一套），輸入框本身不畫框、佔剩下的寬度，
 * 錯誤在右端。打的字很長時在輸入框裡捲動，不會跑到錯誤底下。
 */
function withErrorInside(control: ReactNode, error: string | undefined, errorId: string, size: FieldSize): ReactNode {
  return (
    <span className={cx(styles.control, styles.inside, error && styles.invalid)} data-size={size} onMouseDown={focusInputInside}>
      {control}
      {error && (
        <span id={errorId} className={styles.insideError}>
          <Icon name="error" className={styles.insideErrorIcon} />
          <span className={styles.insideErrorText}>{error}</span>
        </span>
      )}
    </span>
  );
}

/**
 * 表單裡的文字框：按 Enter 會送出表單，但輸入法選字用的 Enter 不算（見 lib/keyboard.ts）。
 * 日期不用它（type="date" 是瀏覽器畫的日曆）：用 components/DatePicker 的 DateField。
 */
export function TextField({
  label,
  hint,
  error,
  size = 'md',
  isLabelHidden,
  leadingIcon,
  isErrorInside = false,
  className,
  onKeyDown,
  ...inputProps
}: TextFieldProps) {
  const ids = useFieldIds(hint, error, inputProps['aria-describedby']);
  const control = (
    <input
      {...inputProps}
      onKeyDown={(event) => {
        preventComposingSubmit(event);
        onKeyDown?.(event);
      }}
      id={ids.controlId}
      data-size={size}
      className={isErrorInside ? styles.insideInput : cx(styles.control, error && styles.invalid)}
      aria-invalid={error ? true : inputProps['aria-invalid']}
      aria-describedby={ids.describedBy}
    />
  );
  return (
    <FieldFrame label={label} hint={hint} error={isErrorInside ? undefined : error} isLabelHidden={isLabelHidden} ids={ids} className={className}>
      {isErrorInside ? withErrorInside(control, error, ids.errorId, size) : withLeadingIcon(leadingIcon, control)}
    </FieldFrame>
  );
}

/**
 * 下拉欄位：選項用 options 給，控制項是 components/Select（docs/DESIGN.md 7.3）。標籤指到按鈕（點標籤會打開清單），
 * 按鈕與清單的名稱都是標籤（aria-labelledby）；說明與錯誤用 aria-describedby 連過去。
 * className 和 TextField 一樣是加在欄位的外框上（不是按鈕）。表單裡撐滿欄位；工具列那一列（size="sm" 加 isLabelHidden）
 * 照目前的值的寬度，最窄 8rem、最寬 16rem（Select 的 width="content"）。
 */
export type SelectFieldProps = FieldOwnProps &
  Omit<SelectProps, 'id' | 'aria-label' | 'aria-labelledby' | 'aria-invalid' | 'width'> & {
    /** 傳進來的 aria-describedby 排在欄位自己的說明後面（預設在前面）：說明先唸，補充的內容（模板的預覽）接在後。 */
    readonly isDescribedByAfterHint?: boolean;
  };

export function SelectField({
  label,
  hint,
  error,
  size = 'md',
  isLabelHidden = false,
  isDescribedByAfterHint = false,
  className,
  ...selectProps
}: SelectFieldProps) {
  const ids = useFieldIds(hint, error, selectProps['aria-describedby'], undefined, isDescribedByAfterHint);
  return (
    <FieldFrame label={label} hint={hint} error={error} isLabelHidden={isLabelHidden} ids={ids} className={className}>
      <Select
        {...selectProps}
        id={ids.controlId}
        size={size}
        width={isLabelHidden && size === 'sm' ? 'content' : 'fill'}
        aria-labelledby={ids.labelId}
        aria-invalid={error ? true : undefined}
        aria-describedby={ids.describedBy}
      />
    </FieldFrame>
  );
}

/** FieldShell 交給控制項的東西：id 給 label 的 htmlFor，describedBy 是說明與錯誤（控制項自己設 aria-describedby）。 */
export interface FieldShellControl {
  readonly id: string;
  readonly describedBy: string | undefined;
}

export interface FieldShellProps {
  readonly label: string;
  readonly hint?: ReactNode;
  readonly error?: string;
  /** 控制項的 id；沒給時自己產生。呼叫端要找到控制項時給（例如把焦點放到第一個有問題的欄位）。 */
  readonly id?: string;
  /** 控制項自己帶看得到的標籤（一組勾選的 legend、勾選框旁邊的字）：這裡不再畫 label，說明與錯誤照樣。 */
  readonly hasOwnLabel?: boolean;
  /** 標籤只給報讀軟體（和 TextField 的 isLabelHidden 一樣，工具列那一列用）。 */
  readonly isLabelHidden?: boolean;
  /** 呼叫端自己的說明（例如清單外面的長句），排在提示與錯誤前面。 */
  readonly describedBy?: string;
  readonly className?: string;
  readonly children: (control: FieldShellControl) => ReactNode;
}

/**
 * 表單欄位的外框（標籤、說明、錯誤與 describedBy），控制項由呼叫端畫：給不是 TextField／SelectField 的控制項，
 * 例如表格的編輯器放進補填對話框。長相和 TextField 同一套，標籤、錯誤的樣式只維護一份。
 */
export function FieldShell({ label, hint, error, id, hasOwnLabel = false, isLabelHidden = false, describedBy, className, children }: FieldShellProps) {
  const ids = useFieldIds(hint, error, describedBy, id);
  return (
    <FieldFrame label={label} hint={hint} error={error} hasOwnLabel={hasOwnLabel} isLabelHidden={isLabelHidden} ids={ids} className={className}>
      {children({ id: ids.controlId, describedBy: ids.describedBy })}
    </FieldFrame>
  );
}

/**
 * 輸入框的長相（高度、內距 --space-3、外框、滑過變色、錯誤的紅框；多行的不固定高度）：給自己畫控制項、
 * 又要和 TextField 一樣的地方（FieldShell 的控制項）。下拉不在這裡：一律是 components/Select，它自己有這一套長相。
 */
export function fieldControlClassName(kind: 'input' | 'textarea', isInvalid: boolean): string {
  return cx(styles.control, kind === 'textarea' && styles.multiline, isInvalid && styles.invalid);
}

/** 看得到的欄位標籤的長相（給自己畫標籤的控制項，例如一組勾選的 legend）：和 TextField 的標籤一樣。 */
export function fieldLabelClassName(): string {
  return styles.label ?? '';
}
