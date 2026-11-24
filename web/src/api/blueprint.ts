/**
 * 藍圖文件的型別，照 schemas/blueprint/v0/project-blueprint.schema.json 手寫。
 * Schema 裡不是 required 的屬性一律是選擇性的：後端目前會把空陣列寫出來，但不保證，讀的時候用 `?? []`。
 */

export type StateCategory = 'backlog' | 'not_started' | 'in_progress' | 'done' | 'canceled';

export type FieldType =
  | 'text'
  | 'long_text'
  | 'number'
  | 'currency'
  | 'date'
  | 'single_select'
  | 'multi_select'
  | 'person'
  | 'checkbox'
  | 'url';

export type OptionColor = 'gray' | 'red' | 'orange' | 'yellow' | 'green' | 'blue' | 'purple';

export type Permission =
  | 'item.read'
  /** 只看得到自己負責或建立的項目；同時有 item.read 時看得到全部。 */
  | 'item.read_own'
  | 'item.create'
  | 'item.update'
  | 'item.transition'
  | 'item.delete'
  | 'comment.create'
  | 'blueprint.read'
  | 'blueprint.propose'
  | 'blueprint.apply'
  | 'project.manage';

export interface TemplateReference {
  readonly template: string;
  readonly version: string;
}

export interface BlueprintMetadata {
  readonly key: string;
  readonly name: string;
  readonly description?: string;
  readonly basedOn?: TemplateReference;
}

export interface FieldOption {
  readonly key: string;
  readonly name: string;
  readonly color?: OptionColor;
}

/** 自訂欄位。核心欄位（title、state、assignee、priority、start_date、due_date、estimate）不在這裡。 */
export interface FieldDefinition {
  readonly key: string;
  readonly name: string;
  readonly type: FieldType;
  readonly description?: string;
  /** 只有 single_select 與 multi_select 有。 */
  readonly options?: readonly FieldOption[];
  /** 只有 currency 有，ISO 4217 代碼。 */
  readonly currency?: string;
}

export interface WorkflowState {
  readonly key: string;
  readonly name: string;
  readonly category: StateCategory;
}

export interface WorkflowTransition {
  readonly from: string;
  readonly to: string;
  readonly allowedRoles?: readonly string[];
  readonly requireFields?: readonly string[];
}

export interface Workflow {
  readonly key: string;
  readonly name: string;
  /** 第一個狀態是新項目的起始狀態。 */
  readonly states: readonly WorkflowState[];
  /** 省略時任何兩個狀態之間都可以轉換；有這個屬性時只允許列出的（空陣列是一條都不能轉）。 */
  readonly transitions?: readonly WorkflowTransition[];
}

export interface ItemTypeDefinition {
  readonly key: string;
  readonly name: string;
  readonly workflow: string;
  readonly fields?: readonly string[];
  readonly parents?: readonly string[];
  readonly icon?: string;
}

export type SortDirection = 'asc' | 'desc';

export interface ViewSort {
  readonly field: string;
  readonly direction: SortDirection;
}

export interface ViewFilter {
  readonly itemTypes?: readonly string[];
  readonly states?: readonly string[];
  readonly stateCategories?: readonly StateCategory[];
  readonly assignee?: 'me' | 'unassigned';
}

export interface ViewDefinition {
  readonly key: string;
  readonly name: string;
  readonly type: 'table' | 'board';
  readonly groupBy?: string;
  readonly columns?: readonly string[];
  readonly sort?: readonly ViewSort[];
  readonly filter?: ViewFilter;
  readonly visibleTo?: readonly string[];
}

export interface AutomationTrigger {
  readonly source: 'item' | 'gitlab' | 'github' | 'schedule' | 'webhook';
  readonly event: string;
}

export interface AutomationCondition {
  readonly field: string;
  readonly in: readonly string[];
}

export interface AutomationAction {
  readonly type: 'transition' | 'set_field' | 'notify' | 'webhook';
  readonly to?: string;
  readonly field?: string;
  readonly value?: string;
  readonly message?: string;
  readonly url?: string;
}

export interface Automation {
  readonly key: string;
  readonly name: string;
  readonly enabled?: boolean;
  readonly trigger: AutomationTrigger;
  readonly conditions?: readonly AutomationCondition[];
  readonly actions: readonly AutomationAction[];
}

export interface RoleDefinition {
  readonly key: string;
  readonly name: string;
  readonly permissions: readonly Permission[];
}

export interface Binding {
  readonly connector: 'gitlab' | 'github' | 'webhook';
  readonly resource: string;
}

export interface ProjectBlueprint {
  readonly apiVersion: 'blueprint/v0';
  readonly kind: 'ProjectBlueprint';
  readonly metadata: BlueprintMetadata;
  readonly fields: readonly FieldDefinition[];
  readonly workflows: readonly Workflow[];
  readonly itemTypes: readonly ItemTypeDefinition[];
  readonly views?: readonly ViewDefinition[];
  readonly automations?: readonly Automation[];
  /** 自訂角色。內建的 admin、editor、commenter、viewer 不在這裡。 */
  readonly roles?: readonly RoleDefinition[];
  readonly bindings?: readonly Binding[];
}
