/**
 * HTTP 合約的資料形狀，照 internal/HANDOFF-week2-web-shell.md 第 5 節手寫。
 * 屬性是 camelCase；列舉是 snake_case 的字串；時刻是 ISO 8601 UTC；日期是 YYYY-MM-DD。
 * 變更集的型別在 changeSetTypes.ts，最後一段從這裡轉出。
 */
import type { Permission, ProjectBlueprint, SortDirection, StateCategory } from './blueprint';

/** ISO 8601 的 UTC 時刻，例如 2026-11-13T08:00:00Z。 */
export type Instant = string;

/** 日曆日期，YYYY-MM-DD。 */
export type CalendarDate = string;

export type OrganizationRole = 'owner' | 'admin' | 'member' | 'guest';

export type Priority = 'none' | 'low' | 'medium' | 'high' | 'urgent';

/** contributor（執行者）：只看得到自己負責或建立的項目（item.read_own），能新增、修改、改狀態、留言，不能刪除。 */
export type BuiltInProjectRole = 'admin' | 'editor' | 'contributor' | 'commenter' | 'viewer';

/** 專案角色的 key：內建的四個之一，或藍圖 roles 裡自訂的。 */
export type ProjectRoleKey = string;

export interface SessionUser {
  readonly personId: string;
  readonly displayName: string;
  readonly email: string;
  readonly organizationId: string;
  readonly organizationName: string;
  readonly role: OrganizationRole;
}

export interface SessionData {
  readonly user: SessionUser;
  /** 用存取權杖呼叫時是那把權杖的範圍（給 kumi 用）；網頁的工作階段一律是 null。 */
  readonly scopes: readonly AccessTokenScope[] | null;
}

export interface SignInRequest {
  readonly email: string;
  readonly password: string;
}

export interface TemplateWorkflowPreview {
  readonly name: string;
  /** 用這個流程的類型名稱。 */
  readonly itemTypes: readonly string[];
  /** 主線的狀態名稱（類別不是 canceled 的），照流程的順序。 */
  readonly states: readonly string[];
  /** states 每個狀態的類別，一一對應：預覽畫狀態的形狀，也用它把「完成」之後的狀態（暫停）和主線分開。 */
  readonly stateCategories: readonly StateCategory[];
  /** 取消類的狀態（不處理、撤回…）：不是流程的最後一步，另外列。 */
  readonly canceledStates: readonly string[];
}

export interface TemplateParents {
  readonly itemType: string;
  /** 可以當它上層的類型名稱。 */
  readonly parents: readonly string[];
}

export interface Template {
  readonly key: string;
  readonly name: string;
  readonly description?: string | null;
  readonly workflows: readonly TemplateWorkflowPreview[];
  /** 有上層的類型與它的上層；沒有就是空陣列。 */
  readonly parents: readonly TemplateParents[];
  /** 自訂欄位的名稱。 */
  readonly fields: readonly string[];
  /** 範例項目的筆數；0 表示沒有範例。 */
  readonly exampleCount: number;
}

export interface Project {
  readonly id: string;
  readonly key: string;
  readonly name: string;
  readonly blueprintVersion: number;
  /** 組織管理員不是專案成員也看得到專案，這時是 null。 */
  readonly myRole: ProjectRoleKey | null;
  readonly createdAt: Instant;
  /**
   * 封存的時刻；沒有封存是 null（PM-4b）。封存的專案只能看：寫入一律 409 `project.archived`，
   * `myPermissions` 只剩讀取與 project.manage（取消封存、改名、管理成員）。
   */
  readonly archivedAt: Instant | null;
}

export interface NewProjectRequest {
  readonly key: string;
  readonly name: string;
  readonly template: string;
  /** 一起建立模板的範例項目；模板沒有範例時後端忽略。 */
  readonly includeExamples?: boolean;
  /** 專案說明的初始內容（說明格式的內容）；不套用格式就不送。 */
  readonly description?: string;
}

export interface Member {
  readonly personId: string;
  readonly displayName: string;
  readonly role: ProjectRoleKey;
}

