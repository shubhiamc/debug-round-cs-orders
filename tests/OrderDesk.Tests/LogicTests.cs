using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace OrderDesk.Tests;

public class LogicTests : IDisposable
{
    private readonly TestApp _app = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Order_without_coupon_adds_tax_and_rounds_to_cents()
    {
        // 3 x 19.99 = 59.97; tax 8.25 % = 4.947525 -> 4.95; total 64.92
        var product = await _app.CreateProduct("Sprocket", 19.99m, 50);
        var token = await _app.RegisterAndLogin();

        var order = await _app.PlaceOrder(token, product.Id, 3);

        Expect.Equal(59.97m, order.Subtotal, "subtotal");
        Expect.Equal(0m, order.Discount, "discount");
        Expect.Equal(4.95m, order.Tax, "tax");
        Expect.Equal(64.92m, order.Total, "total");
    }

    [Fact]
    public async Task Percentage_coupon_is_applied_before_tax()
    {
        // 100.00 - 10 % = 90.00; tax 90.00 * 8.25 % = 7.425 -> 7.43; total 97.43
        var product = await _app.CreateProduct("Sprocket", 100m, 50);
        var token = await _app.RegisterAndLogin();

        var order = await _app.PlaceOrder(token, product.Id, 1, "SAVE10");

        Expect.Equal(10.00m, order.Discount, "SAVE10 discount");
        Expect.Equal(7.43m, order.Tax, "tax on discounted amount");
        Expect.Equal(97.43m, order.Total, "total with SAVE10");
    }

    [Fact]
    public async Task Capped_coupon_respects_its_cap_before_tax()
    {
        // 200.00 with BIG20 (20 %, capped at 15.00) -> 185.00; tax 15.2625 -> 15.26; total 200.26
        var product = await _app.CreateProduct("Sprocket", 100m, 50);
        var token = await _app.RegisterAndLogin();

        var res = await _app.PlaceOrderRaw(token, new[] { new { productId = product.Id, qty = 2 } }, "BIG20");
        Expect.Status(HttpStatusCode.Created, res, "order with BIG20");
        var order = (await res.Content.ReadFromJsonAsync<OrderDto>())!;

        Expect.Equal(15.00m, order.Discount, "BIG20 discount");
        Expect.Equal(200.26m, order.Total, "total with BIG20");
    }

    [Fact]
    public async Task Cancelling_an_order_puts_its_stock_back()
    {
        var product = await _app.CreateProduct("Sprocket", 10m, 50);
        var token = await _app.RegisterAndLogin();
        var order = await _app.PlaceOrder(token, product.Id, 4);
        var stockWhileOrdered = (await _app.GetProduct(token, product.Id)).Stock;

        var res = await _app.Client(token).PostAsync($"/api/orders/{order.Id}/cancel", null);
        Expect.Status(HttpStatusCode.OK, res, "cancel order");

        var stockAfterCancel = (await _app.GetProduct(token, product.Id)).Stock;
        Expect.Equal(stockWhileOrdered + 4, stockAfterCancel, "stock after cancelling an order of 4");
    }

    [Fact]
    public async Task Order_of_exactly_ten_items_is_allowed()
    {
        var product = await _app.CreateProduct("Sprocket", 1m, 50);
        var token = await _app.RegisterAndLogin();

        var res = await _app.PlaceOrderRaw(token, new[] { new { productId = product.Id, qty = 10 } });
        Expect.Status(HttpStatusCode.Created, res, "order of 10 items");
    }

    [Fact]
    public async Task Order_of_eleven_items_is_rejected()
    {
        var product = await _app.CreateProduct("Sprocket", 1m, 50);
        var token = await _app.RegisterAndLogin();

        var res = await _app.PlaceOrderRaw(token, new[] { new { productId = product.Id, qty = 11 } });
        Expect.Status(HttpStatusCode.BadRequest, res, "order of 11 items");
    }

    [Fact]
    public async Task Expired_coupon_is_rejected()
    {
        var product = await _app.CreateProduct("Sprocket", 100m, 50);
        var token = await _app.RegisterAndLogin();

        var res = await _app.PlaceOrderRaw(token, new[] { new { productId = product.Id, qty = 1 } }, "OLD5");
        Expect.Status(HttpStatusCode.BadRequest, res, "order with expired coupon OLD5");
    }
}
