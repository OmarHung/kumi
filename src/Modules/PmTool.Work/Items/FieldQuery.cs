using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using PmTool.Abstractions;
using PmTool.Blueprint.Model;
using PmTool.Work.Blueprints;
using PmTool.Work.Persistence;
using static PmTool.Work.Items.FieldQueryRules;

namespace PmTool.Work.Items;

/// <summary>
/// 把檢查過的欄位條件套到查詢上。篩選用 jsonb 的包含（<c>fields @> '{"client":"a"}'</c>），
/// JSON 用 System.Text.Json 組、當參數傳，不拼進 SQL；包含查詢用得到 GIN 索引。
/// 排序的 key 是參數，選項的 key 來自驗證過的藍圖。
/// </summary>
internal static class FieldQuery
{
    public static IQueryable<ItemEntity> Filter(
        IQueryable<ItemEntity> items, IReadOnlyList<FieldFilter> filters, ProjectSchema schema, Guid me)
    {
        foreach (var filter in filters)
        {
            var field = schema.Fields[filter.Key];
            var predicates = filter.Values.Distinct(StringComparer.Ordinal).Select(value => Matches(field, value, me)).ToList();
            if (filter.IncludeEmpty)
            {
                predicates.Add(Missing(field.Key));
            }

            items = items.Where(AnyOf(predicates));
        }

        return items;
    }

    public static IOrderedQueryable<ItemEntity> Sort(
        IQueryable<ItemEntity> items, string key, ProjectSchema schema, bool descending)
    {
        var field = schema.Fields[key];
        return field.Type switch
        {
            FieldType.SingleSelect => SortBySelect(items, field, descending),
            FieldType.Checkbox => SortByChecked(items, key, descending),
            _ => SortByValue(items, key, descending),
        };
    }

    private static Expression<Func<ItemEntity, bool>> Matches(FieldDefinition field, string value, Guid me)
    {
        var key = field.Key;
        switch (field.Type)
        {
            case FieldType.Checkbox when value == False:
                // 沒勾的不存，所以「沒勾」是「沒有存成 true」。
                var storedTrue = Json(key, true);
                return i => !EF.Functions.JsonContains(i.Fields, storedTrue);
            case FieldType.Checkbox:
                var isChecked = Json(key, true);
                return i => EF.Functions.JsonContains(i.Fields, isChecked);
            case FieldType.MultiSelect:
                var anyJson = Json(key, new[] { value });
                return i => EF.Functions.JsonContains(i.Fields, anyJson);
            case FieldType.Person:
                // me 只有人員欄位換成自己；單選的選項也可能叫 me。人員編號存成小寫加連字號。
                var person = value == Me ? me : Guid.Parse(value);
                var personJson = Json(key, person.ToString("D"));
                return i => EF.Functions.JsonContains(i.Fields, personJson);
            default:
                var optionJson = Json(key, value);
                return i => EF.Functions.JsonContains(i.Fields, optionJson);
        }
    }

    // 值不存在、類型沒有這個欄位、多選是空的都是「沒有填」。只看 key 在不在：寫入時空陣列不存（FieldValueRules），
    // 改了這個前提（例如匯入直接寫進 {"tags": []}），這裡要一起改。
    private static Expression<Func<ItemEntity, bool>> Missing(string key) => i => !EF.Functions.JsonExists(i.Fields, key);

    private static string Json(string key, object value) => JsonSerializer.Serialize(new Dictionary<string, object> { [key] = value });

    private static Expression<Func<ItemEntity, bool>> AnyOf(IReadOnlyList<Expression<Func<ItemEntity, bool>>> predicates)
    {
        var item = Expression.Parameter(typeof(ItemEntity), "i");
        var body = predicates
            .Select(predicate => ReplacingExpressionVisitor.Replace(predicate.Parameters[0], item, predicate.Body))
            .Aggregate(Expression.OrElse);
        return Expression.Lambda<Func<ItemEntity, bool>>(body, item);
    }

    // 沒有值的排最後（兩個方向都是）；jsonb 的值自己會依型別比大小。
    private static IOrderedQueryable<ItemEntity> SortByValue(IQueryable<ItemEntity> items, string key, bool descending)
    {
        var ordered = items.OrderBy(i => !EF.Functions.JsonExists(i.Fields, key));
        return descending
            ? ordered.ThenByDescending(i => FieldSql.Json(i.Fields, key))
            : ordered.ThenBy(i => FieldSql.Json(i.Fields, key));
    }

    // 沒勾在前：勾選的欄位沒有「空」，沒勾就是沒存。
    private static IOrderedQueryable<ItemEntity> SortByChecked(IQueryable<ItemEntity> items, string key, bool descending)
    {
        var checkedJson = Json(key, true);
        return descending
            ? items.OrderByDescending(i => EF.Functions.JsonContains(i.Fields, checkedJson))
            : items.OrderBy(i => EF.Functions.JsonContains(i.Fields, checkedJson));
    }

    // 依選項在藍圖裡的順序；不認得的選項排在認得的後面、空的前面。照 StateRank 的做法組 CASE。
    private static IOrderedQueryable<ItemEntity> SortBySelect(IQueryable<ItemEntity> items, FieldDefinition field, bool descending)
    {
        var key = field.Key;
        var rank = OptionRank(field);
        var ordered = items
            .OrderBy(i => !EF.Functions.JsonExists(i.Fields, key))
            .ThenBy(UnknownOption(field));
        return descending ? ordered.ThenByDescending(rank) : ordered.ThenBy(rank);
    }

    private static Expression<Func<ItemEntity, int>> OptionRank(FieldDefinition field)
    {
        var item = Expression.Parameter(typeof(ItemEntity), "item");
        var text = TextOf(item, field.Key);
        Expression rank = Expression.Constant(int.MaxValue);
        var options = field.Options ?? [];
        for (var position = options.Count - 1; position >= 0; position--)
        {
            rank = Expression.Condition(
                Expression.Equal(text, Expression.Constant(options[position].Key)), Expression.Constant(position), rank);
        }

        return Expression.Lambda<Func<ItemEntity, int>>(rank, item);
    }

    private static Expression<Func<ItemEntity, bool>> UnknownOption(FieldDefinition field)
    {
        var item = Expression.Parameter(typeof(ItemEntity), "item");
        var text = TextOf(item, field.Key);
        Expression known = Expression.Constant(false);
        foreach (var option in field.Options ?? [])
        {
            known = Expression.OrElse(Expression.Equal(text, Expression.Constant(option.Key)), known);
        }

        return Expression.Lambda<Func<ItemEntity, bool>>(Expression.Not(known), item);
    }

    private static MethodCallExpression TextOf(ParameterExpression item, string key) =>
        Expression.Call(
            typeof(FieldSql).GetMethod(nameof(FieldSql.Text))!,
            Expression.Property(item, nameof(ItemEntity.Fields)),
            Expression.Constant(key));
}
