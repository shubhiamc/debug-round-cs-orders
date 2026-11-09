using System;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OrderDesk;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Store>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<CouponService>();
builder.Services.AddSingleton<OrderService>();

var app = builder.Build();

app.UseExceptionHandler(handler => handler.Run(async ctx =>
{
    var error = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    var badRequest = error as BadHttpRequestException;
    ctx.Response.StatusCode = badRequest?.StatusCode ?? 500;
    await ctx.Response.WriteAsJsonAsync(new { error = badRequest is null ? "Internal server error." : "Bad request." });
}));

app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api");

// ---- accounts ------------------------------------------------------------

api.MapPost("/register", (RegisterRequest? req, Store store) =>
{
    var username = req?.Username?.Trim();
    if (string.IsNullOrEmpty(username) || username.Length < 3)
        return Api.Error(400, "Username must be at least 3 characters.");
    if (req?.Password is null || req.Password.Length < 6)
        return Api.Error(400, "Password must be at least 6 characters.");

    var (hash, salt) = PasswordHasher.Hash(req.Password);
    var user = store.TryAddUser(username, hash, salt, "user");
    if (user is null) return Api.Error(409, "Username is already taken.");

    return Results.Created($"/api/users/{user.Id}", new { user.Id, user.Username });
});

api.MapPost("/login", (LoginRequest? req, Store store, TokenService tokens) =>
{
    var user = req?.Username is null ? null : store.FindUser(req.Username);
    if (user is null || req?.Password is null || !PasswordHasher.Verify(req.Password, user.PasswordHash, user.Salt))
        return Api.Error(401, "Invalid username or password.");

    return Results.Ok(new { token = tokens.Issue(user), expiresInSeconds = (int)TokenService.Lifetime.TotalSeconds });
});

// ---- products ------------------------------------------------------------

api.MapGet("/products", (HttpContext ctx, TokenService tokens, Store store) =>
{
    if (AuthHelper.Authenticate(ctx, tokens, store) is null) return Api.Error(401, "Authentication required.");
    return Results.Ok(store.AllProducts());
});

api.MapPost("/products", (HttpContext ctx, TokenService tokens, Store store, CreateProductRequest? req) =>
{
    var user = AuthHelper.Authenticate(ctx, tokens, store);
    if (user is null) return Api.Error(401, "Authentication required.");
    if (user.Role != "admin") return Api.Error(403, "Only admins can create products.");

    if (string.IsNullOrWhiteSpace(req?.Name)) return Api.Error(400, "Name is required.");
    if (req.Price is null or <= 0) return Api.Error(400, "Price must be positive.");
    if (req.Stock is null or < 0) return Api.Error(400, "Stock must be zero or more.");

    var product = store.AddProduct(req.Name.Trim(), req.Price.Value, req.Stock.Value);
    return Results.Created($"/api/products/{product.Id}", product);
});

// ---- orders --------------------------------------------------------------

api.MapPost("/orders", async (HttpContext ctx, TokenService tokens, Store store, OrderService orders, CreateOrderRequest? req) =>
{
    var user = AuthHelper.Authenticate(ctx, tokens, store);
    if (user is null) return Api.Error(401, "Authentication required.");

    var result = await orders.CreateAsync(user, req);
    return result.Order is null
        ? Api.Error(result.Status, result.Error!)
        : Results.Json(Api.View(result.Order), statusCode: result.Status);
});

api.MapGet("/orders", (HttpContext ctx, TokenService tokens, Store store, string? status) =>
{
    var user = AuthHelper.Authenticate(ctx, tokens, store);
    if (user is null) return Api.Error(401, "Authentication required.");

    var mine = store.OrdersFor(user.Id);
    if (!string.IsNullOrWhiteSpace(status))
    {
        if (!Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var wanted))
            return Api.Error(400, $"Unknown status '{status}'.");
        mine = mine.Where(o => o.Status == wanted);
    }

    return Results.Ok(mine.Select(Api.View).ToList());
});

api.MapGet("/orders/{id:int}", (int id, HttpContext ctx, TokenService tokens, Store store) =>
{
    var user = AuthHelper.Authenticate(ctx, tokens, store);
    if (user is null) return Api.Error(401, "Authentication required.");

    var order = store.FindOrder(id);
    if (order is null || order.UserId != user.Id) return Api.Error(404, "Order not found.");

    return Results.Ok(Api.View(order));
});

api.MapPost("/orders/{id:int}/cancel", (int id, HttpContext ctx, TokenService tokens, Store store, OrderService orders) =>
{
    var user = AuthHelper.Authenticate(ctx, tokens, store);
    if (user is null) return Api.Error(401, "Authentication required.");

    var order = store.FindOrder(id);
    if (order is null || order.UserId != user.Id) return Api.Error(404, "Order not found.");

    var result = orders.Cancel(order);
    return result.Order is null
        ? Api.Error(result.Status, result.Error!)
        : Results.Ok(Api.View(result.Order));
});

app.Run();

// ---- helpers -------------------------------------------------------------

public sealed record RegisterRequest(string? Username, string? Password);

public sealed record LoginRequest(string? Username, string? Password);

public sealed record CreateProductRequest(string? Name, decimal? Price, int? Stock);

public static class Api
{
    public static IResult Error(int status, string message) => Results.Json(new { error = message }, statusCode: status);

    public static object View(Order o) => new
    {
        o.Id,
        o.Status,
        o.Lines,
        o.CouponCode,
        o.Subtotal,
        o.Discount,
        o.Tax,
        o.Total,
        o.CreatedAtUtc
    };
}

// Lets the test project spin the app up with WebApplicationFactory<Program>.
public partial class Program
{
}
