// 不同的測試類別同時跑（同時跑幾個是 NUnit 的預設：處理器的數量）。每個測試各自開一個 ApiFactory，
// 限流器、假模組、時間都是那個實體自己的；真的資料庫每個類別各一個。同一個類別裡照舊一個一個跑：類別的欄位與 SetUp 是共用的。
// 動到整個程序共用的東西的類別（換掉 Console、數計時器）標 [NonParallelizable]，等別人都停下來才跑。
[assembly: Parallelizable(ParallelScope.Fixtures)]
