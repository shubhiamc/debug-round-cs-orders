using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit.Sdk;

namespace OrderDesk.Tests;

// ---- DTOs the tests deserialize responses into -----------------------------

public sealed record ProductDto(int Id, string Name, decimal Price, int Stock);

public sealed record LineDto(int ProductId, string ProductName, int Qty, decimal UnitPrice);

public sealed record OrderDto(
    int Id,
    string Status,
    List<LineDto> Lines,
    string? CouponCode,
    decimal Subtotal,
    decimal Discount,
    decimal Tax,
    decimal Total);

public sealed record TokenDto(string Token, int ExpiresInSeconds);

// ---- assertions that say what they expected --------------------------------

public static class Expect
{
    public static void Status(HttpStatusCode expected, HttpResponseMessage actual, string what)
    {
        if (actual.StatusCode != expected)
            throw new XunitException($"{what}: expected {(int)expected}, got {(int)actual.StatusCode}");
    }

    public static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new XunitException($"{what}: expected {expected}, got {actual}");
    }

    public static void True(bool condition, string what, string detail)
    {
        if (!condition) throw new XunitException($"{what}: {detail}");
    }
}

// ---- a clock the tests can move --------------------------------------------

public sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public void Advance(TimeSpan by) => _now += by;
}

// ---- one fresh app (and clock) per test ------------------------------------

public sealed class TestApp : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private int _userCounter;

    public TestClock Clock { get; } = new();

    public TestApp()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
            }));
    }

    public HttpClient Client(string? token = null)
    {
        var client = _factory.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<string> RegisterAndLogin(string? username = null)
    {
        username ??= $"user{++_userCounter}";
        var anon = Client();
        var reg = await anon.PostAsJsonAsync("/api/register", new { username, password = "pa55word" });
        Expect.Status(HttpStatusCode.Created, reg, $"register {username}");
        return await Login(username, "pa55word");
    }

    public async Task<string> Login(string username, string password)
    {
        var res = await Client().PostAsJsonAsync("/api/login", new { username, password });
        Expect.Status(HttpStatusCode.OK, res, $"login {username}");
        return (await res.Content.ReadFromJsonAsync<TokenDto>())!.Token;
    }

    public Task<string> AdminToken() => Login("admin", "admin-pass");

    public async Task<ProductDto> CreateProduct(string name, decimal price, int stock)
    {
        var admin = Client(await AdminToken());
        var res = await admin.PostAsJsonAsync("/api/products", new { name, price, stock });
        Expect.Status(HttpStatusCode.Created, res, $"create product {name}");
        return (await res.Content.ReadFromJsonAsync<ProductDto>())!;
    }

    public async Task<ProductDto> GetProduct(string token, int productId)
    {
        var res = await Client(token).GetAsync("/api/products");
        Expect.Status(HttpStatusCode.OK, res, "list products");
        var all = (await res.Content.ReadFromJsonAsync<List<ProductDto>>())!;
        return all.Find(p => p.Id == productId)!;
    }

    public Task<HttpResponseMessage> PlaceOrderRaw(string token, object items, string? coupon = null) =>
        Client(token).PostAsJsonAsync("/api/orders", new { items, couponCode = coupon });

    /// <summary>
    /// Places an order and waits until it can be read back, so tests about *other* things
    /// do not depend on how quickly the write lands.
    /// </summary>
    public async Task<OrderDto> PlaceOrder(string token, int productId, int qty, string? coupon = null)
    {
        var res = await PlaceOrderRaw(token, new[] { new { productId, qty } }, coupon);
        Expect.Status(HttpStatusCode.Created, res, "place order");
        var order = (await res.Content.ReadFromJsonAsync<OrderDto>())!;

        var client = Client(token);
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var probe = await client.GetAsync($"/api/orders/{order.Id}");
            if (probe.StatusCode == HttpStatusCode.OK) return order;
            await Task.Delay(50);
        }

        throw new XunitException($"place order: order {order.Id} never became readable");
    }

    public void Dispose() => _factory.Dispose();
}
