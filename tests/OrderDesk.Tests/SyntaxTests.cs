using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace OrderDesk.Tests;

public class SyntaxTests : IDisposable
{
    private readonly TestApp _app = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task New_order_is_readable_immediately_after_it_is_created()
    {
        var product = await _app.CreateProduct("Sprocket", 10m, 50);
        var token = await _app.RegisterAndLogin();
        var client = _app.Client(token);

        var res = await _app.PlaceOrderRaw(token, new[] { new { productId = product.Id, qty = 1 } });
        Expect.Status(HttpStatusCode.Created, res, "create order");
        var created = (await res.Content.ReadFromJsonAsync<OrderDto>())!;

        var get = await client.GetAsync($"/api/orders/{created.Id}");
        Expect.Status(HttpStatusCode.OK, get, "get order right after create");

        var list = (await client.GetFromJsonAsync<List<OrderDto>>("/api/orders"))!;
        Expect.True(list.Any(o => o.Id == created.Id), "order list right after create",
            $"expected ids [{created.Id}], got [{string.Join(", ", list.Select(o => o.Id))}]");
    }

    [Fact]
    public async Task Placing_an_order_reduces_stock_by_exactly_the_quantity()
    {
        var product = await _app.CreateProduct("Sprocket", 10m, 50);
        var token = await _app.RegisterAndLogin();

        await _app.PlaceOrder(token, product.Id, 3);

        var after = await _app.GetProduct(token, product.Id);
        Expect.Equal(47, after.Stock, "stock after ordering 3 of 50");
    }

    [Fact]
    public async Task Coupon_code_is_matched_by_value_not_by_instance()
    {
        var product = await _app.CreateProduct("Sprocket", 100m, 50);
        var token = await _app.RegisterAndLogin();

        var res = await _app.PlaceOrderRaw(token, new[] { new { productId = product.Id, qty = 1 } }, "SAVE10");
        Expect.Status(HttpStatusCode.Created, res, "order with coupon SAVE10");

        var padded = await _app.PlaceOrderRaw(token, new[] { new { productId = product.Id, qty = 1 } }, "  save10 ");
        Expect.Status(HttpStatusCode.Created, padded, "order with coupon '  save10 '");
    }

    [Fact]
    public async Task Unknown_coupon_is_rejected_with_400()
    {
        var product = await _app.CreateProduct("Sprocket", 100m, 50);
        var token = await _app.RegisterAndLogin();

        var res = await _app.PlaceOrderRaw(token, new[] { new { productId = product.Id, qty = 1 } }, "NOPE99");
        Expect.Status(HttpStatusCode.BadRequest, res, "order with unknown coupon");
    }
}
