/**
 * 清單登記的「目前看得到的順序」：面板的上一個／下一個、關上面板或從整頁回來時把焦點放回清單上那一筆，都靠它。
 * 表格一份、看板每一欄一份（上一個／下一個在同一欄裡走）、我的工作一份、項目頁的下層項目一份。
 * 面板自己裡面的清單（面板裡的下層項目）不登記：上一個／下一個走的是旁邊那份清單。
 */
export interface ItemSequence {
  /** 照畫面上的順序、已經載入的項目編號。 */
  readonly keys: readonly string[];
  /** 清單的容器：找清單上那一筆的連結時只在這裡面找。沒有（窄螢幕的清單元件拿不到）時在整頁找、跳過面板。 */
  readonly container: HTMLElement | null;
}

export interface ItemSequenceRegistry {
  readonly set: (id: string, sequence: ItemSequence) => void;
  readonly remove: (id: string) => void;
  readonly subscribe: (listener: () => void) => () => void;
  /** 含這個項目的那份順序（同一個陣列，沒變就是同一個參照，可以當 useSyncExternalStore 的快照）。 */
  readonly sequenceOf: (itemKey: string) => readonly string[] | undefined;
  /** 清單上連到這個項目的連結（整頁的網址）：焦點放回去、捲到看得到的地方用。 */
  readonly linkOf: (itemKey: string) => HTMLAnchorElement | null;
  /**
   * 從清單上的哪一個連結打開面板。同一個項目在清單上有好幾個連結時（收件匣裡同一個項目的好幾則通知），
   * linkOf 先給這一個：關上面板時焦點回到點的那一則，不是同一個項目的第一則。
   */
  readonly rememberOpened: (link: HTMLAnchorElement) => void;
}

/** 面板的內容外面包著這個屬性：在整頁找清單上的連結時跳過面板裡的。 */
export const PANEL_CONTENT_ATTRIBUTE = 'data-item-panel';

function linksTo(link: HTMLAnchorElement, itemKey: string, skipPanel: boolean): boolean {
  const suffix = `/items/${encodeURIComponent(itemKey)}`;
  return (link.getAttribute('href') ?? '').endsWith(suffix) && !(skipPanel && link.closest(`[${PANEL_CONTENT_ATTRIBUTE}]`) !== null);
}

function findLink(root: ParentNode, itemKey: string, skipPanel: boolean): HTMLAnchorElement | null {
  const links = Array.from(root.querySelectorAll<HTMLAnchorElement>('a[href]'));
  return links.find((link) => linksTo(link, itemKey, skipPanel)) ?? null;
}

/** 一個面板（一頁）一份。登記放在這裡而不是 state：清單載入更多時不必讓整頁重畫，只有面板訂閱。 */
export function createItemSequenceRegistry(): ItemSequenceRegistry {
  const sequences = new Map<string, ItemSequence>();
  const listeners = new Set<() => void>();
  const notify = () => {
    listeners.forEach((listener) => {
      listener();
    });
  };
  const holding = (itemKey: string) => [...sequences.values()].find((sequence) => sequence.keys.includes(itemKey));
  // 只記最後一次：清單重畫掉了（isConnected 是 false）或連到別的項目時就不用它。
  let opened: HTMLAnchorElement | null = null;
  return {
    set: (id, sequence) => {
      sequences.set(id, sequence);
      notify();
    },
    remove: (id) => {
      if (sequences.delete(id)) notify();
    },
    subscribe: (listener) => {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
    sequenceOf: (itemKey) => holding(itemKey)?.keys,
    linkOf: (itemKey) => {
      const sequence = holding(itemKey);
      if (sequence === undefined) return null;
      const root = sequence.container ?? document;
      const skipPanel = sequence.container === null;
      if (opened?.isConnected === true && root.contains(opened) && linksTo(opened, itemKey, skipPanel)) return opened;
      return findLink(root, itemKey, skipPanel);
    },
    rememberOpened: (link) => {
      opened = link;
    },
  };
}
