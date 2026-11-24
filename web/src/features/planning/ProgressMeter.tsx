import type { ItemProgress } from '../../api/planning';
import styles from './Planning.module.css';

interface ProgressMeterProps {
  /** 進度條的名稱：「<名稱> 的完成進度」。 */
  readonly name: string;
  readonly progress: ItemProgress;
}

/**
 * 完成幾個、估計多少，加一條進度條。進度條是原生的 <progress>（完成數對要做的數量，隱含的角色就是 progressbar），不是裝飾：
 * 理由：寬度是執行時才算得出來的數值，但規範 13 節不准用 style 屬性，原生元素的 value 與 max 就是這個數值。顏色只是輔助，數字一定寫在旁邊。
 * 取消的項目不算進分母（它們不用做，算進去 100% 永遠到不了），另外寫「・取消 N」；沒有項目、或全部取消時沒有東西要做，不畫條。
 */
export function ProgressMeter({ name, progress }: ProgressMeterProps) {
  if (progress.total === 0) return <p className={styles.noItems}>還沒有項目</p>;
  const planned = progress.total - progress.canceled;
  if (planned === 0) return <p className={styles.noItems}>全部取消</p>;
  const canceled = progress.canceled > 0 ? `・取消 ${String(progress.canceled)}` : '';
  const counts = `完成 ${String(progress.done)}／${String(planned)}${canceled}`;
  const hasEstimate = progress.estimateTotal !== null;
  return (
    <div className={styles.progress}>
      <p className={styles.counts}>
        <span>{counts}</span>
        {hasEstimate && <span>{`估計 ${String(progress.estimateDone ?? 0)}／${String(progress.estimateTotal)}`}</span>}
      </p>
      <progress className={styles.track} aria-label={`${name} 的完成進度`} aria-valuetext={counts} max={planned} value={progress.done} />
    </div>
  );
}