export interface UpdateMemberRequest {
  readonly role: ProjectRoleKey;
}

/** 移除成員的回應：被移除前的成員資料。 */
export interface RemovedMember {
  readonly personId: string;
  readonly role: ProjectRoleKey;
}

export interface ProjectDetail {
  readonly project: Project;
  readonly blueprint: ProjectBlueprint;
  readonly members: readonly Member[];
  /** 呼叫者在這個專案能做的事，畫面用它決定哪些操作要開放。 */
  readonly myPermissions: readonly Permission[];
}

export interface PersonRef {
  readonly personId: string;
  readonly displayName: string;
}

/**
 * 自訂欄位的值，形狀依欄位型別：文字類與連結是字串，數字與金額是數字，日期是 YYYY-MM-DD 的字串，
 * 單選是選項的 key，多選是選項 key 的陣列，成員是人員編號的字串，勾選是布林。沒有值的欄位不出現。
 */
export type FieldValue = string | number | boolean | readonly string[];

/**
 * 清單裡的項目（項目清單、我的工作）。說明可能很長，清單一律不帶；要說明的話用 ItemDetail。
 */
export interface Item {
  /** 專案代號加流水號，例如 WEB-12。 */
  readonly key: string;
  readonly projectKey: string;
  readonly number: number;
  readonly type: string;
  readonly title: string;
  readonly state: string;
  readonly stateCategory: StateCategory;
  readonly assignee: PersonRef | null;
  readonly priority: Priority;
  readonly startDate: CalendarDate | null;
  readonly dueDate: CalendarDate | null;
  readonly estimate: number | null;
  readonly parentKey: string | null;
  /** 排進的週期與歸屬的里程碑（編號）；沒有是 null。名稱要看專案的週期與里程碑清單（api/planning.ts）。 */
  readonly cycleId: string | null;
  readonly milestoneId: string | null;
  readonly fields: Readonly<Record<string, FieldValue>>;
  readonly createdAt: Instant;
  readonly updatedAt: Instant;
}

/** 單一項目的回應（讀取、新增、修改、改狀態）：多了說明。 */
export interface ItemDetail extends Item {
  /** 純文字，可以有換行；沒有說明是 null。 */
  readonly description: string | null;
  /** 說明內容的版本（雜湊）：改說明時帶著開始編輯時的版本（ItemPatch.expectedDescriptionRevision），別人改過就回 409。 */
  readonly descriptionRevision: string;
}

/** 新增項目。狀態不能指定，一律從流程的第一個狀態開始。 */
export interface NewItemRequest {
  readonly type: string;
  readonly title: string;
  readonly assigneeId?: string | null;
  readonly priority?: Priority;
  readonly startDate?: CalendarDate | null;
  readonly dueDate?: CalendarDate | null;
  readonly estimate?: number | null;
  readonly parentKey?: string | null;
  readonly cycleId?: string | null;
  readonly milestoneId?: string | null;
  readonly description?: string | null;
  readonly fields?: Readonly<Record<string, FieldValue>>;
}

/**
 * 修改項目：沒寫的屬性（undefined）不會送出，表示「不改」；寫 null 表示「清掉」。
 * fields 裡的值是 null 也是清掉那個欄位。類型不能改，狀態要走 transitionItem。
 */
export interface ItemPatch {
  readonly title?: string;
  readonly assigneeId?: string | null;
  readonly priority?: Priority;
  readonly startDate?: CalendarDate | null;
  readonly dueDate?: CalendarDate | null;
  readonly estimate?: number | null;
  readonly parentKey?: string | null;
  readonly cycleId?: string | null;
  readonly milestoneId?: string | null;
  /** null 或空字串是清掉說明。 */
  readonly description?: string | null;
  /** 和 description 一起給時檢查：說明的版本和這個不同（別人改過）就整個不改、回 409 item.description_conflict。 */
  readonly expectedDescriptionRevision?: string;
  readonly fields?: Readonly<Record<string, FieldValue | null>>;
}

export interface TransitionRequest {
  readonly to: string;
}

