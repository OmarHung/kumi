import {
  useCallback,
  useEffect,
  useId,
  useRef,
  type FocusEvent,
  type KeyboardEvent,
  type KeyboardEventHandler,
  type PointerEvent,
  type ReactNode,
  type Ref,
} from 'react';
import { cx } from '../../lib/cx';
import type { FloatingAlign } from '../../lib/useAnchoredPosition';
import { Icon } from '../Icon/Icon';
import { emptyId, optionId, SelectList } from './SelectList';
import { useSelect } from './useSelect';
import styles from './Select.module.css';

export interface SelectOption {
  readonly value: string;
  readonly label: string;
  /** 選項前面的小圖（狀態的形狀、頭像）：裝飾，aria-hidden。選到它時按鈕上也畫。 */
  readonly leading?: ReactNode;
  /** 有值：不能選（aria-disabled）、方向鍵停得到，原因寫在第二行並用 aria-describedby 連過去。寫短語，完整的說明放在清單外面。 */
  readonly disabledReason?: string;
  /** 從這一項開始是新的一組：前面畫一條細線（第一項不畫）。 */
  readonly startsGroup?: boolean;
  /**
   * 這一項是一組的第一項、組有名字（批次改狀態時依流程分組）：前面畫一行組的名字（不是選項、不能選、報讀軟體跳過）。
   * 第一組的名字在最上面，後面的組前面多一條細線；不要和 startsGroup 一起給。
   */
  readonly groupLabel?: string;
  /** 篩選時除了 label 也比對這些字（人員的 Email）。 */
  readonly keywords?: readonly string[];
}

/** 沒選就關上的原因。scrolled 是按鈕被捲出看得到的範圍（格子用它把焦點放回格子，和點外面分開）。 */
export type SelectDismissReason = 'escape' | 'tab' | 'outside' | 'scrolled' | 'toggle';

interface SelectOwnProps {
  readonly value: string;
  readonly options: readonly SelectOption[];
  /** 選了和目前不一樣的值才呼叫。 */
  readonly onChange: (value: string) => void;
  /** 每一次明確的選擇都呼叫，選了目前的值也算（在 onChange 之後）：格子用它結束編輯。 */
  readonly onPick?: (value: string) => void;
  /**
   * 沒選就關上了：Esc、Tab、點外面（焦點移到外面也算）、按鈕被捲出畫面（scrolled）、再按一次按鈕。
   * Tab 是在按鍵事件裡呼叫，焦點還沒移走。
   */
  readonly onDismiss?: (reason: SelectDismissReason) => void;
  readonly size?: 'md' | 'sm';
  /** fill（預設）：撐滿欄位；content：照目前的值的寬度、8～16rem（工具列與設定頁的列上）。 */
  readonly width?: 'fill' | 'content';
  /**
   * default（預設）：和輸入框同一套外框。bare：沒有自己的外框、底色與焦點光暈，撐滿外層——就地編輯的格子裡用，
   * 外框由格子畫（docs/DESIGN.md 7.3），不會出現格子一圈、下拉又一圈的兩層框。
   */
  readonly variant?: 'default' | 'bare';
  /**
   * 按鈕裡換成這個內容（不畫選項的文字與箭頭）：就地編輯的格子用它顯示格子原本的樣子，欄寬不變（docs/DESIGN.md 8.3）。
   * 內容的文字要說得出目前的值：報讀軟體把按鈕的內容當成 combobox 的值。
   */
  readonly display?: ReactNode;
  /** value 對不到任何選項時顯示的灰字。 */
  readonly placeholder?: string;
  /** 掛上就打開（格子進入編輯時）。 */
  readonly isOpenInitially?: boolean;
  readonly disabled?: boolean;
  readonly align?: FloatingAlign;
  readonly id?: string;
  readonly className?: string;
  readonly ref?: Ref<HTMLButtonElement>;
  readonly 'aria-describedby'?: string;
  readonly 'aria-invalid'?: boolean;
  readonly 'aria-busy'?: boolean;
  /** 停得到、打不開（要說明原因的停用）。 */
  readonly 'aria-disabled'?: boolean;
  /** 焦點離開整個下拉（按鈕與篩選框）時呼叫；焦點在按鈕與篩選框之間移動不算。 */
  readonly onBlur?: (event: FocusEvent<HTMLElement>) => void;
  /** Select 自己沒有處理的按鍵才會到這裡（例如關著時的 Esc）。 */
  readonly onKeyDown?: KeyboardEventHandler<HTMLButtonElement>;
}

