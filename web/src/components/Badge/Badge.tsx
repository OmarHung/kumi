import type { ReactNode } from 'react';
import { cx } from '../../lib/cx';
import { Icon, type IconName } from '../Icon/Icon';
import styles from './Badge.module.css';

/**
 * stateIdle／stateActive／stateDone 是狀態類別（StateBadge 用，顏色來自 --color-state-*）。
 * blue／purple 是藍圖選項的顏色（--color-option-*）：強調色是紫藍，借它畫「藍」的選項會變紫、和「紫」分不出來。
 */
export type BadgeTone =
  | 'neutral'
  | 'accent'
  | 'success'
  | 'warning'
  | 'danger'
  | 'blue'
  | 'purple'
  | 'stateIdle'
  | 'stateActive'
  | 'stateDone';

interface BadgeProps {
  readonly tone?: BadgeTone;
  /** 狀態類別的形狀這類有意思的圖示；純文字的標籤不帶任何前綴。 */
  readonly icon?: IconName;
  readonly children: ReactNode;
}

/** 顏色只是輔助，意思一定要寫在文字裡。 */
export function Badge({ tone = 'neutral', icon, children }: BadgeProps) {
  return (
    <span className={cx(styles.badge, styles[tone])} data-tone={tone}>
      {icon !== undefined && <Icon name={icon} className={styles.icon} />}
      {children}
    </span>
  );
}