/** 改類型。狀態不能指定，由伺服器照新流程決定。 */
export interface ChangeTypeRequest {
  readonly type: string;
}

/** 刪除（封存）項目的回應。 */
export interface DeletedItem {
  readonly key: string;
  readonly deletedAt: Instant;
}

export type ItemSortField =
  | 'number'
  | 'title'
  | 'state'
  | 'priority'
  | 'start_date'
  | 'due_date'
  | 'estimate'
  | 'created_at'
  | 'updated_at'
  /** 自訂欄位：`field.<key>`。能排的型別見 domain/fieldFilters.ts 的 SORTABLE_FIELD_TYPES。 */
  | `field.${string}`;

export interface ItemSort {
  readonly field: ItemSortField;
  readonly direction: SortDirection;
}

/** 項目清單的查詢條件。彼此是「而且」；同一個條件裡的多個值是「或」。 */
export interface ItemQuery {
  readonly type?: readonly string[];
  readonly state?: readonly string[];
  readonly stateCategory?: readonly StateCategory[];
  /** `me`、`unassigned` 或人員編號。 */
  readonly assignee?: string;
  /** 只要這些優先度的（`none` 是沒有設定）。 */
  readonly priority?: readonly Priority[];
  /** 到期日在這一天或之後、之前（含當天）；有設時沒有到期日的項目不算符合。 */
  readonly dueFrom?: CalendarDate;
  readonly dueTo?: CalendarDate;
  /** 只要沒有到期日的；不能和 dueFrom、dueTo、datesFrom、datesTo 一起用。 */
  readonly noDue?: boolean;
  /**
   * 項目的期間（開始日到到期日；只有其中一個時就是那一天）和這一段重疊的；兩個日期都沒有的不算。
   * 時間軸用它：畫面上那一段時間裡，有落在其中的項目都要畫。
   */
  readonly datesFrom?: CalendarDate;
  readonly datesTo?: CalendarDate;
  /** 只要排在這個週期的（週期的編號；「目前的週期」已由呼叫端換成編號）。 */
  readonly cycleId?: string;
  /** 只要沒有週期的。不能和 cycleId 一起用。 */
  readonly noCycle?: boolean;
  readonly milestoneId?: string;
  readonly noMilestone?: boolean;
  readonly q?: string;
  /**
   * 自訂欄位的條件，送成 `field.<key>=值`（同一個欄位多個值重複帶，是「或」；不同欄位是「而且」）。
   * 值：選項的 key、人員編號或 `me`、`true`／`false`；`_none` 是沒有填。只有這個專案的清單收。
   */
  readonly fields?: Readonly<Record<string, readonly string[]>>;
  /** 只要這幾個專案（代號）的：只有跨專案的清單（`GET /v1/items`，listItemsAcrossProjects）收，最多 20 個。 */
  readonly project?: readonly string[];
  /** 只要這個項目（項目編號）的直接下層。 */
  readonly parent?: string;
  readonly sort?: ItemSort;
  readonly limit?: number;
  readonly cursor?: string;
}

export interface ItemPage {
  readonly items: readonly Item[];
  /** 還有下一頁時的游標；沒有了就是 null。 */
  readonly nextCursor: string | null;
}

export interface MyWork {
  readonly items: readonly Item[];
}

/**
 * 看得到、沒有封存的專案用到的一種項目類型（`GET /v1/item-types`）。同一個 key 在不同專案可能叫不同的名字，依 key 合併。
 * 跨專案的「類型」篩選送的是 key。
 */
export interface ItemTypeSummary {
  readonly key: string;
  /** 各專案的名稱，去掉重複，照第一次出現的順序（專案依代號排）。 */
  readonly names: readonly string[];
  /** 用到這個類型的專案代號，照代號排。 */
  readonly projectKeys: readonly string[];
}

