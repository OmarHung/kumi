namespace PmTool.Blueprint;

/// <summary>驗證的用途。保留字這類之後才加嚴的規則，只擋新寫入的文件，不能讓早就存著的文件讀不回來。</summary>
public enum ValidationMode
{
    /// <summary>新建或修改藍圖：所有規則都檢查。</summary>
    Authoring,

    /// <summary>讀回已經存進資料庫的文件：不套用存進去之後才加嚴的規則。</summary>
    Stored,
}
