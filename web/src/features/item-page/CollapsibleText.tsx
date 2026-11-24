import { useId, useLayoutEffect, useRef, useState, type RefObject } from 'react';
import { Button } from '../../components/Button/Button';
import { Markdown } from '../../components/Markdown/Markdown';
import { useMentionNames } from './useMentionNames';
import styles from './CollapsibleText.module.css';

/**
 * 收起來的文字是不是被切掉了（內容比收起來的高度高）。只在收起來時量；文字或寬度變了重量。
 * 展開時沿用上一次的結果：展開之後內容一定放得下，量不出原本有沒有超過。沒有 ResizeObserver（jsdom）時只量一次。
 */
function useIsClamped(ref: RefObject<HTMLElement | null>, text: string, isCollapsed: boolean): boolean {
  const [isClamped, setIsClamped] = useState(false);
  useLayoutEffect(() => {
    const element = ref.current;
    if (!isCollapsed || element === null) {
      return undefined;
    }
    const update = () => {
      setIsClamped(element.scrollHeight > element.clientHeight + 1);
    };
    update();
    if (typeof window.ResizeObserver !== 'function') {
      return undefined;
    }
    const observer = new ResizeObserver(update);
    observer.observe(element);
    // 收起來時外層的高度被限住、不會變：說明從純文字換成排好的 Markdown 時，變的是裡面那一層（components/Markdown）。
    if (element.firstElementChild !== null) observer.observe(element.firstElementChild);
    return () => {
      observer.disconnect();
    };
  }, [ref, text, isCollapsed]);
  return isClamped;
}

/**
 * 很長的說明（Markdown 顯示，components/Markdown）：超過 12 行先收起來（CSS 的 line-clamp，換行照舊保留），「顯示全部」展開、「收起」收回。
 * 全文一直在 DOM 裡，報讀軟體照樣唸得到全部。按鈕的文字本身就說了現在的狀態，所以只換文字、不標 aria-expanded
 * （兩個都有會唸成「收起，已展開」；固定名稱加 aria-expanded 又和看得到的字不一樣）；aria-controls 指向那段文字。
 * 收回時按鈕跟著往上移，把它捲回看得到的地方，焦點才不會停在畫面外。
 */
export function CollapsibleText({ text }: { readonly text: string }) {
  const id = useId();
  const textRef = useRef<HTMLDivElement>(null);
  const toggleRef = useRef<HTMLButtonElement>(null);
  const [isExpanded, setIsExpanded] = useState(false);
  const isClamped = useIsClamped(textRef, text, !isExpanded);
  const mentionNames = useMentionNames();

  function toggle() {
    setIsExpanded(!isExpanded);
    const button = toggleRef.current;
    if (isExpanded && button !== null && typeof button.scrollIntoView === 'function') {
      requestAnimationFrame(() => {
        button.scrollIntoView({ block: 'nearest' });
      });
    }
  }

  return (
    <div className={styles.frame}>
      <div ref={textRef} id={id} className={styles.text} data-collapsed={!isExpanded}>
        <Markdown text={text} mentionNames={mentionNames} />
      </div>
      {isClamped && (
        <Button ref={toggleRef} size="sm" variant="ghost" aria-controls={id} onClick={toggle}>
          {isExpanded ? '收起' : '顯示全部'}
        </Button>
      )}
    </div>
  );
}