export interface Person {
  readonly personId: string;
  readonly displayName: string;
  readonly email: string | null;
  readonly role: OrganizationRole;
  /**
   * 帳號被停用了：不能登入，但照樣在人員清單裡，工作項目與活動也照樣顯示他的名字。沒有帳號的人是 false
   * （internal/HANDOFF-security-local-ops.md 第 2 節第 3 點）。
   */
  readonly disabled: boolean;
}

/** 登出其他裝置的回應：因此作廢的其他工作階段數（0 是沒有別的登入）。 */
export interface SignedOutSessions {
  readonly signedOutSessions: number;
}

/** 登出其他裝置的請求：目前的密碼（確認是本人）。 */
export interface SignOutOtherSessionsRequest {
  readonly currentPassword: string;
}

/** 改自己的密碼的回應：作廢了幾個其他的登入、撤銷了幾把個人存取權杖（沒有勾撤銷時是 0）。 */
export interface PasswordChanged extends SignedOutSessions {
  readonly revokedAccessTokens: number;
}

/** 個人存取權杖的範圍（internal/HANDOFF-pat-cli.md 第 4 節）：對外的名稱是合約，不能改。 */
/** agent:run：當本機代理（kumi agent start），領網頁 AI 助理的訊息、回報回覆。 */
export type AccessTokenScope = 'items:read' | 'items:write' | 'blueprint:read' | 'blueprint:write' | 'agent:run';

/** 一把存取權杖（清單上看得到的樣子）：沒有明文，只有開頭幾個字。 */
export interface AccessToken {
  readonly tokenId: string;
  readonly name: string;
  readonly displayPrefix: string;
  readonly scopes: readonly AccessTokenScope[];
  readonly createdAt: Instant;
  readonly lastUsedAt: Instant | null;
  /** null 是不過期；已經過期的照樣在清單裡（標「已過期」）。 */
  readonly expiresAt: Instant | null;
  /** 從那台電腦的 kumi 瀏覽器登入建立的（internal/HANDOFF-setup-wizard.md 第 3 節）才有電腦名稱；網頁上自己建的是 null。 */
  readonly machineName: string | null;
  /** 建立它的那台電腦或瀏覽器的位址（伺服器看到的）；舊的權杖是 null。 */
  readonly createdFrom: string | null;
}

/** 建立存取權杖：期限 1～365 天（本機代理的最長 90 天），一定要有天數；伺服器不收不過期的。 */
export interface CreateAccessTokenRequest {
  readonly name: string;
  readonly scopes: readonly AccessTokenScope[];
  readonly expiresInDays: number;
}

/** 建立的回應：明文只有這一次拿得到，不存進任何快取。 */
export interface CreatedAccessToken {
  readonly token: AccessToken;
  readonly secret: string;
}

/** 改自己的密碼：要先輸入目前的密碼。新密碼和目前的一樣也接受。 */
export interface ChangePasswordRequest {
  readonly currentPassword: string;
  readonly newPassword: string;
  /** 同時撤銷所有個人存取權杖；不給當作 true（網頁一律明確送出）。 */
  readonly revokeAccessTokens?: boolean;
}

/** 組織的擁有者或管理員替別人設一組新密碼（自己的不行，要用 ChangePasswordRequest）。 */
export interface ResetPasswordRequest {
  readonly newPassword: string;
}

export interface SetPersonRoleRequest {
  readonly role: OrganizationRole;
}

/** 網頁回報的例外是哪一種：window 的 error、沒有人接的 Promise 失敗、路由的錯誤頁（畫面出錯）。 */
export type ClientErrorKind = 'error' | 'unhandled_rejection' | 'render';

/** POST /v1/client-errors 的內文。message 1～1000 字、stack 最多 4000 字、path 是以 / 開頭的路徑（不帶查詢字串）。 */
export interface ClientErrorReport {
  readonly kind: ClientErrorKind;
  readonly message: string;
  readonly stack: string | null;
  readonly path: string;
}

export interface NewPersonRequest {
  readonly displayName: string;
  readonly email: string;
  readonly password: string;
  readonly role: OrganizationRole;
}

