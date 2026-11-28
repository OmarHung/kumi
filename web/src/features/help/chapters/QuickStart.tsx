import { Link } from 'react-router';
import { HelpLead, HelpNote, HelpSection } from '../HelpParts';
import { FaqItem, IconList, StepFlow, Tiles, UiButton } from '../HelpVisuals';
import { ProjectBarMock, ProposalMock, ScreenMap } from './QuickStartFigures';

/*
 * 快速上手：每一節先放一張示意圖或步驟流程，文字只留一兩行（使用者 2026-11-28 要「少字、多圖」）。
 * 寫到的按鈕、頁面名稱要和畫面一致：改了畫面上的名稱或流程，這裡一起改（CLAUDE.md）。
 */

function SignIn() {
  return (
    <HelpSection id="sign-in" title="1. 登入" isContentStart>
      <StepFlow
        label="第一次登入的步驟"
        steps={[
          { key: 'get', icon: 'people', title: '拿到帳號', detail: '管理員給你網址、Email 與初始密碼' },
          { key: 'sign-in', icon: 'account', title: '登入', detail: '打開網址，輸入 Email 與密碼' },
          {
            key: 'password',
            icon: 'resetPassword',
            title: '改密碼',
            detail: (
              <>
                <Link to="/account">設定 → 我的帳號</Link>，至少 10 個字元
              </>
            ),
          },
        ]}
      />
      <HelpNote>忘了密碼：請管理員在「人員」頁替你重設（Kumi 不寄重設密碼的信）。</HelpNote>
    </HelpSection>
  );
}

function Layout() {
  return (
    <HelpSection id="layout" title="2. 認識畫面">
      <ScreenMap />
      <IconList
        label="不同螢幕的導覽"
        items={[
          { key: 'tablet', icon: 'sidebar', children: '平板：左邊一排圖示，按「專案」打開完整的側欄' },
          { key: 'phone', icon: 'more', children: '手機：底部的分頁列（AI 助理也在這裡），其餘的在「更多」裡' },
        ]}
      />
    </HelpSection>
  );
}

function MyWork() {
  return (
    <HelpSection id="my-work" title="3. 看自己的工作：我的工作">
      <p>
        <Link to="/my-work">我的工作</Link>：所有專案裡和你有關的項目，切換 <UiButton>我負責的</UiButton> 或 <UiButton>我建立的</UiButton>。
      </p>
      <Tiles
        label="四種版面"
        tiles={[
          { key: 'table', icon: 'table', title: '表格', hint: '一列一個，排序、分組' },
          { key: 'board', icon: 'board', title: '看板', hint: '依狀態分欄，拖曳改狀態' },
          { key: 'calendar', icon: 'calendar', title: '行事曆', hint: '依到期日排在月曆上' },
          { key: 'timeline', icon: 'timeline', title: '時間軸', hint: '開始日到到期日的長條' },
        ]}
      />
      <HelpNote>點一個項目會在旁邊打開；篩選的條件記在網址裡，存成書籤下次直接是這個樣子。</HelpNote>
    </HelpSection>
  );
}

function Projects() {
  return (
    <HelpSection id="projects" title="4. 進入專案">
      <ProjectBarMock />
      <IconList
        label="專案頁上方的按鈕"
        items={[
          { key: 'info', icon: 'info', children: '資訊：專案的網址、環境、主機' },
          { key: 'planning', icon: 'calendar', children: '規劃：週期與里程碑' },
          { key: 'members', icon: 'members', children: '成員：誰在這個專案、什麼角色' },
          { key: 'more', icon: 'more', children: '更多：已刪除的項目、匯入、重新命名、封存（依你的權限）' },
        ]}
      />
      <IconList
        label="看不到的時候"
        items={[
          { key: 'missing', icon: 'lock', children: '側欄沒有那個專案：請專案的管理員把你加成成員' },
          { key: 'own', icon: 'account', children: '「執行者」只看得到自己負責或建立的項目' },
        ]}
      />
    </HelpSection>
  );
}

function Items() {
  return (
    <HelpSection id="items" title="5. 建立與修改項目">
      <StepFlow
        label="建立與修改項目的步驟"
        steps={[
          { key: 'create', icon: 'add', title: '輸入標題，按 Enter', detail: '表頭下的第一列；平板與手機按右下角的「新增項目」' },
          { key: 'fields', icon: 'edit', title: '點欄位就改', detail: '狀態、負責人、到期日……不用另外存' },
          { key: 'move', icon: 'board', title: '看板拖到下一欄', detail: '狀態跟著換' },
        ]}
      />
      <IconList
        label="其他常用的操作"
        items={[
          {
            key: 'description',
            icon: 'fileDocument',
            children: (
              <>
                說明：<UiButton icon="edit">編輯說明</UiButton> 寫完按 <UiButton>儲存</UiButton>，圖片直接貼上
              </>
            ),
          },
          { key: 'conflict', icon: 'warning', children: '別人同時在改：會提示，不會悄悄蓋掉' },
          { key: 'flow', icon: 'stateInProgress', children: '拖不過去：流程要先經過別的狀態、先填欄位，或要特定角色' },
          {
            key: 'share',
            icon: 'share',
            children: (
              <>
                <UiButton icon="share">分享</UiButton> 複製連結，貼給別人就能打開
              </>
            ),
          },
          { key: 'delete', icon: 'restore', children: '刪錯了：專案的「已刪除的項目」能復原' },
        ]}
      />
    </HelpSection>
  );
}

