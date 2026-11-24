namespace PmTool.Cli.Import.Monday;

/// <summary>fetch 用的 GraphQL 欄位。值一律走變數；這裡只有查詢的形狀。</summary>
internal static class MondayQueries
{
    /// <summary>
    /// 每頁的項目數。每個項目都帶著留言（100 則）與回覆，一頁 25 個大約是一次請求能承受的複雜度；
    /// 再大就容易撞到單次查詢的複雜度上限，再小只是多幾次往返。
    /// </summary>
    public const int ItemsPageSize = 25;

    /// <summary>項目的留言一次取的則數；取滿就表示可能還有，要接著用 page 讀。monday 的上限是 100。</summary>
    public const int UpdatesPageSize = 100;

    public const int UsersPageSize = 200;

    private const string Asset = "assets { id name file_extension file_size created_at uploaded_by { id } public_url }";

    private static readonly string Updates =
        $"updates(limit: {UpdatesPageSize}) {{ id creator_id created_at body {Asset} replies {{ id creator_id created_at body {Asset} }} }}";

    private const string ColumnValues =
        "column_values { id type text value "
        + "... on StatusValue { label index } "
        + "... on PeopleValue { persons_and_teams { id kind } } "
        + "... on BoardRelationValue { linked_items { id name } } }";

    private static readonly string ItemFields =
        $"id name state created_at creator_id url group {{ id }} {ColumnValues} {Asset} {Updates}";

    public const string UsersDeclarations = "$limit: Int, $page: Int";

    public const string UsersSelection = "users(limit: $limit, page: $page) { id name email }";

    public const string BoardDeclarations = "$ids: [ID!]";

    public const string BoardSelection =
        "boards(ids: $ids) { id name groups { id title } columns { id title type settings_str } }";

    /// <summary>子項目看板只需要欄（編號與名稱 monday 自己管，用不到）。</summary>
    public const string SubitemBoardSelection = "boards(ids: $ids) { id columns { id title type settings_str } }";

    public const string FirstPageDeclarations = "$ids: [ID!], $limit: Int, $query: ItemsQuery";

    /// <summary>子項目不再帶子項目，其餘與項目相同。</summary>
    public static readonly string FirstPageSelection =
        $"boards(ids: $ids) {{ items_page(limit: $limit, query_params: $query) {{ cursor items {{ {ItemFields} subitems {{ {ItemFields} }} }} }} }}";

    public const string NextPageDeclarations = "$cursor: String!, $limit: Int";

    public static readonly string NextPageSelection =
        $"next_items_page(cursor: $cursor, limit: $limit) {{ cursor items {{ {ItemFields} subitems {{ {ItemFields} }} }} }}";

    public const string MoreUpdatesDeclarations = "$ids: [ID!], $page: Int";

    public static readonly string MoreUpdatesSelection =
        $"items(ids: $ids) {{ id updates(limit: {UpdatesPageSize}, page: $page) {{ id creator_id created_at body {Asset} replies {{ id creator_id created_at body {Asset} }} }} }}";
}
