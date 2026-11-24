import { Button } from '../../components/Button/Button';
import styles from './Assistant.module.css';

export const EXAMPLES: readonly string[] = ['把驗收拆成內部驗收和客戶驗收', '幫我看設定有沒有用不到的欄位或狀態', '加一個「客戶」單選欄位'];
/** 選了項目時的例子。 */
export const ITEM_EXAMPLES: readonly string[] = ['整理這個項目的重點', '草擬一則回覆留言', '這個項目下一步該做什麼'];

/** 沒有對話或還沒送過訊息時的說明與三個例子。按例子是填進輸入框，不直接送：讓人先看過、改過再送。 */
export function EmptyIntro({ onPick, hasItem }: { readonly onPick: (text: string) => void; readonly hasItem: boolean }) {
  return (
    <section className={styles.intro} aria-label="AI 助理的說明" data-content-start>
      <p className={styles.introText}>
        描述你想怎麼改這個專案的設定，助理會讀目前的設定、算出影響，提出變更集讓你核准。選了項目時，助理也能讀那個項目、建議怎麼改（你按「套用」才會改）。它不會直接改設定。
      </p>
      <ul className={styles.examples} aria-label="例子">
        {(hasItem ? ITEM_EXAMPLES : EXAMPLES).map((example) => (
          <li key={example}>
            <Button
              size="sm"
              onClick={() => {
                onPick(example);
              }}
            >
              {example}
            </Button>
          </li>
        ))}
      </ul>
    </section>
  );
}