/**
 * migrated：套用藍圖的變更集時，項目的資料被搬移（狀態搬到另一個狀態、欄位被刪除封存或換了型別）。
 * type_changed：改了類型（changes 第一筆是 item_type 的前後值，接著是封存的自訂欄位；狀態跟著搬了時有 fromState、toState）。
 * deleted：項目被刪除；刪除之後項目是 404，API 讀不到這一筆，只是列在這裡讓型別完整。
 */
export type ActivityKind =
  | 'created'
  | 'updated'
  | 'transitioned'
  | 'commented'
  | 'migrated'
  | 'type_changed'
  | 'deleted'
  | 'restored'
  | 'attachment_added'
  | 'attachment_removed';

/**
 * 一次修改裡一個欄位的前後值。field 是核心欄位的名字（title、description、assignee、priority、
 * start_date、due_date、estimate、parent、cycle、milestone）或自訂欄位的 key。值的形狀和項目裡的一樣：負責人與成員欄位是
 * 人員編號、上層項目是項目編號、週期與里程碑是它們的編號、優先度是 snake_case（none 算沒有值）、日期是 YYYY-MM-DD。
 * 沒有值是 null；說明與長文字欄位只記「有改」，兩邊一律是 null。
 */
export interface FieldChange {
  readonly field: string;
  readonly from: FieldValue | null;
  readonly to: FieldValue | null;
}

/**
 * 項目的一筆活動。沒有的屬性是 null（changes 只有 updated、migrated 與 type_changed 有；其他種類畫面不讀它）。
 * migrated 的搬移內容放在 fromState、toState（狀態搬移）與 changes（欄位刪除與轉換；刪除的 to 是 null）。
 */
export interface Activity {
  readonly id: string;
  readonly kind: ActivityKind;
  /** 查不到的人，後端用固定的替代文字當 displayName。 */
  readonly actor: PersonRef;
  readonly at: Instant;
  readonly changes: readonly FieldChange[];
  /** 狀態的 key，只有 transitioned、migrated 與 type_changed（狀態搬移時）有。type_changed 的 fromState 在舊類型的流程裡。 */
  readonly fromState: string | null;
  readonly toState: string | null;
  /** 留言的內容（Markdown）；只有 commented 有。已刪除的留言是 null（內容藏起來，復原之後才回來）。 */
  readonly comment: string | null;
  /** 只有回覆有：它回覆的那一則最上層留言（回覆只有一層）。最上層的留言與其他活動是 null。 */
  readonly replyToId: string | null;
  /** 從別的系統搬過來的歷史：來源的名字（例如 `monday`），時間軸寫「從 monday 匯入」；作者與時間是原本的。這裡建立的是 null。 */
  readonly importedFrom: string | null;
  /** 只有 migrated 有：套用之後的藍圖版本。 */
  readonly blueprintVersion: number | null;
  /** 只有編輯過的留言有：最後一次編輯的時間；時間軸上的位置（at）不變。 */
  readonly editedAt: Instant | null;
  /** 只有刪除（藏起來）的留言有：刪除的時間與誰刪的；復原之後都是 null。 */
  readonly deletedAt: Instant | null;
  readonly deletedBy: PersonRef | null;
}

/** 一則留言的一個版本（GET .../revisions，新的在前，第一筆是現在的內容）。 */
export interface CommentRevision {
  readonly number: number;
  readonly body: string;
  /** 這個版本寫下的時間（現在的版本是最後一次編輯的時間）。 */
  readonly at: Instant;
  readonly isCurrent: boolean;
}

export interface CommentRevisionList {
  readonly items: readonly CommentRevision[];
}

/** 活動由舊到新排列；nextCursor 是更新的那一頁的游標，沒有了是 null。 */
export interface ActivityPage {
  readonly items: readonly Activity[];
  readonly nextCursor: string | null;
}

export interface ActivityQuery {
  readonly limit?: number;
  readonly cursor?: string;
  /** 只要這一種活動（網址的 `kind`）；沒給是全部。「留言」用 `commented`，回覆也是 `commented`。 */
  readonly kind?: ActivityKind;
}

