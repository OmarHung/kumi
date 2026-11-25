namespace PmTool.Api;

/// <summary>API 這一層的上限。宣告成 public 是為了讓測試專案能引用同一個數字。</summary>
public static class ApiLimits
{
    // 和藍圖模組的 DocumentLimits.MaxCharacters 相同。那個類別是 internal，不為了 API 打開它，所以在這裡另外寫一份。
    public const int MaxDocumentCharacters = 262_144;

    // 依交接文件以 4 倍換算。超過字元上限但沒超過位元組上限的內文，會交給驗證器回報 document.too_large。
    public const int MaxUtf8BytesPerCharacter = 4;

    public const int MaxDocumentBytes = MaxDocumentCharacters * MaxUtf8BytesPerCharacter;

    public const int RateLimitPermitsPerWindow = 60;

    public const int RateLimitWindowSeconds = 60;

    // 沒登入的請求，同一個來源同時進行的請求數。固定視窗允許在視窗一開始就連發 60 次，沒有這個上限的話，
    // 一個來源可以把 MaxConcurrentValidations 個位置全部占住大半分鐘。
    public const int MaxConcurrentRequestsPerClient = 4;

    // 兩個驗證端點，每個呼叫者（已登入依人、沒登入依來源）同時最多幾個。已登入的人一般同時可以有
    // MaxConcurrentRequestsPerPerson 個請求，剛好等於全部的驗證位置，一個帳號就占得滿；
    // 驗證端點另外依呼叫者限制，至少要 MaxConcurrentValidations / 這個數字 個呼叫者才占得滿。
    public const int MaxConcurrentValidationsPerCaller = 4;

    // 每個正在讀內文的請求最多會占住 MaxDocumentBytes，再加上解碼與驗證時的暫時配置。
    // 同時處理的數量乘上這個大小，就是匿名流量最多能占用的記憶體；16 個在小型容器裡也只有幾十 MiB。
    public const int MaxConcurrentValidations = 16;

    // 讀內文的期限。沒有期限時，用戶端可以把內文送到一半就停住，長時間占住位置與記憶體；
    // Kestrel 的最低速率是算平均的，先快送再停住可以拖一個多小時。
    // 單一來源占不滿全部位置，靠的是 MaxConcurrentRequestsPerClient，不是這個期限。
    public const int BodyReadTimeoutSeconds = 10;

    // 登入、專案、項目這些端點的內文上限。要放得下說明加上一個最長的長文字欄位，再加上其他欄位：
    // WorkRules.MaxDescriptionLength 與 MaxLongTextFieldLength 都是 50,000 字（UTF-16 的字元），一個字在 UTF-8 最多 3 個位元組
    // （4 個位元組的字在 UTF-16 算兩個字），所以兩個加起來是 300,000 位元組；其餘的空間給標題、文字欄位與 JSON 的結構。
    // 一次填滿好幾個長文字欄位的請求會超過，要分次改；用戶端把每個字都跳脫成 \uXXXX 也會超過，網頁的 JSON.stringify 不會這樣做。
    public const int MaxJsonBodyBytes = 512 * 1024;

    // 試算表匯入的內文上限。網頁允許 5 MB 的 CSV，每個格子轉成 JSON 要多寫欄位名稱與引號，一千列加上長說明會遠超過一般端點的上限；
    // 一千列是模組的上限（ItemImport.MaxRows），所以不會無限長。讀內文是逐步放大緩衝區（Utf8BodyReader），不會一開始就占滿這個大小；
    // 這個上限只給匯入端點，其他端點維持 MaxJsonBodyBytes。
    public const int MaxImportBodyBytes = 16 * 1024 * 1024;

    // 已登入的人依人計算。網頁一個畫面就會發好幾個請求，用沒登入的額度一下就滿了。
    public const int SignedInPermitsPerWindow = 600;

    public const int MaxConcurrentRequestsPerPerson = 16;

    // 登入端點依來源 IP 另外算，擋住猜密碼。視窗長度和全域的限流相同。
    public const int SignInPermitsPerWindow = 10;

    // 改自己的密碼依人另外算：拿著別人工作階段的人猜的是那個人的密碼，額度跟著人走，換 IP 也不會多出來。
    // 不和登入共用依 IP 的額度：本機經 Docker 轉發時大家是同一個來源 IP，已登入的人故意送錯的目前密碼，
    // 會把所有人的登入額度一起耗光。
    public const int PasswordChangePermitsPerWindow = 10;

    // 寄測試信，每個人每個視窗最多幾次。每一次都會讓伺服器連到管理員填的主機（逾時要等好幾十秒），
    // 拿它當連線掃描或寄信的工具會被擋在這裡。
    public const int EmailTestPermitsPerWindow = 5;

    // AI 設定頁讀模型清單、存連線，每個人每個視窗最多幾次。每一次都會讓伺服器連到供應商或管理員填的端點。
    public const int AiProviderCallPermitsPerWindow = 10;

    // 送給 AI 助理的訊息，每個人每個視窗最多幾則。每一則都會花組織的模型費用（一則可能呼叫好幾次模型），
    // 一個壞掉的畫面或腳本反覆送，不能在一分鐘內燒掉一大筆。
    public const int AssistantMessagePermitsPerWindow = 10;

    // 本機代理的三個端點，每個人每個視窗最多幾次：長輪詢每 25 秒一次，加上進行中的回覆每 300 毫秒回報一次（同時最多 2 件）。
    // 全域的已登入額度（SignedInPermitsPerWindow）照樣算進去，所以這一個只是讓它單獨有上限，不會比全域的多。
    public const int LocalAgentPermitsPerWindow = 600;

