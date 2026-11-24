import type { KeyboardEvent } from 'react';
import { hsvToHex } from '../../lib/hexColor';
import { PickerThumb } from './PickerThumb';
import { usePointerRatio } from './usePointerRatio';
import styles from './CustomColorPicker.module.css';

const MAX_HUE = 359;
const BIG_STEP = 10;

/** 色相環上每 60° 一個純色，軌道的漸層照這個順序。 */
const HUE_STOPS = ['#ff0000', '#ffff00', '#00ff00', '#00ffff', '#0000ff', '#ff00ff', '#ff0000'];

function clampHue(value: number): number {
  return Math.min(MAX_HUE, Math.max(0, Math.round(value)));
}

/**
 * 右、上加一度，左、下減一度；Shift 與 PageUp／PageDown 一次 10°，Home、End 到兩端。不繞圈：到了兩端就停。
 * 以畫面上的整數度數為基準：色碼換出來的色相常是小數（359.6），直接加減再取整，按右鍵反而會變小。
 */
function nextByKey(exactHue: number, key: string, isBig: boolean): number | null {
  const hue = clampHue(exactHue);
  const step = isBig ? BIG_STEP : 1;
  switch (key) {
    case 'ArrowRight':
    case 'ArrowUp':
      return clampHue(hue + step);
    case 'ArrowLeft':
    case 'ArrowDown':
      return clampHue(hue - step);
    case 'PageUp':
      return clampHue(hue + BIG_STEP);
    case 'PageDown':
      return clampHue(hue - BIG_STEP);
    case 'Home':
      return 0;
    case 'End':
      return MAX_HUE;
    default:
      return null;
  }
}

interface HueSliderProps {
  readonly hue: number;
  readonly idPrefix: string;
  readonly onChange: (hue: number) => void;
}

/**
 * 色相的軌道：彩虹漸層，圓圈裡是那個色相的純色。到 359° 為止（360° 就是 0°，拖到最右邊時圓圈不會跳回左邊）。
 * 軌道和觸控的目標一樣高（--control-height-sm），畫出來的漸層只佔中間一半。
 */
export function HueSlider({ hue, idPrefix, onChange }: HueSliderProps) {
  const pointer = usePointerRatio(({ x }) => {
    onChange(clampHue(x * 360));
  });
  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    const next = nextByKey(hue, event.key, event.shiftKey);
    if (next === null) return;
    event.preventDefault();
    onChange(next);
  }
  const shown = clampHue(hue);
  const gradient = `${idPrefix}-hue`;
  return (
    <div
      role="slider"
      tabIndex={0}
      aria-label="色相"
      aria-valuemin={0}
      aria-valuemax={MAX_HUE}
      aria-valuenow={shown}
      aria-valuetext={`${String(shown)} 度`}
      className={styles.hue}
      onKeyDown={handleKeyDown}
      {...pointer}
    >
      <svg className={styles.svg} aria-hidden="true" focusable="false">
        <defs>
          <linearGradient id={gradient} x1="0" x2="1" y1="0" y2="0">
            {HUE_STOPS.map((color, index) => (
              <stop key={index} offset={index / (HUE_STOPS.length - 1)} stopColor={color} />
            ))}
          </linearGradient>
        </defs>
        <rect y="25%" width="100%" height="50%" rx="6" fill={`url(#${gradient})`} />
        <PickerThumb cx={`${String((shown / 360) * 100)}%`} cy="50%" fill={hsvToHex({ h: shown, s: 1, v: 1 })} />
      </svg>
    </div>
  );
}
