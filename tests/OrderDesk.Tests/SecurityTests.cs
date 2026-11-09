using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace OrderDesk.Tests;

public class SecurityTests : IDisposable
{
    private readonly TestApp _app = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Cannot_read_another_users_order()
    {
        var product = await _app.CreateProduct("Sprocket", 10m, 50);
        var alice = await _app.RegisterAndLogin("alice");
        var bob = await _app.RegisterAndLogin("bob");
        var order = await _app.PlaceOrder(alice, product.Id, 1);

        var res = await _app.Client(bob).GetAsync($"/api/orders/{order.Id}");
        Expect.Status(HttpStatusCode.NotFound, res, "bob reads alice's order");
    }

    [Fact]
    public async Task Cannot_cancel_another_users_order()
    {
        var product = await _app.CreateProduct("Sprocket", 10m, 50);
        var alice = await _app.RegisterAndLogin("alice");
        var bob = await _app.RegisterAndLogin("bob");
        var order = await _app.PlaceOrder(alice, product.Id, 1);

        var res = await _app.Client(bob).PostAsync($"/api/orders/{order.Id}/cancel", null);
        Expect.Status(HttpStatusCode.NotFound, res, "bob cancels alice's order");

        var still = (await _app.Client(alice).GetFromJsonAsync<OrderDto>($"/api/orders/{order.Id}"))!;
        Expect.Equal("Pending", still.Status, "alice's order status after bob's attempt");
    }

    [Fact]
    public async Task Order_list_only_contains_my_orders()
    {
        var product = await _app.CreateProduct("Sprocket", 10m, 50);
        var alice = await _app.RegisterAndLogin("alice");
        var bob = await _app.RegisterAndLogin("bob");
        await _app.PlaceOrder(alice, product.Id, 1);

        var bobsOrders = (await _app.Client(bob).GetFromJsonAsync<System.Collections.Generic.List<OrderDto>>("/api/orders"))!;
        Expect.Equal(0, bobsOrders.Count, "bob's order count");
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var token = await _app.RegisterAndLogin();
        var client = _app.Client(token);
        Expect.Status(HttpStatusCode.OK, await client.GetAsync("/api/orders"), "list orders with fresh token");

        _app.Clock.Advance(TokenService.Lifetime + TimeSpan.FromMinutes(1));

        Expect.Status(HttpStatusCode.Unauthorized, await client.GetAsync("/api/orders"), "list orders with expired token");
    }

    [Fact]
    public async Task Tampered_token_is_rejected()
    {
        var token = await _app.RegisterAndLogin();
        var parts = token.Split('.');
        var forged = "x" + parts[0].Substring(1) + "." + parts[1];

        var res = await _app.Client(forged).GetAsync("/api/orders");
        Expect.Status(HttpStatusCode.Unauthorized, res, "list orders with tampered token");
    }

    [Fact]
    public async Task Garbage_token_is_rejected()
    {
        var res = await _app.Client("not-a-token").GetAsync("/api/orders");
        Expect.Status(HttpStatusCode.Unauthorized, res, "list orders with garbage token");
    }

    [Fact]
    public async Task Missing_token_is_rejected()
    {
        var res = await _app.Client().GetAsync("/api/orders");
        Expect.Status(HttpStatusCode.Unauthorized, res, "list orders without token");
    }

    [Fact]
    public async Task Register_ignores_a_client_supplied_role()
    {
        var anon = _app.Client();
        var reg = await anon.PostAsJsonAsync("/api/register", new { username = "mallory", password = "pa55word", role = "admin" });
        Expect.Status(HttpStatusCode.Created, reg, "register with role field");

        var token = await _app.Login("mallory", "pa55word");
        var res = await _app.Client(token).PostAsJsonAsync("/api/products", new { name = "Free money", price = 1m, stock = 1 });
        Expect.Status(HttpStatusCode.Forbidden, res, "mallory creates product");
    }
}
