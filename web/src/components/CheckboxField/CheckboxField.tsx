import { useId, type ComponentPropsWithRef, type ReactNode } from 'react';
import { cx } from '../../lib/cx';
import { Icon } from '../Icon/Icon';
import styles from './CheckboxField.module.css';

export type CheckboxFieldProps = Omit<ComponentPropsWithRef<'input'>, 'type' | 'children'> & {
  readonly label: ReactNode;
  /** 勾選框下方的說明，用 aria-describedby 連到勾選框。 */
  readonly hint?: ReactNode;
};

function joinIds(...ids: readonly (string | undefined)[]): string | undefined {
  const present = ids.filter((id) => id !== undefined && id !== '');
  return present.length > 0 ? present.join(' ') : undefined;
}

/**
 * 單一的勾選：原生的勾選框，整個標籤都可以點（點擊範圍至少 --target-min）。方框與勾由 global.css 統一畫（docs/DESIGN.md 7.3）。
 */
export function CheckboxField({ label, hint, className, 'aria-describedby': describedBy, ...inputProps }: CheckboxFieldProps) {
  const hintId = useId();
  return (
    <div className={cx(styles.field, className)}>
      <label className={styles.label}>
        <input
          {...inputProps}
          type="checkbox"
          className={styles.box}
          aria-describedby={joinIds(describedBy, hint === undefined ? undefined : hintId)}
        />
        <span>{label}</span>
      </label>
      {hint !== undefined && (
        <p id={hintId} className={styles.hint}>
          {hint}
        </p>
      )}
    </div>
  );
}

export interface CheckboxGroupProps {
  readonly legend: string;
  /** 標題只給報讀軟體（旁邊已經看得出這一組是什麼時）。 */
  readonly isLegendHidden?: boolean;
  readonly hint?: ReactNode;
  readonly error?: string;
  /** row（預設）：排成一列、放不下才折行；column：一個一行（每個勾選框都有一行說明時，說明才不會和旁邊的勾選框混在一起）。 */
  readonly layout?: 'row' | 'column';
  readonly className?: string;
  readonly children: ReactNode;
}

/** 一組勾選：fieldset 加 legend，說明與錯誤連到這一組。 */
export function CheckboxGroup({ legend, isLegendHidden = false, hint, error, layout = 'row', className, children }: CheckboxGroupProps) {
  const baseId = useId();
  const hintId = hint === undefined ? undefined : `${baseId}-hint`;
  const errorId = error === undefined ? undefined : `${baseId}-error`;
  return (
    <fieldset className={cx(styles.group, className)} aria-describedby={joinIds(hintId, errorId)}>
      <legend className={isLegendHidden ? 'visually-hidden' : styles.legend}>{legend}</legend>
      <div className={styles.options} data-layout={layout}>
        {children}
      </div>
      {hintId !== undefined && (
        <p id={hintId} className={styles.hint}>
          {hint}
        </p>
      )}
      {errorId !== undefined && (
        <p id={errorId} className={styles.error}>
          <Icon name="error" className={styles.errorIcon} />
          {error}
        </p>
      )}
    </fieldset>
  );
}
