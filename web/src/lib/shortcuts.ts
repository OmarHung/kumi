import { useEffect, useEffectEvent } from 'react';
import { isComposingNativeKey } from './keyboard';

/**
 * 快捷鍵的登記表與 hook（docs/DESIGN.md 8.3「快捷鍵」）。
 * mod 是平台的主要修飾鍵：Mac 是 ⌘（metaKey），其他平台是 Ctrl（ctrlKey）。
 */
export type ShortcutModifier = 'mod' | 'meta' | 'ctrl' | 'alt' | 'shift';

export interface ShortcutSpec {
  /** KeyboardEvent.key：'k'、'ArrowDown'、'Escape'。字母不分大小寫。 */
  readonly key: string;
  /** 要剛好按著這幾個修飾鍵：多按了別的（例如 Ctrl+Shift+K）就不算。 */
  readonly modifiers?: readonly ShortcutModifier[];
  /** 這個快捷鍵做什麼，給說明（listShortcuts）用。 */
  readonly description: string;
  /** 沒帶 ⌘／Ctrl 的快捷鍵，在輸入框、文字框、下拉選單、contenteditable 裡也作用。 */
  readonly allowInInputs?: boolean;
}

/** 唯一的登記表：新的快捷鍵先加在這裡，畫面上的說明與 aria-keyshortcuts 都從這裡來。 */
export const SHORTCUTS = {
  openJump: { key: 'k', modifiers: ['mod'], description: '搜尋或跳到…' },
  itemPrev: { key: 'ArrowUp', modifiers: ['alt'], description: '上一個項目' },
  itemNext: { key: 'ArrowDown', modifiers: ['alt'], description: '下一個項目' },
  closePanel: { key: 'Escape', description: '關閉面板' },
} as const satisfies Record<string, ShortcutSpec>;

export type ShortcutId = keyof typeof SHORTCUTS;

export interface ShortcutEntry {
  readonly id: ShortcutId;
  readonly description: string;
  /** 畫面上的寫法（formatShortcut）。 */
  readonly keys: string;
}

type PhysicalModifier = 'ctrl' | 'alt' | 'shift' | 'meta';

const MODIFIER_ORDER: readonly PhysicalModifier[] = ['ctrl', 'alt', 'shift', 'meta'];
const MAC_SYMBOLS: Readonly<Record<PhysicalModifier, string>> = { ctrl: '⌃', alt: '⌥', shift: '⇧', meta: '⌘' };
const OTHER_NAMES: Readonly<Record<PhysicalModifier, string>> = { ctrl: 'Ctrl', alt: 'Alt', shift: 'Shift', meta: 'Meta' };
const ARIA_NAMES: Readonly<Record<PhysicalModifier, string>> = { ctrl: 'Control', alt: 'Alt', shift: 'Shift', meta: 'Meta' };
const KEY_LABELS: Readonly<Record<string, string>> = {
  ArrowUp: '↑',
  ArrowDown: '↓',
  ArrowLeft: '←',
  ArrowRight: '→',
  Escape: 'Esc',
  ' ': 'Space',
};
const COMMAND_MODIFIERS: ReadonlySet<ShortcutModifier> = new Set(['mod', 'meta', 'ctrl']);
// 按鈕類的輸入框沒有自己的按鍵行為，快捷鍵照樣作用。
const NON_TEXT_INPUTS: ReadonlySet<string> = new Set(['button', 'checkbox', 'radio', 'reset', 'submit', 'image', 'file', 'color']);
const MAC_PLATFORM = /mac|iphone|ipad|ipod/i;
// 快速跳轉（features/jump/useIsModalDialogOpen.ts）刻意不排除它：浮動視窗開著時 ⌘K 照樣停用（對話框上不再開對話框，docs/DESIGN.md 6.2），
// 所以兩處不同——這裡是「背後的畫面不能被按鍵改動」，那裡是「不疊對話框」。
// data-shortcuts-through：項目的浮動視窗自己處理按鍵（Esc、Alt+↑／↓），它開著時快捷鍵照樣作用；疊在上面的對話框不帶這個屬性。
const MODAL_DIALOG = '[role="dialog"][aria-modal="true"]:not([data-shortcuts-through])';
const LETTER = /^[a-z]$/i;
const LAST_ASCII = 0x7e;

