import type { KeyboardEvent } from 'react';
import { hsvToHex, type Hsv } from '../../lib/hexColor';
import { PickerThumb } from './PickerThumb';
import { usePointerRatio } from './usePointerRatio';
import styles from './CustomColorPicker.module.css';

const STEP = 0.01;
const BIG_STEP = 0.1;

/** 夾在 0～1，取到小數兩位：連按方向鍵時不會累積浮點數的誤差（0.7000000000000001）。 */
function level(value: number): number {
  return Math.round(Math.min(1, Math.max(0, value)) * 100) / 100;
}

/** 左右是飽和度、上下是亮度；Shift 與 PageUp／PageDown 一次 10%，Home、End 到飽和度的兩端。 */
function nextByKey(hsv: Hsv, key: string, isBig: boolean): Hsv | null {
  const step = isBig ? BIG_STEP : STEP;
  switch (key) {
    case 'ArrowRight':
      return { ...hsv, s: level(hsv.s + step) };
    case 'ArrowLeft':
      return { ...hsv, s: level(hsv.s - step) };
    case 'ArrowUp':
      return { ...hsv, v: level(hsv.v + step) };
    case 'ArrowDown':
      return { ...hsv, v: level(hsv.v - step) };
    case 'PageUp':
      return { ...hsv, v: level(hsv.v + BIG_STEP) };
    case 'PageDown':
      return { ...hsv, v: level(hsv.v - BIG_STEP) };
    case 'Home':
      return { ...hsv, s: 0 };
    case 'End':
      return { ...hsv, s: 1 };
    default:
      return null;
  }
}

function percent(value: number): number {
  return Math.round(value * 100);
}

interface SaturationAreaProps {
  readonly hsv: Hsv;
  /** SVG 漸層的 id 前綴（一頁裡要唯一）。 */
  readonly idPrefix: string;
  readonly onChange: (next: Hsv) => void;
}

/**
 * 取色器的方塊：底色是目前色相的純色，疊上「左白右透明」與「上透明下黑」兩層漸層；圓圈在 (飽和度, 1 − 亮度)。
 * 二維的值沒有對應的 ARIA 角色，照常見的做法當成一個 slider：數值是飽和度，aria-valuetext 兩個都唸，
 * 最後加上組出來的色碼（光聽百分比想像不出是什麼顏色）。
 * 整塊用 SVG 畫、位置用百分比：不用 style 屬性（內容安全政策與樣式檢查），也不必量寬度。
 */
export function SaturationArea({ hsv, idPrefix, onChange }: SaturationAreaProps) {
  const pointer = usePointerRatio(({ x, y }) => {
    onChange({ ...hsv, s: level(x), v: level(1 - y) });
  });
  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    const next = nextByKey(hsv, event.key, event.shiftKey);
    if (next === null) return;
    event.preventDefault();
    onChange(next);
  }
  const white = `${idPrefix}-white`;
  const black = `${idPrefix}-black`;
  return (
    <div
      role="slider"
      tabIndex={0}
      aria-label="飽和度與亮度"
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={percent(hsv.s)}
      aria-valuetext={`飽和度 ${String(percent(hsv.s))}%、亮度 ${String(percent(hsv.v))}%，${hsvToHex(hsv)}`}
      className={styles.area}
      onKeyDown={handleKeyDown}
      {...pointer}
    >
      <svg className={styles.svg} aria-hidden="true" focusable="false">
        <defs>
          <linearGradient id={white} x1="0" x2="1" y1="0" y2="0">
            <stop offset="0" stopColor="#fff" />
            <stop offset="1" stopColor="#fff" stopOpacity="0" />
          </linearGradient>
          <linearGradient id={black} x1="0" x2="0" y1="0" y2="1">
            <stop offset="0" stopColor="#000" stopOpacity="0" />
            <stop offset="1" stopColor="#000" />
          </linearGradient>
        </defs>
        <rect width="100%" height="100%" rx="6" fill={hsvToHex({ h: hsv.h, s: 1, v: 1 })} />
        <rect width="100%" height="100%" rx="6" fill={`url(#${white})`} />
        <rect width="100%" height="100%" rx="6" fill={`url(#${black})`} />
        <PickerThumb cx={`${String(hsv.s * 100)}%`} cy={`${String((1 - hsv.v) * 100)}%`} fill={hsvToHex(hsv)} />
      </svg>
    </div>
  );
}
