# 藍圖：格式與修改的規則

藍圖是一個專案的設定，格式第 0 版（`apiVersion: blueprint/v0`）。`kumi blueprint export` 匯出的檔案是**變更集**：

```yaml
# project: WEB
baseVersion: 12            # 以藍圖第幾版為基準；不要自己改
blueprint:                 # 想要的藍圖全文
  apiVersion: blueprint/v0
  kind: ProjectBlueprint
  metadata: { key: website_redesign, name: 官網改版 }
  fields: [...]
  workflows: [...]
  itemTypes: [...]
  views: [...]
  automations: [...]
  roles: [...]
  bindings: [...]
migrations: {...}          # 只有會動到既有資料時才需要
summary: 一句話說明       # 也可以用 apply --summary 給
```

送出的是全文，差異由伺服器對照 `baseVersion` 算。所以沒有要改的部分照原樣留著；從檔案裡刪掉的東西會被當成要刪除。

在 Kumi 的 repo 裡，格式的唯一依據是 `schemas/blueprint/v0/`（`project-blueprint.schema.json`、`changeset.schema.json` 與 `examples/`）。不在 repo 裡時，以這份文件與 `kumi blueprint validate` 的訊息為準。

## 目錄

- 共同規則
- 各區塊的寫法
- 會動到既有資料的修改（migrations）
- 常見的修改
- 檢查與套用

## 共同規則

- **key** 是不會變的識別字：小寫英文字母開頭，只能有小寫字母、數字與底線，2 到 40 個字元（`^[a-z][a-z0-9_]{1,39}$`）。顯示名稱（`name`）可以改，key 不能改：改 key 等於刪掉舊的、加一個新的。
- 每個物件只能有格式裡列出的屬性，多寫一個不認得的屬性也會被擋。
- `name`、選項名稱可以用中文；key 一律英文。替新東西取 key 時，用簡短的英文描述（`legal_review`、`client_name`），同一份藍圖裡不能重複。

## 各區塊的寫法

### fields：自訂欄位

```yaml
fields:
  - { key: quote_amount, name: 報價金額, type: currency, currency: TWD }
  - { key: client_signoff_date, name: 客戶驗收日, type: date }
  - key: impact
    name: 影響程度
    type: single_select
    options:
      - { key: low,  name: 低, color: gray }
      - { key: high, name: 高, color: red }
```

- `type`：`text`、`long_text`、`number`、`currency`、`date`、`single_select`、`multi_select`、`person`、`checkbox`、`url`。
- `single_select`、`multi_select` 必須有 `options`，其他型別不能有。選項的 `color`：`gray`、`red`、`orange`、`yellow`、`green`、`blue`、`purple`。
- `currency` 型別要給 `currency`（ISO 4217 代碼，例如 `TWD`）。
- 欄位要在 `itemTypes[].fields` 列出來，那種項目才有這個欄位。

### workflows：流程

```yaml
workflows:
  - key: main
    name: 主要流程
    states:
      - { key: todo,  name: 待辦,   category: not_started }   # 第一個是新項目的起始狀態
      - { key: doing, name: 進行中, category: in_progress }
      - { key: done,  name: 完成,   category: done }
    transitions:
      - { from: todo,  to: doing }
      - { from: doing, to: done, allowedRoles: [project_manager], requireFields: [client_signoff_date] }
```

- 狀態的 `category` 只有五個：`backlog`、`not_started`、`in_progress`、`done`、`canceled`。「我的工作」與跨專案的清單靠類別運作，選錯類別，項目會出現在錯的地方（例如做完的被當成已過期）。
- **第一個狀態是新項目的起始狀態**，調整順序時留意。
- 沒有 `transitions` 時，任何兩個狀態之間都能互相轉換；有列出時，只允許列出的轉換。加了新狀態卻沒加轉換，項目就進不去也出不來，`validate` 會擋下：每個狀態都要能從起始狀態走到（`workflow.unreachable_state`），不是 `done`、`canceled` 類的狀態都要有轉換可以離開（`workflow.dead_end_state`）。
- `allowedRoles`：只有這些角色能做這個轉換。`requireFields`：做這個轉換前必須先填好的欄位。

### itemTypes：項目類型

```yaml
itemTypes:
  - { key: requirement, name: 需求, workflow: delivery, fields: [quote_amount] }
  - { key: task, name: 任務, workflow: task_flow, parents: [requirement] }
```

- `workflow` 指向 `workflows[].key`。`fields` 列出這種項目有的自訂欄位。`parents` 是可以掛在哪些類型底下，省略表示只能放在最上層。

### views：檢視

```yaml
views:
  - { key: progress_board, name: 進度看板, type: board, groupBy: state }
  - key: requirement_table
    name: 需求清單
    type: table
    columns: [title, state, assignee, quote_amount, due_date]
    sort: [{ field: due_date, direction: asc }]
    filter: { itemTypes: [requirement], stateCategories: [not_started, in_progress], assignee: me }
    visibleTo: [project_manager]
```

- `type`：`table` 或 `board`。`groupBy`：`state`、`assignee`、`item_type`，或一個 `single_select` 欄位的 key。
- `filter` 只有 `itemTypes`、`states`、`stateCategories`、`assignee`（`me` 或 `unassigned`）四種，彼此是「而且」。`visibleTo` 省略表示專案成員都看得到。

### roles：專案角色

