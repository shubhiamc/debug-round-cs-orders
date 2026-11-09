# Answer key

12 bugs: 10 are caught by the tests (the first one stops the test project from compiling), 2 are hidden.
The fixed code is on the `solution` branch (`git diff main solution`).

Line numbers refer to `main`.

## Bugs with tests

| # | Flavor | File | Symptom / failing test | Root cause | Fix |
|---|--------|------|------------------------|-----------|-----|
| 1 | Syntax | `tests/OrderDesk.Tests/SyntaxTests.cs` | Test project does not compile: `'List<OrderDto>' does not contain a definition for 'Any'` | `ImplicitUsings` is disabled, so `System.Linq` is not imported. The file calls `Any` / `Select` without it. | Add `using System.Linq;` |
| 2 | Syntax (async) | `src/OrderDesk/OrderService.cs` `CreateAsync` | Symptom 1. `New_order_is_readable_immediately_after_it_is_created`. Order list is empty right after "Place order". | `_store.SaveOrderAsync(order)` is not awaited. The (simulated slow) write finishes after the response was sent, so a quick read misses it. The compiler warns with CS4014 / CS1998. | `await _store.SaveOrderAsync(order);` |
| 3 | Syntax (LINQ) | `OrderService.cs` `CreateAsync` | Symptom 2. `Placing_an_order_reduces_stock_by_exactly_the_quantity`: expected 47, got 44. | `items.Select(Reserve)` is lazy and `Reserve` has a side effect (decrements stock). `lines` is enumerated twice (`Sum`, then `ToList`), so stock is decremented twice. | Materialize once: `var lines = items.Select(Reserve).ToList();` (and use `lines` as is). Don't put side effects inside `Select`. |
| 4 | Syntax (equality) | `src/OrderDesk/TextUtils.cs` `FindByKey` | Symptom 3. `Coupon_code_is_matched_by_value_not_by_instance`: expected 201, got 400. | The key selector returns `object`, so `keySelector(item) == key` compares two `object`s, which is **reference** equality. The request string and the coupon's string are different instances. | `Equals(keySelector(item), key)` (or make the helper generic over the key type and use `EqualityComparer<TKey>.Default`). |
| 5 | API (500 vs 404) | `Program.cs` GET `/orders/{id}` + `Store.cs` `GetOrder` | Symptom 7. `Get_unknown_order_returns_404`: expected 404, got 500. | `GetOrder` uses the dictionary indexer, which throws `KeyNotFoundException` for an unknown id. Nothing handles it, so the exception handler returns 500. | Use `store.FindOrder(id)` (`TryGetValue`), return 404 on null. Delete `GetOrder`. |
| 6 | API (409) | `OrderService.cs` `Cancel` | Symptom 6. `Cancel_same_order_twice_returns_409`: expected 409, got 200. | No "already cancelled" guard. | `if (order.Status == OrderStatus.Cancelled) return OrderResult.Fail(409, ...)` before changing anything. |
| 7 | Security (IDOR) | `Program.cs` POST `/orders/{id}/cancel` | Symptom 8. `Cannot_cancel_another_users_order`: expected 404, got 200. | The handler only checks the order exists. The ownership check (`order.UserId != user.Id`) that GET has is missing. | `if (order is null \|\| order.UserId != user.Id) return 404;` (404 rather than 403 so ids can't be probed). |
| 8 | Security (token) | `src/OrderDesk/Auth.cs` `TokenService.Validate` | Symptom 9. `Expired_token_is_rejected`: expected 401, got 200. | The token carries `exp` but `Validate` never checks it, so a token is valid forever. | `if (claims.Exp <= _time.GetUtcNow().ToUnixTimeSeconds()) return null;` |
| 9 | Logic (coupon vs tax) | `OrderService.cs` `CreateAsync` | Symptom 4. `Capped_coupon_respects_its_cap_before_tax` (and the SAVE10 test): total 201.50 instead of 200.26. | Tax is computed on the full subtotal, and the percentage is then taken off `subtotal + tax`. With a capped coupon the order of operations changes the answer. | `discount = DiscountFor(coupon, subtotal); taxable = subtotal - discount; tax = Round(taxable * rate); total = taxable + tax`. |
| 10 | Logic (state order) | `OrderService.cs` `Cancel` | Symptom 5. `Cancelling_an_order_puts_its_stock_back`. | Status is set to `Cancelled` **before** `if (order.Status == Pending) Restock(order)`, so the condition is never true. | Restock first, then set the status (or capture `wasPending` beforehand). |

## Hidden bugs (no failing test)

| # | Flavor | File | Symptom | Root cause | Fix |
|---|--------|------|---------|-----------|-----|
| H1 | Security (timing) | `Auth.cs` `PasswordHasher.Verify` | Symptom 11 (security review). Everything behaves correctly. | The derived hash is compared as strings with `==`, which returns at the first differing character (timing side channel). | `CryptographicOperations.FixedTimeEquals(computed, Convert.FromBase64String(hash))`. |
| H2 | Logic (time zone) | `CouponService.cs` `Find` | Symptom 10. Passes in CI/UTC and in tests (the `TestClock` has a UTC local zone). | `_time.GetLocalNow().DateTime` (server local time, offset dropped) is compared to a UTC expiry. On a server in UTC+11, `NYE30` is treated as expired about 11 hours early; in UTC-8 it lives 8 hours too long. | `_time.GetUtcNow().UtcDateTime > coupon.ExpiresAtUtc`. To make it testable, give `TestClock` a non-UTC `LocalTimeZone` and assert on a coupon near its expiry. |

## Interactions to watch for (good discussion points)

- **#1 hides everything.** Until the test project compiles you see no test results at all.
- **#4 masks #9.** All coupon tests fail with 400 first. After fixing the equality bug, the wrong totals appear.
- **#5 masks #2** in `New_order_is_readable_immediately...`: the "get right after create" call returns 500 first; after fixing #5 it returns 404, which exposes the missing `await`.
- **#6 and #10** interact in the UI: once stock is restocked correctly, cancelling twice would restock twice if the 409 guard were still missing.
- The test helper `PlaceOrder` polls until the order is readable. That is why #2 does not make every other test fail.

## Things that look suspicious but are correct

- `Money.Round` uses `MidpointRounding.AwayFromZero` on `decimal` (no float/double anywhere).
- Max items per order: `> MaxItemsPerOrder` is right (10 allowed, 11 rejected).
- Role is not bound from the register request (a test pins that down).
- Token signing uses a random per-process key and constant-time signature comparison.
