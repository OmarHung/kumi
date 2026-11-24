import type { ReactNode } from 'react';
import { PageBar } from '../PageBar/PageBar';
import { PageLayout } from '../PageLayout/PageLayout';

export interface StatusPageProps {
  /** 瀏覽器分頁的標題（例如還在載入的專案代號）。 */
  readonly title: string;
  readonly children: ReactNode;
}

/**
 * 頁面還沒有內容可以顯示時（載入中、讀不到、找不到、沒有權限）的外框：照樣有頁列，
 * 每一頁的外框才一致，疊在頁列左上角的按鈕也不會蓋住內容。頁列上沒有 h1：有的話由內容自己給（錯誤頁的標題）。
 */
export function StatusPage({ title, children }: StatusPageProps) {
  return (
    <PageLayout variant="reading" bar={<PageBar title={title} titleInBar={false} />}>
      {children}
    </PageLayout>
  );
}
