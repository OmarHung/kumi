import { Component, lazy, Suspense, type ReactNode } from 'react';
import type { MarkdownProps } from './MarkdownContent';
import styles from './Markdown.module.css';

// react-markdown 與它的外掛有一百多 kB：只有真的要顯示說明或留言的頁面才下載，主程式檔不變大。
const MarkdownContent = lazy(() => import('./MarkdownContent').then((module) => ({ default: module.MarkdownContent })));

/**
 * 下載好之前顯示的純文字：換行與縮排照原樣，內容一開始就在，下載好之後才換成排好的 Markdown
 * （換掉的是同一段文字，報讀軟體唸的字不變，也不會先出現空白再補上）。
 */
export function PlainText({ text }: { readonly text: string }) {
  return (
    <div className={styles.markdown} data-state="plain">
      {text}
    </div>
  );
}

export type { MarkdownProps };

interface LoadBoundaryProps {
  readonly fallback: ReactNode;
  readonly children: ReactNode;
}

/**
 * 延後載入失敗時（重新部署之後，舊分頁要的檔案已經不在了）繼續顯示純文字：說明與留言還是看得到，
 * 不讓整頁掉進錯誤頁。重新整理之後會載入新的檔案。
 */
class MarkdownLoadBoundary extends Component<LoadBoundaryProps, { readonly hasFailed: boolean }> {
  override state = { hasFailed: false };

  static getDerivedStateFromError() {
    return { hasFailed: true };
  }

  override render() {
    return this.state.hasFailed ? this.props.fallback : this.props.children;
  }
}

/** 說明與留言的 Markdown 顯示。 */
export function Markdown(props: MarkdownProps) {
  return (
    // 外面這一層一直在：純文字換成排好的 Markdown 時它的高度會變，呼叫端（說明的「顯示全部」）觀察它的大小。
    <div>
      <MarkdownLoadBoundary fallback={<PlainText text={props.text} />}>
        <Suspense fallback={<PlainText text={props.text} />}>
          <MarkdownContent {...props} />
        </Suspense>
      </MarkdownLoadBoundary>
    </div>
  );
}