export interface NewCommentRequest {
  readonly body: string;
  /** 要回覆的留言（活動的編號）。回覆一則回覆等於回覆它的那一則最上層留言；找不到是 404 comment.not_found。 */
  readonly replyTo?: string;
}

export interface EditCommentRequest {
  readonly body: string;
}

export type {
  ChangeSetStatus,
  ChangeSet,
  ChangeKind,
  ChangeResource,
  Change,
  ConversionFailurePolicy,
  Impact,
  ChangeSetDetail,
  StateMove,
  ChangeSetMigrations,
  ChangeSetRequest,
} from './changeSetTypes';

export type {
  NotificationKind,
  Notification,
  NotificationPage,
  NotificationQuery,
  UnreadCount,
  UpdatedCount,
  SubscriptionReason,
  ItemSubscription,
} from './notificationTypes';

/** 儲存的檢視（個人的）：存起來的版面與條件，不複製資料。到期存的是「本週」這種相對的說法，不是日期。 */
export type SavedViewLayout = 'table' | 'board' | 'calendar' | 'timeline';

/** 和網址上的篩選同一套（domain/itemFilters.ts）；沒設的條件是 null。搜尋文字在 JSON 叫 q，和網址一樣。 */
export interface SavedViewQuery {
  readonly type: string | null;
  readonly state: string | null;
  readonly assignee: string | null;
  readonly q: string | null;
  /** `<欄位>:asc|desc`，和網址的 sort 一樣。 */
  readonly sort: string | null;
  readonly due: string | null;
  readonly priority: string | null;
  /** `current`（存「目前的」不存編號：過了一個週期，打開還是目前的那一個）、`none` 或週期編號。 */
  readonly cycle: string | null;
  /** `none` 或里程碑編號。 */
  readonly milestone: string | null;
  /** 自訂欄位的條件：key 對到值，和網址的 `field.<key>` 同一種寫法。沒有條件時是 null。 */
  readonly fields: Readonly<Record<string, string>> | null;
  /**
   * 只有「我的工作」用：狀態類別的條件。null 是還沒結束的（預設）；`all` 是所有狀態；或一個類別。和 state 只能有一個。
   * 「我的工作」的 assignee 也不同：null 是「我」（預設），`all` 是所有人，另外是 `unassigned` 或人員編號。
   */
  readonly category: string | null;
  /** 只有「我的工作」用：只看這個專案（代號）。state 只有在有 project 時才有意義。 */
  readonly project: string | null;
  /** 分組。專案的檢視是看板的：`assignee`、`field.<key>`，依狀態（預設）時是 null；「我的工作」是表格的：`project`，依急迫程度（預設）時是 null。 */
  readonly group: string | null;
}

/** 新增與整個換掉用的內文。 */
export interface SavedViewDraft {
  readonly name: string;
  readonly layout: SavedViewLayout;
  readonly query: SavedViewQuery;
}

export interface SavedView extends SavedViewDraft {
  readonly id: string;
  readonly createdAt: Instant;
  readonly updatedAt: Instant;
}

/** 專案說明（一份 Markdown，GET／PUT /v1/projects/{key}/description）。沒寫過時三個都是 null。 */
export interface ProjectDescription {
  readonly text: string | null;
  readonly updatedAt: Instant | null;
  readonly updatedBy: PersonRef | null;
}

/** 說明格式：開始寫專案說明時填進去的 Markdown 架構（GET /v1/description-templates）。 */
export interface DescriptionTemplate {
  readonly id: string;
  readonly name: string;
  readonly body: string;
  readonly isBuiltIn: boolean;
  readonly isDefault: boolean;
}

/** 新增與修改說明格式用的內文（POST／PUT /v1/description-templates）。 */
export interface DescriptionTemplateInput {
  readonly name: string;
  readonly body: string;
  readonly isDefault: boolean;
}

/** 專案搜尋的一筆（GET /v1/search/projects）：代號或名稱就比對到的 snippet 是 null，否則是說明裡那一行的摘錄。 */
export interface ProjectSearchHit {
  readonly project: Project;
  readonly snippet: string | null;
}