function Comments() {
  return (
    <HelpSection id="notifications" title="6. 留言、提及與通知">
      <StepFlow
        label="提及之後的通知"
        steps={[
          {
            key: 'mention',
            icon: 'mention',
            title: '留言打 @ 選人',
            detail: (
              <>
                按 <UiButton>送出留言</UiButton>
              </>
            ),
          },
          { key: 'inbox', icon: 'notifications', title: '對方的收件匣', detail: '畫面角落也跳出提示' },
          { key: 'mail', icon: 'mail', title: '5 分鐘沒讀', detail: '寄 Email，同一段時間併成一封' },
        ]}
      />
      <IconList
        label="通知的設定"
        items={[
          { key: 'follow', icon: 'following', children: '關注：建立、負責、留言、被提及的項目會自動關注，不想收就按掉' },
          {
            key: 'inbox',
            icon: 'notifications',
            children: (
              <>
                <Link to="/inbox">收件匣</Link>：全部／指派給我／提及與回覆
              </>
            ),
          },
          {
            key: 'settings',
            icon: 'settings',
            children: (
              <>
                <Link to="/account/notifications">設定 → 通知</Link>：提示要完整、精簡或不顯示，Email 開或關
              </>
            ),
          },
        ]}
      />
    </HelpSection>
  );
}

function Assistant() {
  return (
    <HelpSection id="assistant" title="7. 請 AI 助理幫忙">
      <StepFlow
        label="AI 助理怎麼幫你改東西"
        steps={[
          { key: 'ask', icon: 'sparkles', title: '說你要做什麼', detail: '可以附上會議紀錄、截圖' },
          { key: 'card', icon: 'fileDocument', title: '助理提出建議卡', detail: '列出要改什麼' },
          {
            key: 'apply',
            icon: 'check',
            title: '你按「套用」才會改',
            detail: '以你的身分、照你的權限',
          },
        ]}
      />
      <ProposalMock />
      <IconList
        label="可以這樣問"
        items={[
          { key: 'split', icon: 'quote', children: '「把這份會議紀錄拆成任務，指派給負責的人，下週五到期」' },
          { key: 'due', icon: 'quote', children: '「我這週到期的有哪些？哪些過期了？」' },
          { key: 'move', icon: 'quote', children: '「把 WEB-12 改成進行中，留言說我明天開始做」' },
        ]}
      />
      <HelpNote>
        找不到「AI 助理」：組織還沒開 AI。管理員允許的話，可以到 <Link to="/account/ai">設定 → 我的 AI 助理</Link>接自己的帳號。
      </HelpNote>
    </HelpSection>
  );
}

function NextSteps() {
  return (
    <HelpSection id="next" title="8. 接下來">
      <Tiles
        label="接下來可以看的頁面"
        tiles={[
          { key: 'overview', icon: 'overview', title: '專案總覽', hint: '每個專案過期、停滯的有幾個', to: '/overview' },
          { key: 'knowledge', icon: 'knowledge', title: '知識庫', hint: '流程、規範、交接文件', to: '/knowledge' },
          { key: 'integrations', icon: 'link', title: '命令列與整合', hint: '接上 Claude Code、Codex', to: '/account/integrations' },
        ]}
      />
    </HelpSection>
  );
}

function Faq() {
  return (
    <HelpSection id="faq" title="常見問題">
      <FaqItem question="側欄沒有我要的專案。">你還不是那個專案的成員，請專案的管理員把你加進去。</FaqItem>
      <FaqItem question="我看得到專案，卻只看得到幾個項目。">你的角色只能看自己負責或建立的項目（「執行者」）；要看全部請管理員調整角色。</FaqItem>
      <FaqItem question="看板上拖不動卡片，或選不到某個狀態。">流程限制了這一步：可能要先經過別的狀態、先填某個欄位，或只有特定的角色能做。</FaqItem>
      <FaqItem question="沒收到 Email。">
        確認 <Link to="/account/notifications">設定 → 通知</Link>的 Email 開著；5 分鐘內在網頁上讀過的不寄，組織還沒設定寄信時也不寄。
      </FaqItem>
      <FaqItem question="被登出了。">別的裝置改了密碼、或管理員重設了密碼，用新密碼重新登入；帳號被停用就請找管理員。</FaqItem>
    </HelpSection>
  );
}

/** 快速上手：第一次用 Kumi 的人照著做大約十五分鐘，從登入一路到請 AI 助理幫忙。 */
export function QuickStart() {
  return (
    <>
      <HelpLead>第一次用 Kumi？照著這一章做，大約十五分鐘。</HelpLead>
      <SignIn />
      <Layout />
      <MyWork />
      <Projects />
      <Items />
      <Comments />
      <Assistant />
      <NextSteps />
      <Faq />
    </>
  );
}
