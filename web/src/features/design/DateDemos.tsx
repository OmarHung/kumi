import { useState } from 'react';
import { DateField, type DateFieldProps } from '../../components/DatePicker/DateField';
import { DatePicker } from '../../components/DatePicker/DatePicker';
import { Demo } from './DemoSection';
import styles from './Design.module.css';

type DemoDateFieldProps = Omit<DateFieldProps, 'value' | 'onChange'> & { readonly initial: string };

/** 展示用的日期欄位：自己記著值，選了、打了就換。 */
function DemoDateField({ initial, ...props }: DemoDateFieldProps) {
  const [value, setValue] = useState(initial);
  return <DateField {...props} value={value} onChange={setValue} />;
}

function DemoCellDate({ label, initial }: { readonly label: string; readonly initial: string }) {
  const [value, setValue] = useState(initial);
  return <DatePicker aria-label={label} variant="bare" size="sm" value={value} onChange={setValue} />;
}

/** DatePicker 的每一種狀態（docs/DESIGN.md 7.3「日期」）：空的、有值、有錯、停用、唯讀、月份與年份畫面、工具列的 sm、格子裡的 bare。 */
export function DateDemos() {
  return (
    <>
      <Demo name="DateField（DatePicker）">
        <DemoDateField label="到期日" hint="點輸入框打開日曆，也可以直接打 2026/12/15；↓ 把焦點移進日曆。" initial="2026-12-15" />
        <DemoDateField label="開始日" initial="" />
        <DemoDateField label="驗收日" error="驗收日不能早於開始日。" initial="2026-11-02" />
        <DemoDateField label="建立日" hint="停用：打不開。" disabled initial="2026-11-13" />
        <DemoDateField label="檢查日" hint="唯讀：看得到值，打不開日曆。" readOnly initial="2026-11-20" />
      </Demo>
      <Demo name="DatePicker 的月份與年份畫面">
        <DemoDateField
          label="交付日"
          hint="點日曆的標題「2028 年 2 月」換到月份畫面，再點「2028 年」換到年份畫面；選了月份、年份回到上一層，Esc 也是。選的（實心）和今天所在的（環線）分開看得到。"
          initial="2028-02-29"
        />
      </Demo>
      <Demo name="DateField size=sm（工具列）">
        <div className={styles.selectRow}>
          <DemoDateField label="到期日（工具列）" size="sm" isLabelHidden initial="" className={styles.inlineField} />
        </div>
      </Demo>
      <Demo name="DatePicker variant=bare（格子裡）">
        {/* 模擬編輯中的格子：框由格子畫，日期欄位本身沒有外框、底色與焦點光暈，撐滿格子（規範 7.3）。 */}
        <span className={styles.cellFrame}>
          <DemoCellDate label="格子裡的到期日" initial="2026-12-15" />
        </span>
        <span className={styles.cellFrame}>
          <DemoCellDate label="格子裡的開始日" initial="" />
        </span>
      </Demo>
    </>
  );
}