    // 主機代理（worker）依代理算，每個視窗最多幾次：長輪詢每 25 秒一次，加上進行中的回覆每 300 毫秒回報一次（同時最多 2 件）。
    // 它的請求不是任何人的，不算進全域的「已登入」或「沒登入」額度：worker 常駐、在容器網路裡共用同一個來源 IP，用 IP 算會把它自己擋住。
    public const int HostAgentPermitsPerWindow = 1200;

    // 同一個代理同時進行的請求數：一個長輪詢、最多兩件工作的回報，加上一點餘裕。
    public const int MaxConcurrentHostAgentRequests = 16;

    // 配對（用配對碼換機器憑證）依來源 IP 算，不用登入：擋住猜配對碼。失敗另外算進 BearerFailureLimiter。
    public const int HostPairPermitsPerWindow = 10;

    // OAuth 的 token 與撤銷端點依來源 IP 算，不用登入、沒有別的門檻：擋住猜授權碼與更新權杖。
    public const int OAuthTokenPermitsPerWindow = 30;

    // 動態註冊依來源 IP 算，每小時十次（視窗比其他的長，見 ApiRateLimiting.OAuthRegisterWindow）：每一次都會多一列資料。
    public const int OAuthRegisterPermitsPerHour = 10;

    // 同意頁按「允許」，每個人每個視窗最多幾次：每一次都會發出一個授權碼。
    public const int OAuthApprovePermitsPerWindow = 10;

    // 同意頁的 check 與 deny，每個人每個視窗最多幾次（兩個共用一份）：check 會替使用者去抓 Client ID Metadata Document（連到外面），
    // 不限的話登入的人能拿它當跳板，或拿來灌滿快取的資料表。比「允許」寬鬆：重新整理同意頁就是一次 check。
    public const int OAuthCheckPermitsPerWindow = 20;

    // 命令列瀏覽器登入：check 與 approve 依人、token 依來源 IP，額度各自和 OAuth 同意頁的一樣（原因見上面各項）。
    public const int CliLoginCheckPermitsPerWindow = OAuthCheckPermitsPerWindow;

    public const int CliLoginApprovePermitsPerWindow = OAuthApprovePermitsPerWindow;

    public const int CliLoginTokenPermitsPerWindow = OAuthTokenPermitsPerWindow;

    // 授權請求的表單內文上限（位元組）：token、撤銷與註冊的內文都很小。
    public const int MaxOAuthBodyBytes = 16 * 1024;

    // 網頁回報錯誤的端點，每個人每個視窗最多幾次。一個壞掉的畫面可能每秒丟好幾次例外，網頁自己也會去重與設上限，
    // 這裡是最後一道：記錄是寫到磁碟的，不能讓一個人把它灌滿。
    public const int ClientErrorPermitsPerWindow = 10;

    // 網頁回報的錯誤各欄位的上限（UTF-16 的字元數）。網頁送之前會先截斷，超過就是不照合約送的用戶端。
    public const int MaxClientErrorMessageLength = 1000;

    // 一筆記錄裡訊息與堆疊會各寫兩次（整句訊息與結構化的屬性），堆疊太長的話，幾個人就能把有上限的記錄輪替掉。
    public const int MaxClientErrorStackLength = 4000;

    // 網頁回報錯誤的端點，全站每個視窗最多幾次。每人的額度擋不住很多人一起送；記錄的輪替上限是固定的，
    // 超過這個數字的回報寧可丟掉，也不要把伺服器自己的錯誤擠出記錄。
    public const int ClientErrorGlobalPermitsPerWindow = 60;

    public const int MaxClientErrorPathLength = 500;

    // 就緒檢查不限流又不需要登入，每次都去問資料庫的話，等於讓任何人都能替資料庫加壓。
    // 結果保留一小段時間，同時來的請求共用同一次檢查。
    public const int ReadinessCacheSeconds = 2;

    // 資料庫連不上時，連線可能要等很久才失敗；超過這個時間就當作還沒準備好。
    public const int ReadinessTimeoutSeconds = 5;

    // 同一個來源（IPv6 以 /64 計）每個視窗最多幾次 Bearer 權杖驗證失敗，超過就在查資料庫之前回 429。
    // 正常使用的人不會一分鐘失敗幾十次；CI 設錯權杖時會連續失敗，擋它幾十秒也比一直查資料庫好。有效的權杖不計。
    public const int BearerFailuresPerWindow = 30;

    // 記憶體裡最多記幾個來源的權杖失敗次數：來源（尤其是 IPv6 的 /64）是用戶端可以任意換的。
    // 滿了之後沒記過的來源不記也不擋，已經記過的照舊。
    public const int BearerFailureMaxTrackedSources = 10_000;

    // 匯入（試算表與歷史）每個人同時最多幾個：每一個都可能是 MaxImportBodyBytes 的內文加上整批寫入。
    public const int MaxConcurrentImportsPerPerson = 2;

    // kumi 執行檔的下載，每個人每個視窗最多幾次：一個檔案是幾十 MiB，正常的人一分鐘不會下載超過幾次。
    public const int CliDownloadPermitsPerWindow = 10;

    // 附件的下載（項目與知識庫共用一份額度）。說明裡內嵌的圖片也是從這裡下載，一頁可以有幾十張，
    // 所以比一般的限制寬；仍然低於已登入的全域額度（SignedInPermitsPerWindow）。
    public const int AttachmentDownloadPermitsPerWindow = 300;

    // 網頁回報的錯誤：同一個人、同一種類加訊息，這段時間內只記一次。
    public const int ClientErrorDedupeWindowSeconds = 60;

    // 去重器最多記幾個鍵；滿了先清掉過期的，還是滿的話就不再記新的（寧可多記一筆，不讓記憶體長大）。
    public const int ClientErrorDedupeMaxKeys = 10_000;
}
