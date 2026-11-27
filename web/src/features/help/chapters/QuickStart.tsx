import { Link } from 'react-router';
import { HelpFaq, HelpLead, HelpNote, HelpSection, HelpTable, type HelpTableRow } from '../HelpParts';

const SIDEBAR_ROWS: readonly HelpTableRow[] = [
  {
    key: 'jump',
    name: '搜尋或跳到…',
    description: (
      <>
        打項目編號（例如 <code>WEB-12</code>）、標題、專案或知識庫的頁面，按 Enter 直接過去。快捷鍵是 <kbd>⌘K</kbd>（Windows 是{' '}
        <kbd>Ctrl+K</kbd>）
      </>
    ),
  },
  { key: 'my-work', name: <Link to="/my-work">我的工作</Link>, description: '所有專案裡你負責的項目，旁邊的數字是還沒結束的件數' },
  { key: 'overview', name: <Link to="/overview">專案總覽</Link>, description: '每個專案一列：還沒結束、已過期、快到期、停滯的項目有幾個' },
  { key: 'inbox', name: <Link to="/inbox">收件匣</Link>, description: '你的通知，旁邊的數字是未讀的件數' },
  { key: 'knowledge', name: <Link to="/knowledge">知識庫</Link>, description: '組織與專案的文件' },
  { key: 'projects', name: <Link to="/projects">專案</Link>, description: '你看得到的專案；底下列出各個專案，點了進去' },
  { key: 'people', name: '人員', description: '只有組織的擁有者與管理員看得到' },
  {
    key: 'settings',
    name: <Link to="/account">設定</Link>,
    description: '你自己的帳號、通知、AI 帳號、命令列；管理員另外有組織的設定',
  },
  { key: 'help', name: '使用說明', description: '就是這裡' },
];

function SignIn() {
  return (
    <HelpSection id="sign-in" title="1. 登入" isContentStart>
      <p>Kumi 沒有自己註冊的地方：帳號由組織的擁有者或管理員在「人員」頁建立，他們會給你 Kumi 的網址、你的 Email 與一組初始密碼。</p>
      <p>
        <strong>先改密碼</strong>：初始密碼是別人設的。到 <Link to="/account">設定 → 我的帳號</Link> 的「密碼」，輸入目前的密碼與新密碼（至少 10
        個字元），按「更新密碼」。
      </p>
      <ul>
        <li>改密碼之後，其他裝置上的登入都會登出，這台不會。</li>
        <li>下面有「同時撤銷所有存取權杖」：還沒用過命令列就不用管它。</li>
        <li>忘了密碼的話請管理員在「人員」頁替你重設，Kumi 不寄重設密碼的信。</li>
      </ul>
    </HelpSection>
  );
}

function Layout() {
  return (
    <HelpSection id="layout" title="2. 認識畫面">
      <p>左邊的側欄是主要導覽：</p>
      <HelpTable label="側欄的項目" headers={['項目', '做什麼']} rows={SIDEBAR_ROWS} />
      <p>組織開了 AI（或允許你接自己的 AI 帳號）時，畫面右下角有「AI 助理」按鈕，第 7 節再說。平板直向的寬度裡側欄換成左邊一排圖示，按「專案」打開完整的側欄；手機上是畫面底部的分頁列，其餘的在「更多」裡。</p>
    </HelpSection>
  );
}

function MyWork() {
  return (
    <HelpSection id="my-work" title="3. 看自己的工作：我的工作">
      <p>
        <Link to="/my-work">我的工作</Link>把所有專案裡和你有關的項目放在一起，不用一個專案一個專案找。
      </p>
      <ul>
        <li>
          <strong>範圍</strong>：「我負責的」（預設）或「我建立的」——後者用來追蹤你交代給別人的事。
        </li>
        <li>
          <strong>版面</strong>：表格、看板、行事曆、時間軸四種，看同一批項目。行事曆與時間軸照開始日、到期日排，沒有日期的項目不會出現在上面。
        </li>
        <li>上方可以依專案、狀態、類型篩選（「更多條件」還有別的），也能排序、分組；條件記在網址裡，存成書籤下次就直接是這個樣子。</li>
      </ul>
      <p>點一個項目，寬螢幕上會在旁邊打開項目的面板，清單還在原地；要整頁看就按面板上的「展開成整頁」。</p>
    </HelpSection>
  );
}

