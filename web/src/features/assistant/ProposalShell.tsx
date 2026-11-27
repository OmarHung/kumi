import type { UseQueryResult } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { errorMessage } from '../../api/http';
import { Notice } from '../../components/Notice/Notice';
import styles from './ProposalCard.module.css';

interface ProposalShellProps<T> {
  readonly query: UseQueryResult<T>;
  /** 「週期與里程碑建議」：讀不到、載入中的文字都用它。 */
  readonly noun: string;
  readonly children: (proposal: T) => ReactNode;
}

/** 新種類的建議卡片共用的載入與讀不到：背景重讀失敗時 data 還在，有資料就照舊顯示。 */
export function ProposalShell<T>({ query, noun, children }: ProposalShellProps<T>) {
  if (query.data === undefined) {
    if (query.isError) return <Notice tone="warning">{`讀不到這份${noun}：${errorMessage(query.error)}`}</Notice>;
    return <p className={styles.loading}>{`正在載入${noun}…`}</p>;
  }
  return <>{children(query.data)}</>;
}
