import type { Range } from '@tiptap/core';
import type { Member } from '../../../api/types';
import { mentionKeyAction } from '../../item-page/mentions/mentionKeys';
import type { DismissedMention, MentionBridge, MentionSuggestionProps } from './mentionSuggestion';

interface Session {
  readonly items: readonly Member[];
  readonly command: MentionSuggestionProps['command'];
}

export interface MentionSnapshot {
  readonly session: Session | null;
  /** 反白的是第幾個（沒有選項時沒有意義）。 */
  readonly active: number;
}

/**
 * 提及清單的狀態（開著嗎、有哪些成員、反白哪一個）與它的按鍵：放在 React 外面的小 store，
 * Tiptap 的 Suggestion 外掛在建立編輯器時就抓住了它的回呼，React 這邊用 useSyncExternalStore 訂閱。
 * ↑／↓ 選（繞回另一端）、Enter 或 Tab 插入、Esc 關上（同一個 @ 不再打開，dismissed 記著位置）。
 */
export class MentionStore implements MentionBridge {
  readonly dismissed: DismissedMention = { from: null };
  private snapshot: MentionSnapshot = { session: null, active: 0 };
  private readonly listeners = new Set<() => void>();
  private members: readonly Member[] | undefined;
  private isBusy = false;

  /** 呼叫端每次渲染後更新：可以提及的人、送出中（不開）。 */
  configure(members: readonly Member[] | undefined, isBusy: boolean): void {
    this.members = members;
    this.isBusy = isBusy;
  }

  readonly subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  };

  readonly getSnapshot = (): MentionSnapshot => this.snapshot;

  getMembers(): readonly Member[] | undefined {
    return this.members;
  }

  isBlocked(): boolean {
    return this.isBusy;
  }

  isOpen(): boolean {
    return this.snapshot.session !== null;
  }

  start(props: MentionSuggestionProps): void {
    this.set({ session: { items: props.items, command: props.command }, active: 0 });
  }

  update(props: MentionSuggestionProps): void {
    // 同一串查詢（只是換了清單）時反白的不動；沒有那麼多選項時退回最後一個。
    const active = this.snapshot.session === null ? 0 : Math.min(this.snapshot.active, Math.max(props.items.length - 1, 0));
    this.set({ session: { items: props.items, command: props.command }, active });
  }

  exit(): void {
    this.set({ session: null, active: 0 });
  }

  point(index: number): void {
    if (this.snapshot.session !== null) this.set({ session: this.snapshot.session, active: index });
  }

  choose(index: number): void {
    const member = this.snapshot.session?.items[index];
    if (member === undefined) return;
    this.snapshot.session?.command({ id: member.personId, label: member.displayName });
  }

  keyDown(event: KeyboardEvent, range: Range): boolean {
    const { session, active } = this.snapshot;
    if (session === null) return false;
    const action = mentionKeyAction(event, session.items.length > 0);
    if (action === null) return false;
    event.preventDefault();
    if (action === 'dismiss') {
      // Esc 只是關清單：說明的編輯器在 Esc 時取消編輯，所以不能再往外傳。
      event.stopPropagation();
      this.dismissed.from = range.from;
    } else if (action === 'select') this.choose(active);
    else this.set({ session, active: (active + (action === 'next' ? 1 : -1) + session.items.length) % session.items.length });
    return true;
  }

  private set(next: MentionSnapshot): void {
    this.snapshot = next;
    this.listeners.forEach((listener) => {
      listener();
    });
  }
}