```yaml
roles:
  - key: project_manager
    name: 專案經理
    permissions: [item.read, item.create, item.update, item.transition, comment.create, blueprint.read, blueprint.propose, blueprint.apply]
```

權限：`item.read`、`item.create`、`item.update`、`item.transition`、`item.delete`、`comment.create`、`blueprint.read`、`blueprint.propose`、`blueprint.apply`、`project.manage`。拿掉某個角色的權限，擁有這個角色的人會立刻失去那個能力；改角色之前，先跟使用者確認影響到誰。

### automations、bindings

自動化（`trigger`、`conditions`、`actions`）與外部連結（`gitlab`、`github`、`webhook`）照匯出的原樣保留即可。使用者明確要改它們時，照 `schemas/blueprint/v0/project-blueprint.schema.json` 的定義寫，憑證不放在藍圖裡。

## 會動到既有資料的修改（migrations）

拿掉還有項目的狀態、刪除欄位、改欄位型別，都要在變更集的 `migrations` 寫明怎麼處理；少寫的話，`plan` 會擋下來並列出缺什麼。

```yaml
migrations:
  moveStates:                              # 被刪除的狀態底下的項目要搬去哪個狀態
    - { workflow: main, from: qa, to: internal_qa }
  dropFields: [old_budget]                 # 確認這些被刪除的欄位可以封存（資料保留 30 天）
  convertFields:                           # 欄位改型別時，轉不過去的值怎麼處理
    - { field: estimate_text, onFailure: leave_empty }   # 或 abort：有轉不過去的就整個不套用
```

這些都會改到別人的資料：搬去哪個狀態、刪哪個欄位、轉不過去的值要清空還是放棄，**由使用者決定**。先問，不要替他選；`plan` 的影響（幾個項目）一併給他看。

## 常見的修改

**加一個狀態**：在 `states` 裡插到合適的位置，選對 `category`，再補進出的 `transitions`（流程有列 `transitions` 時）。不會動到既有資料。

**把一個狀態拆成兩個**：加兩個新狀態、拿掉舊的，補轉換，並在 `migrations.moveStates` 寫明舊狀態的項目搬去哪一個。完整的例子是 repo 的 `schemas/blueprint/v0/examples/split-acceptance.changeset.yaml`（基準是同資料夾的 `website-redesign.blueprint.yaml`）。

**拿掉或取代一個狀態**（例如把「驗收」換成「法務審查」）：

1. 在 `states` 加新的狀態（如果有），拿掉舊的。
2. 拿掉所有 `from` 或 `to` 是舊狀態的 `transitions`，補上經過新狀態的轉換。舊轉換上的 `allowedRoles`、`requireFields` 不會自動移到新的轉換上；原本有「只有專案經理能結案」這類限制時，問使用者新的轉換要不要照舊。
3. 找出其他還引用舊狀態 key 的地方一起改：`views[].filter.states`、`automations[].conditions`（`field: state` 的 `in`）、`automations[].actions` 的 `to`。漏掉的話，`kumi blueprint validate` 在本機就會以結束代碼 2 列出路徑（例如 `transition.unknown_state`）。
4. 舊狀態底下還有項目時，在 `migrations.moveStates` 寫搬去哪裡。去向由使用者決定；你可以建議，但要說是建議。
5. 原本有退回的轉換（例如驗收退回進行中），問使用者新的流程還要不要能退回。

**加一個單選欄位**：在 `fields` 加欄位與 `options`，再把 key 加到要用它的 `itemTypes[].fields`。

**加一個選項**：在那個欄位的 `options` 加一筆。拿掉已經有項目在用的選項會動到資料，`plan` 會列出影響。

**改顯示名稱**：只改 `name`，key 不動。不會動到資料。

## 檢查與套用

1. `kumi blueprint validate -f 檔案`：只在本機檢查格式，不連伺服器。有問題時照訊息裡的路徑修正，修到通過。
2. `kumi blueprint plan -f 檔案`：伺服器算出差異、影響與擋住的原因，什麼都不存。`--json` 的欄位：`baseVersion`、`currentVersion`、`blueprint`、`changes`、`impacts`、`blockers`、`canApply`。結束代碼：
   - 0：沒有影響任何項目，也沒有被擋住。
   - 2：文件不合規定（例如引用了不存在的 key）。照訊息修正。
   - 3：會影響既有的項目，或有擋住的原因（`blockers`，例如少寫 `migrations`、藍圖的版本變了）。`migrations` 寫對了，只要有項目被搬動，照樣是 3。這不是錯誤，是要人看過再決定的訊號。`blockers` 是空的、`canApply` 是 `true` 時，就只差使用者點頭。
3. 把 `plan` 的結果用使用者看得懂的話講一次：改了什麼、影響幾個項目、有沒有被擋住。
4. 使用者同意後才 `kumi blueprint apply -f 檔案 --summary "一句話"`。結束代碼 3 時不要加 `--yes` 重送，除非使用者看過那份影響並明確說要套用。
5. 套用成功會印出新的藍圖版本。使用者沒有核准的權限時是「已送出 #N，等待核准」：照實說還沒生效，由有權限的人在網頁或用 `kumi changeset apply N --project 代號` 核准。
6. 送出成功、套用卻失敗時，訊息會寫「已送出 #N，還沒套用」。把原因告訴使用者，由他決定要用 `kumi changeset apply N --project 代號` 再試，還是 `kumi changeset reject N --project 代號` 拒絕。
