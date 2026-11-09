# debug-round-cs-orders

A **debugging-interview practice project** in C# (.NET 8, ASP.NET Core minimal API, xUnit, `WebApplicationFactory`).
Everything is in memory, there is no database.

`OrderDesk` is a small order-management service with a tiny web UI. It was shipped with bugs.
Your job: **find them, explain the root cause, fix them.**

> The `main` branch is the buggy one. Do not peek at the other branches until you are done.
> `solution` has the fixed code, `answer-key` has a write-up of every bug.

## Setup

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet restore
dotnet test      # run the test suite
dotnet run --project src/OrderDesk   # http://localhost:5000 (see console output for the port)
```

Open the URL printed in the console to use the UI. A seeded admin account exists:
`admin` / `admin-pass`. Register any other user from the UI.

Failing assertions print what they expected and what they got, e.g. `cancel order: expected 200, got 409`.

## Suggested format (45–60 min)

1. Run the tests. Read the output before you touch any code.
2. Read the user reports below and try to reproduce each one (tests, UI, or `curl`).
3. For each bug: say what is wrong **and why**, then fix it with the smallest sensible change.
4. Talk out loud. Interviewers care about *how* you narrow things down.

Things to know:

- Not every bug has a failing test, and not every failing test points at only one bug.
  Fixing one bug can reveal another hiding behind it.
- Don't "fix" a test to make it pass. The tests describe the intended behaviour.
- `app.js` / `index.html` are fine; symptoms you see in the UI come from the backend.

## The app

| Endpoint | Description |
| --- | --- |
| `POST /api/register` | `{ username, password }` |
| `POST /api/login` | returns a bearer token (valid for 1 hour) |
| `GET /api/products` | list products (auth) |
| `POST /api/products` | create a product (admin only) |
| `POST /api/orders` | `{ items: [{ productId, qty }], couponCode }`, max 10 items per order, 8.25 % tax |
| `GET /api/orders?status=` | my orders, optionally filtered by `pending` / `cancelled` |
| `GET /api/orders/{id}` | one of my orders |
| `POST /api/orders/{id}/cancel` | cancel one of my orders, stock goes back on the shelf |

Coupons: `SAVE10` (10 % off), `BIG20` (20 % off, discount capped at 15.00), `NYE30` (30 % off, promo ends 2026-12-31 23:59:59 UTC), `OLD5` (expired).
A coupon discounts the goods; tax is then charged on what the customer actually pays for them.

Code map: `Program.cs` (routes) · `Auth.cs` (passwords, tokens) · `Store.cs` (in-memory data) · `OrderService.cs` (order rules) · `CouponService.cs` · `TextUtils.cs`.
Tests: `ApiTests.cs` · `SecurityTests.cs` · `SyntaxTests.cs` · `LogicTests.cs`.

## User-reported symptoms

1. *"I place an order and the 'My orders' list is empty. If I wait a moment and hit Refresh it shows up."*
2. *"I ordered 3 Sprockets and the product list says the stock went down by 6."*
3. *"SAVE10 is printed on our flyers, but checkout says the code isn't valid."*
4. *"A basket of two 100.00 items with BIG20 should come to 200.26 according to finance. The app disagrees."*
5. *"I cancelled an order, but the stock never came back."*
6. *"I clicked Cancel on an order that was already cancelled and the app happily said 'cancelled' again."*
7. *"I typed a wrong order number in the lookup box and got an 'Internal server error' banner."*
8. *"My colleague changed the number in a request and cancelled somebody else's order."*
9. *"A laptop in the warehouse was left logged in over the weekend, and on Monday it could still place orders."*
10. *"Staff in our Sydney office say the NYE30 promo stopped working hours before it should have."*
11. *Security review ticket:* "Please review how we handle credentials in the login code. No incident, just a periodic check."
12. *"After the last change the test project doesn't even build on my machine."*
## Scoring yourself

For each bug, did you: (a) reproduce it, (b) name the root cause, (c) fix it minimally, (d) check nothing else broke?
When you're done, compare with the `answer-key` branch.
