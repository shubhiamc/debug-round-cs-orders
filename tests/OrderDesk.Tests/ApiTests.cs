using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace OrderDesk.Tests;

public class ApiTests : IDisposable
{
    private readonly TestApp _app = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Register_new_user_returns_201()
    {
        var res = await _app.Client().PostAsJsonAsync("/api/register", new { username = "alice", password = "pa55word" });
        Expect.Status(HttpStatusCode.Created, res, "register");
    }

    [Fact]
    public async Task Register_duplicate_username_returns_409()
    {
        await _app.RegisterAndLogin("alice");
        var res = await _app.Client().PostAsJsonAsync("/api/register", new { username = "ALICE", password = "pa55word" });
        Expect.Status(HttpStatusCode.Conflict, res, "register duplicate");
    }

    [Fact]
    public async Task Register_short_password_returns_400()
    {
        var res = await _app.Client().PostAsJsonAsync("/api/register", new { username = "alice", password = "123" });
        Expect.Status(HttpStatusCode.BadRequest, res, "register short password");
    }

    [Fact]
    public async Task Login_wrong_password_returns_401()
    {
        await _app.RegisterAndLogin("alice");
        var res = await _app.Client().PostAsJsonAsync("/api/login", new { username = "alice", password = "nope-nope" });
        Expect.Status(HttpStatusCode.Unauthorized, res, "login wrong password");
    }

    [Fact]
    public async Task Products_require_a_token()
    {
        var res = await _app.Client().GetAsync("/api/products");
        Expect.Status(HttpStatusCode.Unauthorized, res, "list products without token");
    }

    [Fact]
    public async Task Regular_user_cannot_create_products()
    {
        var token = await _app.RegisterAndLogin();
        var res = await _app.Client(token).PostAsJsonAsync("/api/products", new { name = "Sneaky", price = 1m, stock = 1 });
        Expect.Status(HttpStatusCode.Forbidden, res, "create product as regular user");
    }

    [Fact]
    public async Task Admin_can_create_products()
    {
        var product = await _app.CreateProduct("Sprocket", 3.50m, 12);
        Expect.Equal("Sprocket", product.Name, "created product name");
        Expect.Equal(12, product.Stock, "created product stock");
    }

    [Fact]
    public async Task Create_order_returns_201()
    {
        var product = await _app.CreateProduct("Sprocket", 10m, 50);
        var token = await _app.RegisterAndLogin();
        var res = await _app.PlaceOrderRaw(token, new[] { new { productId = product.Id, qty = 1 } });
        Expect.Status(HttpStatusCode.Created, res, "create order");
    }

    [Fact]
    public async Task Create_order_without_items_returns_400()
    {
        var token = await _app.RegisterAndLogin();
        var res = await _app.PlaceOrderRaw(token, Array.Empty<object>());
        Expect.Status(HttpStatusCode.BadRequest, res, "create order with no items");
    }

    [Fact]
    public async Task Create_order_for_unknown_product_returns_400()
    {
        var token = await _app.RegisterAndLogin();
        var res = await _app.PlaceOrderRaw(token, new[] { new { productId = 424242, qty = 1 } });
        Expect.Status(HttpStatusCode.BadRequest, res, "create order for unknown product");
    }

    [Fact]
    public async Task Create_order_beyond_stock_returns_409()
    {
        var product = await _app.CreateProduct("Rarity", 10m, 2);
        var token = await _app.RegisterAndLogin();
        var res = await _app.PlaceOrderRaw(token, new[] { new { productId = product.Id, qty = 3 } });
        Expect.Status(HttpStatusCode.Conflict, res, "create order beyond stock");
    }

    [Fact]
    public async Task Get_unknown_order_returns_404()
    {
        var token = await _app.RegisterAndLogin();
        var res = await _app.Client(token).GetAsync("/api/orders/999999");
        Expect.Status(HttpStatusCode.NotFound, res, "get unknown order");
    }

    [Fact]
    public async Task Cancel_unknown_order_returns_404()
    {
        var token = await _app.RegisterAndLogin();
        var res = await _app.Client(token).PostAsync("/api/orders/999999/cancel", null);
        Expect.Status(HttpStatusCode.NotFound, res, "cancel unknown order");
    }

    [Fact]
    public async Task Cancel_pending_order_returns_200_and_marks_it_cancelled()
    {
        var product = await _app.CreateProduct("Sprocket", 10m, 50);
        var token = await _app.RegisterAndLogin();
        var order = await _app.PlaceOrder(token, product.Id, 2);

        var res = await _app.Client(token).PostAsync($"/api/orders/{order.Id}/cancel", null);
        Expect.Status(HttpStatusCode.OK, res, "cancel order");

        var cancelled = (await res.Content.ReadFromJsonAsync<OrderDto>())!;
        Expect.Equal("Cancelled", cancelled.Status, "status after cancel");
    }

    [Fact]
    public async Task Cancel_same_order_twice_returns_409()
    {
        var product = await _app.CreateProduct("Sprocket", 10m, 50);
        var token = await _app.RegisterAndLogin();
        var order = await _app.PlaceOrder(token, product.Id, 1);
        var client = _app.Client(token);

        Expect.Status(HttpStatusCode.OK, await client.PostAsync($"/api/orders/{order.Id}/cancel", null), "first cancel");
        Expect.Status(HttpStatusCode.Conflict, await client.PostAsync($"/api/orders/{order.Id}/cancel", null), "second cancel");
    }

    [Fact]
    public async Task List_orders_can_be_filtered_by_status()
    {
        var product = await _app.CreateProduct("Sprocket", 10m, 50);
        var token = await _app.RegisterAndLogin();
        var keep = await _app.PlaceOrder(token, product.Id, 1);
        var drop = await _app.PlaceOrder(token, product.Id, 1);
        var client = _app.Client(token);
        await client.PostAsync($"/api/orders/{drop.Id}/cancel", null);

        var pending = (await client.GetFromJsonAsync<List<OrderDto>>("/api/orders?status=pending"))!;
        Expect.Equal(1, pending.Count, "pending orders");
        Expect.Equal(keep.Id, pending[0].Id, "pending order id");

        var cancelled = (await client.GetFromJsonAsync<List<OrderDto>>("/api/orders?status=cancelled"))!;
        Expect.Equal(1, cancelled.Count, "cancelled orders");
        Expect.Equal(drop.Id, cancelled[0].Id, "cancelled order id");
    }

    [Fact]
    public async Task List_orders_with_unknown_status_returns_400()
    {
        var token = await _app.RegisterAndLogin();
        var res = await _app.Client(token).GetAsync("/api/orders?status=teleported");
        Expect.Status(HttpStatusCode.BadRequest, res, "list orders with bogus status");
    }
}
