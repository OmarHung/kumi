/**
 * 變更集的資料形狀（從 types.ts 拆出來，types.ts 會轉出這些型別，呼叫端照舊從 types.ts 匯入）。
 */
import type { ApiErrorDetail } from './envelope';
import type { FieldType, ProjectBlueprint } from './blueprint';
import type { Instant, PersonRef } from './types';

/**
 * 變更集，照 internal/HANDOFF-week3-changesets.md 3A.4 節手寫。
 * pending 等待核准；applied 已套用；rejected 已放棄（拒絕）；expired 別的變更集先套用了，基準版本已經不是目前的版本。
 */
export type ChangeSetStatus = 'pending' | 'applied' | 'rejected' | 'expired';

export interface ChangeSet {
  /** 專案裡的流水號，畫面上寫成「#3」，網址也用它。 */
  readonly number: number;
  readonly status: ChangeSetStatus;
  /** 送出時對照的藍圖版本。 */
  readonly baseVersion: number;
  /** 套用之後的藍圖版本；只有已套用的有。 */
  readonly appliedVersion: number | null;
  readonly summary: string | null;
  readonly proposedBy: PersonRef;
  readonly proposedAt: Instant;
  /** 直接在設定頁提出的、AI 助理替人提出的，或外部的 AI 代理（MCP）提出的（核准的人要知道內容是模型寫的）。 */
  readonly origin: 'direct' | 'assistant' | 'agent';
  /** 套用或放棄的人；等待核准與過期的沒有。 */
  readonly decidedBy: PersonRef | null;
  /** 套用、放棄或被標成過期的時刻。 */
  readonly decidedAt: Instant | null;
}

export type ChangeKind = 'added' | 'removed' | 'modified';

export type ChangeResource =
  | 'blueprint'
  | 'metadata'
  | 'field'
  | 'field_option'
  | 'workflow'
  | 'state'
  | 'transition'
  | 'item_type'
  | 'view'
  | 'automation'
  | 'role'
  | 'binding';

/**
 * 藍圖的一個差異。資源用 key 對應：改名是「修改」，換 key 是一個刪除加一個新增。
 * 轉換沒有 key，用「起點->終點」；整合綁定用「連接器:資源」。選項屬於欄位、狀態與轉換屬於流程，parentKey 是所屬的 key。
 */
export interface Change {
  readonly kind: ChangeKind;
  readonly resource: ChangeResource;
  readonly key: string;
  readonly parentKey: string | null;
  /** 改了哪些屬性（文件裡的名字）。清單型的屬性（例如流程的 states）出現在這裡，表示兩版都有的項目順序變了。 */
  readonly changedProperties: readonly string[];
}

interface ImpactBase {
  /**
   * 受影響的數量：狀態搬移、欄位刪除、欄位從類型拿掉與轉換是會被改到的項目數；類型刪除、上層限制縮小是擋住套用的項目數；
   * 角色刪除是還是這個角色的成員數。不是等待核准的變更集一律是 0。
   */
  readonly affected: number;
  /** 這一項讓變更集現在套用不了。 */
  readonly blocksApply: boolean;
}

export type ConversionFailurePolicy = 'leave_empty' | 'abort';

/**
 * 對既有資料的影響，七種。field_detach 是欄位從某個類型拿掉、欄位本身還在：那個類型的項目在這個欄位的值會被封存。
 */
export type Impact = ImpactBase &
  (
    | { readonly kind: 'state_move'; readonly workflow: string; readonly from: string; readonly to: string }
    | { readonly kind: 'field_drop'; readonly field: string }
    | {
        readonly kind: 'field_conversion';
        readonly field: string;
        readonly fromType: FieldType;
        readonly toType: FieldType;
        readonly onFailure: ConversionFailurePolicy | null;
      }
    | { readonly kind: 'item_type_removal'; readonly itemType: string }
    | { readonly kind: 'parent_removal'; readonly itemType: string; readonly parent: string }
    | { readonly kind: 'role_removal'; readonly role: string }
    | { readonly kind: 'field_detach'; readonly itemType: string; readonly field: string }
  );

export interface ChangeSetDetail {
  readonly changeSet: ChangeSet;
  /** 變更集想要的藍圖全文，和專案內容的 blueprint 同一個形狀。 */
  readonly blueprint: ProjectBlueprint;
  /** 基準版本（baseVersion）的藍圖全文，也就是差異的「改之前」。已套用的變更集不能拿專案目前的藍圖來比：那已經是改之後的樣子。 */
  readonly baseBlueprint: ProjectBlueprint;
  /** 相對於基準版本的差異。 */
  readonly changes: readonly Change[];
  /** 對既有資料的影響；只有等待核准的變更集會依目前的資料重算數量。 */
  readonly impacts: readonly Impact[];
  /** 現在套用不了的原因；能套用、或不是等待核准時是空的。 */
  readonly blockers: readonly ApiErrorDetail[];
  /** 等待核准、沒有擋住的原因、而且呼叫者有 blueprint.apply。 */
  readonly canApply: boolean;
}

export interface StateMove {
  readonly workflow: string;
  /** 被刪除的狀態。 */
  readonly from: string;
  /** 項目搬去的狀態，在新藍圖的同一個流程裡。 */
  readonly to: string;
}

/** 會影響既有資料的修改要在這裡寫明怎麼處理，否則計畫會擋下來。 */
export interface ChangeSetMigrations {
  readonly moveStates?: readonly StateMove[];
  /** 確認這些被刪除的欄位的值可以封存。 */
  readonly dropFields?: readonly string[];
}

/** 送出的變更集（schemas/blueprint/v0/changeset.schema.json）：想要的藍圖全文加上資料搬移的指示。 */
export interface ChangeSetRequest {
  /** 這份修改以第幾版為基準；伺服器的目前版本不同時會被擋下。 */
  readonly baseVersion: number;
  /** 給核准者看的一句話說明。 */
  readonly summary?: string;
  readonly blueprint: ProjectBlueprint;
  readonly migrations?: ChangeSetMigrations;
}
