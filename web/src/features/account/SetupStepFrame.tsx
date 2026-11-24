import { useEffect, useId, useRef, type ReactNode } from 'react';
import { Button } from '../../components/Button/Button';
import { Icon } from '../../components/Icon/Icon';
import styles from './SetupWizard.module.css';

export type StepState = 'upcoming' | 'current' | 'done';

interface SetupStepFrameProps {
  readonly number: number;
  readonly title: string;
  readonly state: StepState;
  /** 完成的步驟收成一行：做了什麼。 */
  readonly summary?: string;
  /** 完成的步驟被點開重看。 */
  readonly isReopened: boolean;
  readonly onToggle: () => void;
  /** 上一步剛完成、輪到這一步：焦點放到標題（按的按鈕隨上一步收起而消失，焦點不能掉到 body）。 */
  readonly shouldFocus: boolean;
  readonly children: ReactNode;
}

const STATE_TEXT: Readonly<Record<StepState, string>> = {
  upcoming: '（還沒到這一步）',
  current: '',
  done: '（已完成）',
};

/**
 * 設定精靈的一步（步驟卡，docs/design/08-interaction.md「命令列與整合」）：標題一行，輪到它才展開內容；
 * 還沒到的只有淡色的標題；完成的收成一行摘要加「重看」。同一頁由上往下排，所以展開只會往下長，不推動上面的內容。
 * 狀態用文字與圖示（勾、序號）表達，不只靠顏色。
 */
export function SetupStepFrame({ number, title, state, summary, isReopened, onToggle, shouldFocus, children }: SetupStepFrameProps) {
  const headingRef = useRef<HTMLHeadingElement>(null);
  const bodyId = useId();
  const isOpen = state === 'current' || isReopened;
  useEffect(() => {
    if (shouldFocus) headingRef.current?.focus();
  }, [shouldFocus]);
  return (
    <li className={styles.step} data-state={state}>
      <div className={styles.header}>
        <span className={styles.marker} aria-hidden="true">
          {state === 'done' ? <Icon name="check" size={16} /> : number}
        </span>
        <div className={styles.titles}>
          {/* tabIndex=-1：程式可以把焦點放過來，Tab 不停在標題上。 */}
          <h3 ref={headingRef} className={styles.title} tabIndex={-1}>
            {title}
            <span className="visually-hidden">{STATE_TEXT[state]}</span>
          </h3>
          {state === 'done' && summary !== undefined && <p className={styles.summary}>{summary}</p>}
        </div>
        {state === 'done' && (
          <Button
            variant="ghost"
            size="sm"
            aria-expanded={isOpen}
            aria-controls={isOpen ? bodyId : undefined}
            aria-label={`${isOpen ? '收起' : '重看'}：${title}`}
            onClick={onToggle}
          >
            {isOpen ? '收起' : '重看'}
          </Button>
        )}
      </div>
      {isOpen && (
        <div id={bodyId} className={styles.body}>
          {children}
        </div>
      )}
    </li>
  );
}
