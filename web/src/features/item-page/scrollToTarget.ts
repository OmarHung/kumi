/**
 * 往上找第一個真的會捲動的祖先（面板捲動的那一層）。只看 overflow 不夠：一邊設了 hidden，另一邊算出來就是 auto，
 * 那種內容沒超出的祖先不會捲，要跳過。找不到是 null（整頁捲）。
 */
function scrollParentOf(element: HTMLElement): HTMLElement | null {
  for (let node = element.parentElement; node !== null; node = node.parentElement) {
    const { overflowY } = getComputedStyle(node);
    if ((overflowY === 'auto' || overflowY === 'scroll') && node.scrollHeight > node.clientHeight) return node;
  }
  return null;
}

/**
 * 把目標捲到看得到的地方，一般置中；比可見的高度還高時對齊開頭（留出 scroll-margin，黏住的標題列不蓋住開頭）。
 * 在面板（自己捲動的那一層）裡只捲面板：scrollIntoView 會連帶捲整頁，清單頁的頁面比一個畫面高時，
 * 旁邊的清單被捲到頁列底下（實測收件匣捲了 108px）。不在會捲動的容器裡（整頁的項目頁）才用 scrollIntoView。
 */
export function scrollTargetIntoView(entry: HTMLElement, behavior: ScrollBehavior): void {
  const container = scrollParentOf(entry);
  if (container === null) {
    // jsdom 等環境沒有 scrollIntoView。
    if (typeof entry.scrollIntoView !== 'function') return;
    entry.scrollIntoView({ block: entry.offsetHeight > window.innerHeight ? 'start' : 'center', behavior });
    return;
  }
  const top = entry.getBoundingClientRect().top - container.getBoundingClientRect().top + container.scrollTop;
  const isTall = entry.offsetHeight > container.clientHeight;
  const margin = Number.parseFloat(getComputedStyle(entry).scrollMarginBlockStart) || 0;
  const destination = isTall ? top - margin : top - (container.clientHeight - entry.offsetHeight) / 2;
  container.scrollTo({ top: Math.max(0, destination), behavior });
}

/** 在容器的內容裡，要捲到哪裡才整個看得到；已經看得到是 null。太高放不下時對齊開頭（扣掉 scroll-margin）。 */
function revealDestination(element: HTMLElement, container: HTMLElement): number | null {
  const top = element.getBoundingClientRect().top - container.getBoundingClientRect().top + container.scrollTop;
  const margin = Number.parseFloat(getComputedStyle(element).scrollMarginBlockStart) || 0;
  const isTall = element.offsetHeight + margin > container.clientHeight;
  if (isTall || top - margin < container.scrollTop) return top - margin;
  // 下面也留 scroll-margin：右下角的 AI 助理按鈕在的時候，「送出留言」不被它蓋住（ActivitySection.module.css）。
  const bottom = top + element.offsetHeight + (Number.parseFloat(getComputedStyle(element).scrollMarginBlockEnd) || 0);
  return bottom > container.scrollTop + container.clientHeight ? bottom - container.clientHeight : null;
}

/**
 * 把元素整個捲進看得到的地方，捲最少（scrollIntoView 的 nearest）：留言框展開後下面被面板的底切掉時用。
 * 和 scrollTargetIntoView 一樣，在面板裡只捲面板、不連帶捲整頁；已經整個看得到就不動。
 */
export function revealInScrollParent(element: HTMLElement, behavior: ScrollBehavior): void {
  const container = scrollParentOf(element);
  if (container === null) {
    if (typeof element.scrollIntoView !== 'function') return;
    element.scrollIntoView({ block: 'nearest', behavior });
    return;
  }
  const destination = revealDestination(element, container);
  if (destination !== null) container.scrollTo({ top: Math.max(0, destination), behavior });
}
