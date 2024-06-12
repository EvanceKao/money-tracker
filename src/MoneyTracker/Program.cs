using FluentValidation;
using Microsoft.EntityFrameworkCore;

#region Main

var builder = WebApplication.CreateBuilder(args);

// Register your validator
builder.Services.AddTransient<ExpenseValidator>();

// Register the DbContext and configure SQLite database
builder.Services.AddDbContext<ExpenseContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

// Automatically run migrations
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ExpenseContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseDefaultFiles();
// Enable serving of static files
app.UseStaticFiles();

// ... other middleware and routes ...

app.MapGet("/", () => Results.File("index.html", "text/html"));

app.MapGet("/_hc", () => "Hello World!");

//    - 需能夠支出的 CRUD API

// 建立支出記錄
app.MapPost("/api/expenses", async (Expense expense, ExpenseValidator validator, ExpenseContext context, HttpContext httpContext) =>
{
    var validationResult = validator.Validate(expense);
    if (!validationResult.IsValid)
    {
        return Results.BadRequest(validationResult.Errors);
    }

    // Get user ID from the header
    if (httpContext.Request.Headers.TryGetValue("x-user-id", out var userId))
    {
        expense.UserId = userId;
    }
    else
    {
        return Results.BadRequest("x-user-id header is missing");
    }

    // Save to database
    context.Expenses.Add(expense);
    await context.SaveChangesAsync();

    return Results.Created($"/api/expenses/{expense.Id}", expense);
});

// 查詢支出記錄列表
// 1. **查詢需求**
//    - 查詢時允許傳入：
//      - 標題：要可以模糊搜尋
//      - 發生日期和時間的起訖：
//        - 起訖不能夠超過 30 天內
app.MapGet("/api/expenses", async (ExpenseContext context, HttpContext httpContext, string? title, DateTimeOffset? startDate, DateTimeOffset? endDate, int pageIndex = 0, int pageSize = 10) =>
{
    // Get user ID from the header
    if (httpContext.Request.Headers.TryGetValue("x-user-id", out var userId))
    {
    }
    else
    {
        return Results.BadRequest("x-user-id header is missing");
    }

    if (startDate.HasValue && endDate.HasValue)
    {
        var dateDiff = (endDate.Value - startDate.Value).Days;
        if (dateDiff < 0 || dateDiff > 30)
        {
            return Results.BadRequest("The date range should be within 30 days.");
        }
    }

    var query = context.Expenses.AsQueryable();

    query = query.Where(e => e.UserId == userId.ToString());

    if (!string.IsNullOrEmpty(title))
    {
        query = query.Where(e => e.Title.Contains(title));
    }

    if (startDate.HasValue)
    {
        query = query.Where(e => e.OccurredAt >= startDate.Value);
    }

    if (endDate.HasValue)
    {
        query = query.Where(e => e.OccurredAt <= endDate.Value);
    }

    var totalExpenses = await query.CountAsync();
    var expenses = await query.Skip(pageIndex * pageSize).Take(pageSize).ToListAsync();
    return Results.Ok(new { Total = totalExpenses, Expenses = expenses });
});

// 取得支出記錄
app.MapGet("/api/expenses/{id}", async (int id, ExpenseContext context) =>
{
    var expense = await context.Expenses.FindAsync(id);
    if (expense == null)
    {
        return Results.NotFound();
    }

    return Results.Ok(expense);
});

// 更新支出記錄
app.MapPut("/api/expenses/{id}", async (int id, Expense expense, ExpenseValidator validator, ExpenseContext context) =>
{
    var validationResult = validator.Validate(expense);
    if (!validationResult.IsValid)
    {
        return Results.BadRequest(validationResult.Errors);
    }

    var existingExpense = await context.Expenses.FindAsync(id);
    if (existingExpense == null)
    {
        return Results.NotFound();
    }

    // Update the properties of the existing expense
    existingExpense.Title = expense.Title;
    existingExpense.Amount = expense.Amount;
    existingExpense.OccurredAt = expense.OccurredAt;
    existingExpense.Category = expense.Category;

    await context.SaveChangesAsync();

    // 回傳 put 後的資料
    return Results.Ok(existingExpense);
});

// 刪除支出記錄
app.MapDelete("/api/expenses/{id}", async (int id, ExpenseContext context) =>
{
    var expense = await context.Expenses.FindAsync(id);
    if (expense == null)
    {
        return Results.NotFound();
    }

    context.Expenses.Remove(expense);
    await context.SaveChangesAsync();

    return Results.NoContent();
});

app.Run();

#endregion


// 3. **支出記錄需求**
//    - 一筆支出至少包含：
//      1. 標題
//      2. 金額
//      3. 發生日期和時間
//      4. 分類
// ## 進進階需求
//    - 每一個使用者的支出記錄都要不同，不能夠互相看到
public class Expense
{
    public int Id { get; set; }
    public string Title { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string Category { get; set; }
    public string UserId { get; set; }
}

// 4. **支出記錄建立判斷**
//    - 金額不能夠為負數
//    - 分類不能夠是 [食、衣、住、行] 以外的分類
//    - 發生日期不能夠晚於 1 年前
public class ExpenseValidator : AbstractValidator<Expense>
{
    public ExpenseValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Category).Must(x => new[] { "食", "衣", "住", "行" }.Contains(x));
        RuleFor(x => x.OccurredAt).LessThan(DateTimeOffset.Now.AddYears(1));
    }
}

public class ExpenseContext : DbContext
{
    public DbSet<Expense> Expenses { get; set; }

    public ExpenseContext(DbContextOptions<ExpenseContext> options)
        : base(options)
    {
    }
}


