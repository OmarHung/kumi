import type { DescriptionTemplate } from '../../api/types';
import { Badge } from '../../components/Badge/Badge';
import styles from './DescriptionTemplates.module.css';

/** 名稱與後面的標籤：預設、內建。標籤的文字才是意思，不只靠顏色；表格與窄螢幕的清單共用。 */
export function TemplateName({ template }: { readonly template: DescriptionTemplate }) {
  return (
    <span className={styles.name}>
      <span>{template.name}</span>
      {template.isDefault && <Badge tone="accent">預設</Badge>}
      {template.isBuiltIn && <Badge>內建</Badge>}
    </span>
  );
}