function Projects() {
  return (
    <HelpSection id="projects" title="4. 進入專案">
      <p>在側欄的「專案」底下點一個專案。專案頁上方有：</p>
      <ul>
        <li>
          <strong>版面</strong>：表格、看板、行事曆、時間軸，和「我的工作」一樣，只是範圍是這個專案的全部項目。
        </li>
        <li>
          <strong>資訊</strong>：專案的網址、部署環境、主機這類說明。
        </li>
        <li>
          <strong>規劃</strong>：週期與里程碑。
        </li>
        <li>
          <strong>成員</strong>：這個專案有誰、各是什麼角色。
        </li>
        <li>
          <strong>知識庫</strong>、<strong>AI 助理</strong>、<strong>設定</strong>：專案有知識庫空間、組織開了 AI、你的角色看得到專案設定時才有。
        </li>
        <li>
          最右邊「專案的更多動作」的選單：已刪除的項目、匯入項目、重新命名、封存專案，看得到哪幾項依你的權限而定。平板與手機上，「資訊」以外的都收進一顆「專案的動作」。
        </li>
      </ul>
      <p>
        <strong>看不到某個專案</strong>：你要先被加進那個專案的成員才看得到，請專案的管理員把你加進去。
      </p>
      <p>
        <strong>看得到多少項目</strong>看你在專案裡的角色。大部分角色看得到全部；「執行者」只看得到自己負責或建立的項目，數字也只算這些。
      </p>
    </HelpSection>
  );
}

function Items() {
  return (
    <HelpSection id="items" title="5. 建立與修改項目">
      <h3>建立</h3>
      <ul>
        <li>
          <strong>表格</strong>：在表頭下面那一列「新增項目：輸入標題後按 Enter」輸入標題，按 Enter。平板與手機上是按右下角的「新增項目」再輸入。
        </li>
        <li>
          <strong>看板</strong>：每一欄都能直接新增，建好的項目就在那個狀態。
        </li>
      </ul>
      <p>先只寫標題也沒關係，其他的建好之後再補。</p>
      <h3>修改</h3>
      <p>
        打開項目，旁邊（窄螢幕在上面）是欄位：狀態、負責人、類型、優先度、開始日、到期日……點了就改，不用另外按儲存。有哪些欄位、有哪些狀態，每個專案自己設定，所以不同專案看到的不一樣。
      </p>
      <ul>
        <li>
          <strong>改狀態</strong>：在欄位裡選，或在看板上把卡片拖到另一欄。拖不過去、或選單裡沒有那個狀態，代表這個專案的流程不允許這樣走：可能要先經過別的狀態、先填某個欄位，或只有特定的角色能做。
        </li>
        <li>
          <strong>說明</strong>：按「新增說明」或「編輯說明」，寫完按「儲存」。編輯器是所見即所得的，圖片與檔案可以直接貼上或拖進來。寫到一半離開，下次打開會看到「還沒儲存的說明」，不會不見。你編輯時別人也改了說明，Kumi
          會提示「這段說明在你編輯時被別人改過了」：可以「看新的內容」或「繼續編輯」；按儲存時才發現的話，選項是「看新的內容」或「用我的版本覆蓋」。不會悄悄蓋掉別人的。
        </li>
        <li>
          <strong>改類型</strong>、<strong>刪除</strong>在項目上方「更多動作」的選單裡（專案只有一種類型時沒有改類型）。刪掉的項目到專案的「已刪除的項目」能復原。
        </li>
        <li>
          <strong>分享</strong>：按「分享」複製這個項目的連結，貼給別人就能打開（對方要看得到這個專案）。
        </li>
      </ul>
    </HelpSection>
  );
}

function Comments() {
  return (
    <HelpSection id="notifications" title="6. 留言、提及與通知">
      <h3>留言</h3>
      <p>在項目下方的「寫下留言…」輸入，按「送出留言」。</p>
      <ul>
        <li>
          <strong>提及</strong>：打 <kbd>@</kbd> 會列出專案的成員，選一個人，他會收到「提及你」的通知。
        </li>
        <li>
          <strong>回覆</strong>：在某則留言上按回覆，回覆接在那則底下。
        </li>
        <li>
          <strong>附件</strong>：檔案、圖片直接貼上或拖進輸入框。
        </li>
        <li>自己的留言可以編輯，改過的會留下之前的版本；刪除的留言留在原位顯示「已刪除」，可以復原。</li>
      </ul>
      <h3>關注</h3>
      <p>項目上的「關注」決定這個項目有留言、改了狀態時要不要通知你。你建立的、負責的、留過言的、被提及的項目會自動關注；不想再收到就按掉。</p>
      <h3>收件匣</h3>
      <p>
        通知都在<Link to="/inbox">收件匣</Link>：上方可以分「全部」「指派給我」「提及與回覆」，也可以搜尋；點一則就打開那個項目、跳到那則留言，並標成已讀。只有專案的成員會收到那個專案的通知。
      </p>
      <p>
        新通知來的時候畫面角落會跳出提示，幾秒後自己消失。到 <Link to="/account/notifications">設定 → 通知</Link> 可以調：
      </p>
      <ul>
        <li>
          <strong>通知提示</strong>：完整（顯示留言內容）、精簡（只顯示誰做了什麼，開會投影、共用螢幕時用）、不顯示。這個設定只影響這台電腦的瀏覽器。
        </li>
        <li>
          <strong>Email 通知</strong>：通知 5 分鐘後還沒讀才寄，同一段時間的併成一封；不想收信就關掉。組織還沒設定寄信的話，這裡會寫「組織還沒開啟寄信」。
        </li>
      </ul>
    </HelpSection>
  );
}

