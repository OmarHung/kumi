import type { ComponentPropsWithRef } from 'react';
import { cx } from '../../lib/cx';
import styles from './Button.module.css';

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';
export type ButtonSize = 'md' | 'sm';

/** 讓 Link 也能長得像按鈕（例如「回到專案清單」），樣式只維護一份。 */
export function buttonClassName(
  variant: ButtonVariant = 'secondary',
  size: ButtonSize = 'md',
  className?: string,
): string {
  return cx(styles.button, styles[variant], styles[size], className);
}

type ButtonProps = ComponentPropsWithRef<'button'> & {
  readonly variant?: ButtonVariant;
  readonly size?: ButtonSize;
};

export function Button({
  variant = 'secondary',
  size = 'md',
  type = 'button',
  className,
  ...rest
}: ButtonProps) {
  return <button type={type} className={buttonClassName(variant, size, className)} {...rest} />;
}
