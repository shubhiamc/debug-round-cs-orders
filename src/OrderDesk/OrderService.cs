using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OrderDesk;

public sealed record OrderItemRequest(int ProductId, int Qty);

public sealed record CreateOrderRequest(List<OrderItemRequest?>? Items, string? CouponCode);

public sealed record OrderResult(Order? Order, int Status, string? Error)
{
    public static OrderResult Ok(Order order, int status = 200) => new(order, status, null);
    public static OrderResult Fail(int status, string error) => new(null, status, error);
}

public static class Money
{
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

public sealed class OrderService
{
    public const int MaxItemsPerOrder = 10;
    public const decimal TaxRate = 0.0825m;

    private readonly Store _store;
    private readonly CouponService _coupons;
    private readonly TimeProvider _time;

    public OrderService(Store store, CouponService coupons, TimeProvider time)
    {
        _store = store;
        _coupons = coupons;
        _time = time;
    }

    public async Task<OrderResult> CreateAsync(User user, CreateOrderRequest? request)
    {
        if (request?.Items is null || request.Items.Count == 0)
            return OrderResult.Fail(400, "At least one item is required.");
        if (request.Items.Any(i => i is null || i.Qty <= 0))
            return OrderResult.Fail(400, "Every item needs a positive quantity.");

        var items = request.Items
            .GroupBy(i => i!.ProductId)
            .Select(g => new OrderItemRequest(g.Key, g.Sum(i => i!.Qty)))
            .ToList();

        if (items.Sum(i => i.Qty) > MaxItemsPerOrder)
            return OrderResult.Fail(400, $"An order can contain at most {MaxItemsPerOrder} items.");

        var lookup = _coupons.Find(request.CouponCode);
        if (lookup.Error is not null) return OrderResult.Fail(400, lookup.Error);

        Order order;
        lock (_store.Gate)
        {
            foreach (var item in items)
            {
                var product = _store.GetProduct(item.ProductId);
                if (product is null) return OrderResult.Fail(400, $"Unknown product {item.ProductId}.");
                if (product.Stock < item.Qty) return OrderResult.Fail(409, $"Not enough stock for {product.Name}.");
            }

            var lines = items.Select(Reserve).ToList();

            var subtotal = lines.Sum(l => l.UnitPrice * l.Qty);
            var discount = lookup.Coupon is null ? 0m : _coupons.DiscountFor(lookup.Coupon, subtotal);
            var taxable = subtotal - discount;
            var tax = Money.Round(taxable * TaxRate);

            order = new Order
            {
                Id = _store.NextOrderId(),
                UserId = user.Id,
                Lines = lines,
                CouponCode = lookup.Coupon?.Code,
                Subtotal = subtotal,
                Discount = discount,
                Tax = tax,
                Total = taxable + tax,
                Status = OrderStatus.Pending,
                CreatedAtUtc = _time.GetUtcNow().UtcDateTime
            };
        }

        await _store.SaveOrderAsync(order);
        return OrderResult.Ok(order, 201);
    }

    /// <summary>Cancels a pending order and puts its stock back on the shelf. Ownership is checked by the caller.</summary>
    public OrderResult Cancel(Order order)
    {
        lock (_store.Gate)
        {
            if (order.Status == OrderStatus.Cancelled)
                return OrderResult.Fail(409, "Order is already cancelled.");

            Restock(order);
            order.Status = OrderStatus.Cancelled;
        }

        return OrderResult.Ok(order);
    }

    private OrderLine Reserve(OrderItemRequest item)
    {
        var product = _store.GetProduct(item.ProductId)!;
        product.Stock -= item.Qty;
        return new OrderLine
        {
            ProductId = product.Id,
            ProductName = product.Name,
            Qty = item.Qty,
            UnitPrice = product.Price
        };
    }

    private void Restock(Order order)
    {
        foreach (var line in order.Lines)
        {
            var product = _store.GetProduct(line.ProductId);
            if (product is not null) product.Stock += line.Qty;
        }
    }
}