function currentPlatform(): string {
  // navigator.platform 已經不建議用，但 userAgentData 只有 Chromium 有；透過只有這兩個屬性的型別讀，不碰標成過時的宣告。
  const legacy: { readonly platform?: string; readonly userAgentData?: { readonly platform?: string } } = navigator;
  return legacy.userAgentData?.platform ?? legacy.platform ?? '';
}

/** 是不是 Mac（含 iPhone、iPad）：⌘ 還是 Ctrl。 */
export function isMac(platform: string = currentPlatform()): boolean {
  return MAC_PLATFORM.test(platform);
}

function physicalModifiers(spec: ShortcutSpec, mac: boolean): ReadonlySet<PhysicalModifier> {
  return new Set((spec.modifiers ?? []).map((modifier) => (modifier === 'mod' ? (mac ? 'meta' : 'ctrl') : modifier)));
}

function pressedModifiers(event: KeyboardEvent): ReadonlySet<PhysicalModifier> {
  const pressed: readonly [PhysicalModifier, boolean][] = [
    ['ctrl', event.ctrlKey],
    ['alt', event.altKey],
    ['shift', event.shiftKey],
    ['meta', event.metaKey],
  ];
  return new Set(pressed.filter(([, isDown]) => isDown).map(([modifier]) => modifier));
}

function isAscii(character: string): boolean {
  return character.length === 1 && character.charCodeAt(0) <= LAST_ASCII;
}

function keyMatches(wanted: string, event: KeyboardEvent): boolean {
  // Chrome 的自動填入送出的 keydown 沒有 key。
  const loose: { readonly key?: string } = event;
  const key = loose.key ?? '';
  if (key.toLowerCase() === wanted.toLowerCase()) return true;
  // 俄文之類的鍵盤配置按同一個實體按鍵打出別的字：單一英文字母退回看實體按鍵。
  return LETTER.test(wanted) && key !== '' && !isAscii(key) && event.code === `Key${wanted.toUpperCase()}`;
}

/** 這個按鍵是不是這個快捷鍵：按鍵一樣，修飾鍵剛好一樣。 */
export function matchesShortcut(spec: ShortcutSpec, event: KeyboardEvent, mac: boolean = isMac()): boolean {
  const wanted = physicalModifiers(spec, mac);
  const pressed = pressedModifiers(event);
  const sameModifiers = wanted.size === pressed.size && [...wanted].every((modifier) => pressed.has(modifier));
  return sameModifiers && keyMatches(spec.key, event);
}

function isTextEntry(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false;
  if (target instanceof HTMLTextAreaElement || target instanceof HTMLSelectElement) return true;
  // 自己做的下拉（components/Select 的按鈕）和原生的下拉一樣：關著時的 Esc 是格子的（取消編輯），Alt+↓ 是打開清單。
  if (target.getAttribute('role') === 'combobox') return true;
  if (target instanceof HTMLInputElement) return !NON_TEXT_INPUTS.has(target.type);
  return target.closest('[contenteditable]:not([contenteditable="false"])') !== null;
}

/** 強制回應的對話框（components/Dialog）開著：背後的畫面不能被按鍵改動。 */
function isModalDialogOpen(): boolean {
  return document.querySelector(MODAL_DIALOG) !== null;
}

