using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OrderDesk;

public enum OrderStatus
{
    Pending,
    Cancelled
}

public sealed class User
{
    public int Id { get; init; }
    public string Username { get; init; } = "";
    public string PasswordHash { get; init; } = "";
    public string Salt { get; init; } = "";
    public string Role { get; init; } = "user";
}

public sealed class Product
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public decimal Price { get; init; }
    public int Stock { get; set; }
}

public sealed class OrderLine
{
    public int ProductId { get; init; }
    public string ProductName { get; init; } = "";
    public int Qty { get; init; }
    public decimal UnitPrice { get; init; }
}

public sealed class Order
{
    public int Id { get; init; }
    public int UserId { get; init; }
    public List<OrderLine> Lines { get; init; } = new();
    public string? CouponCode { get; init; }
    public decimal Subtotal { get; init; }
    public decimal Discount { get; init; }
    public decimal Tax { get; init; }
    public decimal Total { get; init; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public DateTime CreatedAtUtc { get; init; }
}

public sealed class Coupon
{
    public string Code { get; init; } = "";
    public decimal Percent { get; init; }          // 0.10m == 10 %
    public decimal? MaxDiscount { get; init; }     // optional cap, in currency
    public DateTime ExpiresAtUtc { get; init; }
}

/// <summary>In-memory "database". Everything lives and dies with the process.</summary>
public sealed class Store
{
    /// <summary>Held while stock or order state is being changed.</summary>
    public object Gate { get; } = new();

    private readonly ConcurrentDictionary<int, User> _users = new();
    private readonly ConcurrentDictionary<int, Product> _products = new();
    private readonly ConcurrentDictionary<int, Order> _orders = new();
    private int _userSeq;
    private int _productSeq;
    private int _orderSeq;

    public IReadOnlyList<Coupon> Coupons { get; } = new List<Coupon>
    {
        new() { Code = "SAVE10", Percent = 0.10m, ExpiresAtUtc = new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
        new() { Code = "BIG20", Percent = 0.20m, MaxDiscount = 15.00m, ExpiresAtUtc = new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
        new() { Code = "OLD5", Percent = 0.05m, ExpiresAtUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
        new() { Code = "NYE30", Percent = 0.30m, ExpiresAtUtc = new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc) },
    };

    public Store()
    {
        var (hash, salt) = PasswordHasher.Hash("admin-pass");
        TryAddUser("admin", hash, salt, "admin");

        AddProduct("Widget", 9.99m, 100);
        AddProduct("Gadget", 24.50m, 50);
        AddProduct("Gizmo", 100.00m, 20);
    }

    // ---- users ----------------------------------------------------------

    public User? TryAddUser(string username, string hash, string salt, string role)
    {
        lock (Gate)
        {
            if (FindUser(username) is not null) return null;
            var user = new User
            {
                Id = Interlocked.Increment(ref _userSeq),
                Username = username,
                PasswordHash = hash,
                Salt = salt,
                Role = role
            };
            _users[user.Id] = user;
            return user;
        }
    }

    public User? FindUser(string username) =>
        _users.Values.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));

    public User? GetUser(int id) => _users.TryGetValue(id, out var u) ? u : null;

    // ---- products -------------------------------------------------------

    public Product AddProduct(string name, decimal price, int stock)
    {
        var p = new Product { Id = Interlocked.Increment(ref _productSeq), Name = name, Price = price, Stock = stock };
        _products[p.Id] = p;
        return p;
    }

    public Product? GetProduct(int id) => _products.TryGetValue(id, out var p) ? p : null;

    public IEnumerable<Product> AllProducts() => _products.Values.OrderBy(p => p.Id);

    // ---- orders ---------------------------------------------------------

    public int NextOrderId() => Interlocked.Increment(ref _orderSeq);

    /// <summary>Pretends to be a slow database write.</summary>
    public async Task SaveOrderAsync(Order order)
    {
        await Task.Delay(120);
        _orders[order.Id] = order;
    }

    public Order? FindOrder(int id) => _orders.TryGetValue(id, out var o) ? o : null;

    public IEnumerable<Order> OrdersFor(int userId) =>
        _orders.Values.Where(o => o.UserId == userId).OrderBy(o => o.Id);
}