/** 名稱至少給一個：combobox 的名稱不能來自內容（值是按鈕裡的文字），沒有名稱的過不了型別檢查。 */
type SelectName =
  | { readonly 'aria-label': string; readonly 'aria-labelledby'?: string }
  | { readonly 'aria-label'?: string; readonly 'aria-labelledby': string };

export type SelectProps = SelectOwnProps & SelectName;

interface SelectAria {
  readonly 'aria-label'?: string;
  readonly 'aria-labelledby'?: string;
  readonly 'aria-describedby'?: string;
  readonly 'aria-invalid'?: boolean;
  readonly 'aria-busy'?: boolean;
  readonly 'aria-disabled'?: boolean;
}

/** 按鈕的 ARIA（combobox 的合約，規範 7.3）：打開時才有 aria-controls 與 aria-activedescendant（有篩選框時反白在篩選框上）。 */
function buttonAria(aria: SelectAria, controls: string | null, activeDescendant: string | undefined) {
  return {
    'aria-haspopup': 'listbox',
    'aria-expanded': controls !== null,
    'aria-controls': controls ?? undefined,
    'aria-activedescendant': activeDescendant,
    'aria-label': aria['aria-label'],
    'aria-labelledby': aria['aria-labelledby'],
    'aria-describedby': aria['aria-describedby'],
    'aria-invalid': aria['aria-invalid'] === true || undefined,
    'aria-busy': aria['aria-busy'] === true || undefined,
    'aria-disabled': aria['aria-disabled'] === true || undefined,
  } as const;
}

/** 空白鍵的 click 在放開時才觸發：按下時已經開關過了，放開時不能再按一次按鈕。 */
function preventSpaceClick(event: KeyboardEvent<HTMLButtonElement>) {
  if (event.key === ' ') event.preventDefault();
}

/**
 * 滑鼠左鍵按下就把焦點放到按鈕上：Safari 點 <button> 不給它焦點，之後的「焦點離開就關上」、Esc、方向鍵都接不到。
 * 擋掉預設動作（不讓瀏覽器自己決定焦點），click 照常觸發開關。觸控與其他按鍵照瀏覽器原本的做法。
 */
function focusOnMousePress(event: PointerEvent<HTMLButtonElement>) {
  if (event.pointerType !== 'mouse' || event.button !== 0) return;
  event.preventDefault();
  event.currentTarget.focus({ preventScroll: true });
}

/** 呼叫端給的 ref（物件或函式）也指到按鈕。 */
function assignRef(ref: Ref<HTMLButtonElement> | undefined, element: HTMLButtonElement | null) {
  if (typeof ref === 'function') ref(element);
  else if (ref !== undefined && ref !== null) ref.current = element;
}

/** 按鈕自己的 ref（清單的定位與「點在外面」要用）加上呼叫端給的 ref。 */
function useButtonRef(ref: Ref<HTMLButtonElement> | undefined) {
  const buttonRef = useRef<HTMLButtonElement>(null);
  const setButton = useCallback(
    (element: HTMLButtonElement | null) => {
      buttonRef.current = element;
      assignRef(ref, element);
    },
    [ref],
  );
  return { buttonRef, setButton };
}

/**
 * 打開時焦點到篩選框。用 useEffect（不是 layout effect）：格子的編輯器在自己的 layout effect 裡把焦點放到第一個元素
 * （按鈕），這裡要在它之後才搶得到。
 */
function useFocusFilter(isOpen: boolean, isFilterable: boolean) {
  const inputRef = useRef<HTMLInputElement>(null);
  useEffect(() => {
    const input = inputRef.current;
    if (!isOpen || !isFilterable || input === null) return;
    input.focus({ preventScroll: true });
    // 關著時打的字已經在框裡：游標放在後面，接著打的字才接在後面。
    input.setSelectionRange(input.value.length, input.value.length);
  }, [isOpen, isFilterable]);
  return inputRef;
}

/** 焦點離開整個下拉：移到外面的元素，或去了「沒有元素」（切到別的視窗）。 */
function leavesRoot(event: FocusEvent<HTMLElement>): boolean {
  const next = event.relatedTarget;
  return !(next instanceof Node) || !event.currentTarget.contains(next);
}

/** 外層的 blur：清單照「焦點移到外面就關上」；呼叫端的 onBlur 只在焦點離開整個下拉時叫（按鈕與篩選框之間移動不算）。 */
function rootBlurHandler(closeOnBlur: (event: FocusEvent<HTMLElement>) => void, onBlur: SelectOwnProps['onBlur']) {
  return (event: FocusEvent<HTMLElement>) => {
    closeOnBlur(event);
    if (leavesRoot(event)) onBlur?.(event);
  };
}

