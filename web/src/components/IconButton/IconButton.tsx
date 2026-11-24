import type { ComponentPropsWithRef } from 'react';
import { Link, type To } from 'react-router';
import { cx } from '../../lib/cx';
import { buttonClassName } from '../Button/Button';
import { Icon, type IconName } from '../Icon/Icon';
import styles from './IconButton.module.css';

export type IconButtonProps = Omit<ComponentPropsWithRef<'button'>, 'children' | 'aria-label' | 'title'> & {
  readonly icon: IconName;
  /** 同時是 aria-label 與 title：報讀軟體唸它，滑鼠滑過也看得到。 */
  readonly label: string;
  /** 滑鼠滑過時看到的說明（title），比名稱長時用；沒給就是 label。名稱還是 label。 */
  readonly description?: string;
  readonly variant?: 'ghost' | 'secondary';
  readonly size?: 'md' | 'sm';
};

/** 只有圖示的按鈕（關閉、更多、上移、下移…）。外觀沿用 Button 的變化，寬度和高度一樣。 */
export function IconButton({
  icon,
  label,
  description,
  variant = 'ghost',
  size = 'md',
  type = 'button',
  className,
  ...rest
}: IconButtonProps) {
  return (
    <button
      type={type}
      aria-label={label}
      title={description ?? label}
      data-size={size}
      className={buttonClassName(variant, size, cx(styles.iconButton, className))}
      {...rest}
    >
      <Icon name={icon} />
    </button>
  );
}

export interface IconLinkProps {
  readonly to: To;
  /** 連結帶著的 navigate state（例如回去時要到的清單）。 */
  readonly state?: unknown;
  readonly icon: IconName;
  /** 同時是 aria-label 與 title。 */
  readonly label: string;
  readonly variant?: 'ghost' | 'secondary';
  readonly size?: 'md' | 'sm';
  readonly className?: string;
}

/**
 * 長得和 IconButton 一樣的連結（例如項目面板的「展開成整頁」）：要的是換頁（⌘／Ctrl 點開新分頁、複製連結），
 * 所以是連結不是按鈕；名稱、尺寸、外觀和 IconButton 同一套。
 */
export function IconLink({ to, state, icon, label, variant = 'ghost', size = 'md', className }: IconLinkProps) {
  return (
    <Link
      to={to}
      state={state}
      aria-label={label}
      title={label}
      data-size={size}
      className={buttonClassName(variant, size, cx(styles.iconButton, className))}
    >
      <Icon name={icon} />
    </Link>
  );
}