/**
 * 不處理的情形：內層已經處理過（preventDefault；stopPropagation 的根本到不了 document）、輸入法選字中、
 * 沒帶 ⌘／Ctrl 的快捷鍵在輸入框裡（Mac 的 Option+↑ 在文字框是移到段落開頭，Windows 的 Alt+↓ 在下拉選單是打開選單），
 * 以及沒帶 ⌘／Ctrl 的快捷鍵在強制回應的對話框開著時：焦點在對話框裡、或點了背景掉到頁面本體，Alt+↓、Esc 都不能
 * 換掉或關掉背後的面板（刪除的確認框不能變成刪下一個）。帶 ⌘／Ctrl 的（⌘K）由呼叫端自己決定（isEnabled）。
 */
function shouldSkip(spec: ShortcutSpec, event: KeyboardEvent): boolean {
  if (event.defaultPrevented || isComposingNativeKey(event)) return true;
  const isCommand = (spec.modifiers ?? []).some((modifier) => COMMAND_MODIFIERS.has(modifier));
  if (isCommand) return false;
  return (spec.allowInInputs !== true && isTextEntry(event.target)) || isModalDialogOpen();
}

function keyLabel(key: string): string {
  return KEY_LABELS[key] ?? (key.length === 1 ? key.toUpperCase() : key);
}

function orderedModifiers(spec: ShortcutSpec, mac: boolean): readonly PhysicalModifier[] {
  const wanted = physicalModifiers(spec, mac);
  return MODIFIER_ORDER.filter((modifier) => wanted.has(modifier));
}

/** 畫面上的寫法：Mac 是「⌘K」「⌥↓」（⌃⌥⇧⌘ 的順序），其他平台是「Ctrl+K」「Alt+↓」。 */
export function formatShortcut(spec: ShortcutSpec, mac: boolean = isMac()): string {
  const modifiers = orderedModifiers(spec, mac);
  if (mac) return [...modifiers.map((modifier) => MAC_SYMBOLS[modifier]), keyLabel(spec.key)].join('');
  return [...modifiers.map((modifier) => OTHER_NAMES[modifier]), keyLabel(spec.key)].join('+');
}

/** 給按鈕的 aria-keyshortcuts：「Meta+K」「Control+K」「Alt+ArrowDown」。 */
export function ariaKeyShortcuts(spec: ShortcutSpec, mac: boolean = isMac()): string {
  const key = spec.key.length === 1 ? spec.key.toUpperCase() : spec.key;
  return [...orderedModifiers(spec, mac).map((modifier) => ARIA_NAMES[modifier]), key].join('+');
}

/** 登記表裡的每一個快捷鍵，照登記的順序：給之後的說明用。 */
export function listShortcuts(mac: boolean = isMac()): readonly ShortcutEntry[] {
  const ids = Object.keys(SHORTCUTS) as readonly ShortcutId[];
  return ids.map((id) => ({ id, description: SHORTCUTS[id].description, keys: formatShortcut(SHORTCUTS[id], mac) }));
}

export interface ShortcutOptions {
  /** false 時不聽（例如沒有上一個項目）：按鍵照常交給瀏覽器。預設 true。 */
  readonly isEnabled?: boolean;
}

/**
 * 掛著時聽這個快捷鍵。聽在 document 的冒泡階段：內層（編輯中的格子、開著的選單、對話框）先處理，
 * preventDefault 或 stopPropagation 過的就不再處理；處理了就 preventDefault 與 stopPropagation，同一個按鍵只做一件事。
 */
export function useShortcut(spec: ShortcutSpec, handler: (event: KeyboardEvent) => void, options: ShortcutOptions = {}): void {
  const { isEnabled = true } = options;
  const handleKeyDown = useEffectEvent((event: KeyboardEvent) => {
    if (shouldSkip(spec, event) || !matchesShortcut(spec, event)) return;
    event.preventDefault();
    event.stopPropagation();
    handler(event);
  });
  useEffect(() => {
    if (!isEnabled) return undefined;
    const listener = (event: KeyboardEvent) => {
      handleKeyDown(event);
    };
    document.addEventListener('keydown', listener);
    return () => {
      document.removeEventListener('keydown', listener);
    };
  }, [isEnabled]);
}