interface SelectValueProps {
  readonly selected: SelectOption | undefined;
  readonly placeholder: string | undefined;
  readonly display: ReactNode;
}

/** 按鈕上的值：目前的值（前面帶同一個小圖），對不到選項時是灰字的 placeholder；給了 display 就只放它（不畫箭頭）。 */
function SelectValue({ selected, placeholder, display }: SelectValueProps) {
  if (display !== undefined) return <span className={styles.value}>{display}</span>;
  return (
    <>
      {selected?.leading !== undefined && (
        <span className={styles.leading} aria-hidden="true">
          {selected.leading}
        </span>
      )}
      <span className={styles.value} data-placeholder={selected === undefined || undefined}>
        {selected?.label ?? placeholder}
      </span>
      <Icon name="chevronDown" className={styles.chevron} />
    </>
  );
}

/** 反白的選項的 id：照它在完整清單裡的位置編（positions），篩選時同一個選項 id 不變，換了反白 id 就換。 */
function activeIdOf(listId: string, active: number, positions: readonly number[]): string | undefined {
  const position = positions[active];
  return position === undefined ? undefined : optionId(listId, position);
}

/** 按鈕的 aria-activedescendant：沒有篩選框時指到反白的選項；一個選項都沒有時指到「沒有可以選的」。有篩選框時反白在篩選框上。 */
function buttonActiveDescendant(listId: string, activeId: string | undefined, hasAnyOption: boolean): string | undefined {
  return activeId ?? (hasAnyOption ? undefined : emptyId(listId));
}

/**
 * 從幾個選項裡選一個值（docs/DESIGN.md 7.3）：取代原生的 <select>，清單的長相和 Menu 一樣，淺色、深色、每個系統都一致。
 * 按鈕是 role="combobox"，名稱一定要另外給（aria-label 或 aria-labelledby；型別檢查擋）：combobox 的名稱不能來自內容，
 * 值是按鈕裡的文字。外層和 Menu 一樣是 display: contents 的容器（給「點在外面」與焦點離開的判斷），className 加在按鈕上。
 * 選項 10 個以上時清單最上面有篩選框（selectFilter.ts）。
 */
export function Select({ ref, value, options, size = 'md', width = 'fill', variant = 'default', display, placeholder, isOpenInitially = false, disabled = false, ...rest }: SelectProps) {
  const { align = 'start', id, className, onChange, onPick, onDismiss, onBlur, onKeyDown, ...aria } = rest;
  const { buttonRef, setButton } = useButtonRef(ref);
  const listId = useId();
  const canOpen = !disabled && aria['aria-disabled'] !== true;
  const { isOpen, active, visible, positions, isFilterable, filter, rootRef, handleRootBlur, closeWhenAnchorHidden, handleKeyDown, toggle, pick, setActive } = useSelect(
    { options, value, isOpenInitially, canOpen, isBusy: aria['aria-busy'] === true, onChange, onPick, onDismiss, onKeyDown },
    buttonRef,
  );
  const inputRef = useFocusFilter(isOpen, isFilterable);
  const activeId = activeIdOf(listId, active, positions);
  const activeDescendant = isOpen && !isFilterable ? buttonActiveDescendant(listId, activeId, options.length > 0) : undefined;
  return (
    <div ref={rootRef} className={styles.root} onBlur={rootBlurHandler(handleRootBlur, onBlur)}>
      <button
        {...buttonAria(aria, isOpen ? listId : null, activeDescendant)}
        ref={setButton}
        type="button"
        role="combobox"
        id={id}
        className={cx(styles.trigger, className)}
        data-size={size}
        data-width={width}
        data-variant={variant}
        disabled={disabled}
        onPointerDown={focusOnMousePress}
        onClick={toggle}
        onKeyDown={handleKeyDown}
        onKeyUp={preventSpaceClick}
      >
        <SelectValue selected={options.find((option) => option.value === value)} placeholder={placeholder} display={display} />
      </button>
      {isOpen && (
        <SelectList
          {...{ listId, value, active, activeId, align, visible, positions }}
          hasAnyOption={options.length > 0}
          anchorRef={buttonRef}
          aria={aria}
          filter={isFilterable ? { controls: filter, inputRef, all: options } : null}
          onPoint={setActive}
          onChoose={pick}
          onAnchorHidden={closeWhenAnchorHidden}
        />
      )}
    </div>
  );
}