function Assistant() {
  return (
    <HelpSection id="assistant" title="7. 請 AI 助理幫忙">
      <p>
        按右下角的「AI 助理」打開對話面板。看不到這顆按鈕，代表組織還沒開 AI；管理員允許的話，也可以到{' '}
        <Link to="/account/ai">設定 → 我的 AI 助理</Link>接自己的 AI 帳號。
      </p>
      <ul>
        <li>
          <strong>選專案</strong>：在專案頁會自動選那個專案；面板上方可以換，或選「不選專案」問模板、規劃，請助理建議開新專案。
        </li>
        <li>
          <strong>選項目</strong>：在項目頁會自動帶著那個項目，可以直接問「整理這個項目的重點」。
        </li>
        <li>可以附上檔案，例如會議紀錄、需求文件、截圖。</li>
      </ul>
      <p>可以請它做的事，例如：</p>
      <ul>
        <li>「把這份會議紀錄拆成任務，指派給負責的人，到期日訂在下週五」</li>
        <li>「我這週到期的有哪些？哪些已經過期了？」</li>
        <li>「把 WEB-12 改成進行中，留言說我明天開始做」</li>
      </ul>
      <p>
        <strong>助理不會直接改任何東西。</strong>
        它會在對話裡提出建議，畫成一張卡片，列出要改什麼；你看過按「套用」（新增項目是「全部建立」）才會以你的身分寫入，不要就按「不要」。它能提的建議和你的權限一樣：你自己做不到的事，它也提不出來。
      </p>
      <HelpNote>對話只有你自己看得到。面板上方的「對話」可以回到之前的對話；「開成整頁」換成整頁的版面。</HelpNote>
    </HelpSection>
  );
}

function NextSteps() {
  return (
    <HelpSection id="next" title="8. 接下來">
      <ul>
        <li>
          <strong>
            <Link to="/overview">專案總覽</Link>
          </strong>
          ：負責好幾個專案的人每天看一眼。上方的範圍可以切「只看我管理的」或「全部看得到的」；已過期、停滯的數字點下去就是那幾個項目的清單。
        </li>
        <li>
          <strong>
            <Link to="/knowledge">知識庫</Link>
          </strong>
          ：放不屬於某個項目的文件（流程、規範、交接）。組織的空間成員都看得到，專案的空間看得到專案的人才看得到；頁面可以再限制給特定的人。
        </li>
        <li>
          <strong>命令列與 AI 工具</strong>：<Link to="/account/integrations">設定 → 命令列與整合</Link>
          最上面的設定精靈會給一行指令，裝好命令列工具並接上自己電腦上的 Claude Code 或 Codex。
        </li>
      </ul>
    </HelpSection>
  );
}

function Faq() {
  return (
    <HelpSection id="faq" title="常見問題">
      <HelpFaq question="側欄沒有我要的專案。">你還不是那個專案的成員，請專案的管理員把你加進去。</HelpFaq>
      <HelpFaq question="我看得到專案，卻只看得到幾個項目。">
        你在這個專案的角色只能看自己負責或建立的項目（「執行者」）。需要看全部的話，請管理員調整你的角色。
      </HelpFaq>
      <HelpFaq question="看板上拖不動卡片，或選不到某個狀態。">專案的流程限制了這一步：可能要先經過別的狀態、先填某個欄位，或只有特定的角色能做。</HelpFaq>
      <HelpFaq question="沒收到 Email。">
        確認 <Link to="/account/notifications">設定 → 通知</Link>
        的 Email 通知是開著的，而且你沒有在 5 分鐘內就在網頁上讀了（讀過的不寄）。組織還沒設定寄信時一律不寄。
      </HelpFaq>
      <HelpFaq question="被登出了。">
        你在別的裝置改了密碼、或管理員替你重設了密碼，都會讓這裡的登入失效，用新密碼重新登入就好。帳號被停用的話就登不進來，請找管理員。
      </HelpFaq>
    </HelpSection>
  );
}

/** 快速上手：第一次用 Kumi 的人照著做大約十五分鐘，從登入一路到請 AI 助理幫忙。 */
export function QuickStart() {
  return (
    <>
      <HelpLead>第一次用 Kumi 的人看這一章：從登入開始，一路做到建立項目、留言、看通知、請 AI 助理幫忙。照著做大約十五分鐘。</HelpLead>
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
