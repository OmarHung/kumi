import type { DescriptionTemplate } from '../../api/types';
import { List, ListRow } from '../../components/List/List';
import { TableScroll, tableStyles } from '../../components/Table/Table';
import { TemplateActions, type TemplateActionHandlers } from './TemplateActions';
import { TemplateName } from './TemplateName';
import styles from './DescriptionTemplates.module.css';

interface ViewProps {
  readonly templates: readonly DescriptionTemplate[];
  readonly handlers: TemplateActionHandlers;
}

/** 寬螢幕：名稱（加標籤）與最後一欄的動作。第一列標上 data-content-start，給 scripts/ui-audit.js 量（docs/DESIGN.md 6.2）。 */
export function DescriptionTemplateTable({ templates, handlers }: ViewProps) {
  return (
    <TableScroll label="說明格式" layout="page">
      <table className={tableStyles.table}>
        <caption className="visually-hidden">說明格式</caption>
        <thead>
          <tr>
            <th scope="col">名稱</th>
            <th scope="col">
              <span className="visually-hidden">動作</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {templates.map((template, index) => (
            <tr key={template.id} data-content-start={index === 0 || undefined}>
              <td className={tableStyles.primaryCell}>
                <TemplateName template={template} />
              </td>
              <td className={styles.actionCell}>
                <TemplateActions template={template} {...handlers} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </TableScroll>
  );
}

/** 窄螢幕：改成清單（docs/DESIGN.md 6.3）。靜態的一筆，動作放在右邊。 */
export function DescriptionTemplateCompactList({ templates, handlers }: ViewProps) {
  return (
    <List label="說明格式">
      {templates.map((template, index) => (
        <ListRow
          key={template.id}
          title={<TemplateName template={template} />}
          titleText={template.name}
          trailing={<TemplateActions template={template} {...handlers} />}
          isContentStart={index === 0}
        />
      ))}
    </List>
  );
}
