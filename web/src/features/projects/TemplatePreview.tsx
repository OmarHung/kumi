import { Fragment } from 'react';
import type { Template, TemplateWorkflowPreview } from '../../api/types';
import { Icon } from '../../components/Icon/Icon';
import { cx } from '../../lib/cx';
import { StateBadge } from '../work-items/ItemCells';
import { hierarchyChains, workflowParts, type HierarchyChain, type PreviewState } from './templatePreviewParts';
import styles from './TemplatePreview.module.css';

interface TemplatePreviewProps {
  readonly id: string;
  readonly template: Template;
}

/** 報讀軟體才唸的標點：畫面上靠圖示與間距分開，唸的時候要有停頓。 */
function Spoken({ text }: { readonly text: string }) {
  return <span className="visually-hidden">{text}</span>;
}

/**
 * 主線的狀態是和清單上一樣的狀態標籤（形狀跟著類別），用箭頭接起來。箭頭和它前面的標籤包在同一個 li 裡、不換行，
 * 換行只發生在 li 之間，箭頭不會落在行首；箭頭 aria-hidden，報讀時狀態之間唸「、」。
 */
function StateChain({ main, others }: { readonly main: readonly PreviewState[]; readonly others: readonly PreviewState[] }) {
  return (
    <ul className={styles.chain}>
      {main.map((state, index) => {
        const isLast = index === main.length - 1;
        return (
          <li key={state.name} className={cx(styles.step, isLast && others.length > 0 && styles.beforeOthers)}>
            <StateBadge label={state.name} category={state.category} />
            {!isLast && <Icon name="chevronRight" className={styles.arrow} />}
            {!isLast && <Spoken text="、" />}
          </li>
        );
      })}
      {others.length > 0 && (
        <li className={styles.others}>
          <Spoken text="；" />
          <span className={styles.othersLabel}>另外</span>
          <Spoken text="：" />
          {others.map((state, index) => (
            <span key={state.name} className={styles.step}>
              {index > 0 && <Spoken text="、" />}
              <StateBadge label={state.name} category={state.category} />
            </span>
          ))}
        </li>
      )}
    </ul>
  );
}

function WorkflowBlock({ workflow }: { readonly workflow: TemplateWorkflowPreview }) {
  const { main, others } = workflowParts(workflow);
  return (
    <div className={styles.workflow}>
      <p className={styles.types}>
        {workflow.itemTypes.join('、')}
        <Spoken text="：" />
      </p>
      <StateChain main={main} others={others} />
      <Spoken text="。" />
    </div>
  );
}

/**
 * 上下層一棵一行：最上層的類型，後面每一層前面是「放在前一個底下」的圖示（報讀時唸「底下是」）。
 * 最上層有好幾種時是「其中之一」，用「／」隔開、報讀唸「或」：「、」在流程那一行是「這幾種都用」，同一個符號兩種意思會看錯。
 * 一層一行的縮排樹比較直觀，但接案模板就要三行，對話框在 900 高的畫面會超出去、「建立」要捲才看得到。
 */
function Hierarchy({ chains }: { readonly chains: readonly HierarchyChain[] }) {
  return chains.map((chain, index) => (
    <p key={`${chain.roots.join('、')}-${String(index)}`} className={styles.tree}>
      {index > 0 && <Spoken text="；" />}
      <span className={styles.typeName}>
        {chain.roots.map((root, rootIndex) => (
          <Fragment key={root}>
            {rootIndex > 0 && <span aria-hidden="true">／</span>}
            {rootIndex > 0 && <Spoken text="或" />}
            {root}
          </Fragment>
        ))}
      </span>
      {chain.levels.map((type, depth) => (
        <span key={`${type}-${String(depth)}`} className={styles.level}>
          <Icon name="subItem" className={styles.branch} />
          <Spoken text="，底下是" />
          <span className={styles.typeName}>{type}</span>
        </span>
      ))}
    </p>
  ));
}

/**
 * 選到的模板有哪些類型、流程的狀態、上下層與欄位，畫成看得出結構的樣子（使用者 2026-11-20 嫌原本一行一行的字太像文章）：
 * 狀態是清單上的狀態標籤、上下層是縮排的樹、欄位是標籤。用 aria-describedby 接在模板欄位上：報讀軟體停在欄位時就唸得到，
 * 不必另外摸過去。不能加 role 與 aria-label：aria-label 會蓋掉內容，描述就只剩標籤那幾個字。
 * 內容多寡不同，高度會跟著換；它在按鈕上面，按鈕會跟著動，這是有意的取捨（docs/design/08-interaction.md）。
 */
export function TemplatePreview({ id, template }: TemplatePreviewProps) {
  const hasParents = template.parents.length > 0;
  const hasFields = template.fields.length > 0;
  if (template.workflows.length === 0 && !hasFields && !hasParents) {
    return null;
  }
  return (
    <div id={id} className={styles.preview}>
      {template.workflows.map((workflow) => (
        <WorkflowBlock key={workflow.name} workflow={workflow} />
      ))}
      {(hasParents || hasFields) && (
        <dl className={styles.facts}>
          {hasParents && (
            <>
              <dt className={styles.factLabel}>上下層<Spoken text="：" /></dt>
              <dd className={styles.factValue}>
                <Hierarchy chains={hierarchyChains(template.parents)} />
                <Spoken text="。" />
              </dd>
            </>
          )}
          {hasFields && (
            <>
              <dt className={styles.factLabel}>欄位<Spoken text="：" /></dt>
              <dd className={styles.factValue}>
                <ul className={styles.tags}>
                  {template.fields.map((field, index) => (
                    <li key={field} className={styles.tag}>
                      {index > 0 && <Spoken text="、" />}
                      {field}
                    </li>
                  ))}
                </ul>
              </dd>
            </>
          )}
        </dl>
      )}
    </div>
  );
}
