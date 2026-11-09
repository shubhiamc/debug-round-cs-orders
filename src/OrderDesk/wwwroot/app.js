// Tiny front-end for OrderDesk. It only talks to /api/*.
// Symptoms you see here are caused by the server; there is nothing to fix in this file.

let token = sessionStorage.getItem('token') || null;

const $ = (id) => document.getElementById(id);
const money = (n) => Number(n).toFixed(2);

function banner(message, ok) {
  const el = $('banner');
  el.textContent = message;
  el.className = ok ? 'ok' : 'err';
}

async function api(method, path, body) {
  const headers = { 'Content-Type': 'application/json' };
  if (token) headers['Authorization'] = 'Bearer ' + token;
  const res = await fetch('/api' + path, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  let data = null;
  try { data = await res.json(); } catch { /* empty body */ }
  return { ok: res.ok, status: res.status, data };
}

function fail(prefix, r) {
  const detail = (r.data && r.data.error) || 'request failed';
  banner(`${prefix}: ${r.status} ${detail}`, false);
}

async function register() {
  const r = await api('POST', '/register', { username: $('username').value, password: $('password').value });
  r.ok ? banner('Registered. You can log in now.', true) : fail('Register', r);
}

async function login() {
  const r = await api('POST', '/login', { username: $('username').value, password: $('password').value });
  if (!r.ok) return fail('Login', r);
  token = r.data.token;
  sessionStorage.setItem('token', token);
  $('who').textContent = 'Signed in as ' + $('username').value;
  banner('Logged in.', true);
  await refreshAll();
}

function logout() {
  token = null;
  sessionStorage.removeItem('token');
  $('who').textContent = 'Not signed in';
  $('products').innerHTML = '';
  $('orders').innerHTML = '';
  banner('Logged out.', true);
}

async function loadProducts() {
  const r = await api('GET', '/products');
  if (!r.ok) return fail('Products', r);
  $('products').innerHTML = r.data
    .map((p) => `<tr><td>${p.id}</td><td>${p.name}</td><td>${money(p.price)}</td><td>${p.stock}</td></tr>`)
    .join('');
  $('order-product').innerHTML = r.data.map((p) => `<option value="${p.id}">${p.name}</option>`).join('');
}

async function loadOrders() {
  const status = $('filter').value;
  const r = await api('GET', '/orders' + (status ? '?status=' + status : ''));
  if (!r.ok) return fail('Orders', r);
  $('orders').innerHTML = r.data
    .map((o) => {
      const items = o.lines.map((l) => `${l.qty} x ${l.productName}`).join(', ');
      return `<tr>
        <td>${o.id}</td><td>${items}</td>
        <td>${money(o.subtotal)}</td><td>${money(o.discount)}</td><td>${money(o.tax)}</td>
        <td><strong>${money(o.total)}</strong></td>
        <td class="${o.status}">${o.status}</td>
        <td><button data-cancel="${o.id}">Cancel</button></td>
      </tr>`;
    })
    .join('');
}

async function placeOrder() {
  const productId = Number($('order-product').value);
  const qty = Number($('order-qty').value);
  const couponCode = $('order-coupon').value || null;
  const r = await api('POST', '/orders', { items: [{ productId, qty }], couponCode });
  if (!r.ok) return fail('Place order', r);
  banner(`Order #${r.data.id} placed. Total ${money(r.data.total)}`, true);
  await refreshAll();
}

async function cancelOrder(id) {
  const r = await api('POST', `/orders/${id}/cancel`);
  if (!r.ok) return fail('Cancel', r);
  banner(`Order #${id} cancelled.`, true);
  await refreshAll();
}

async function lookup() {
  const id = $('lookup-id').value;
  const r = await api('GET', '/orders/' + id);
  if (!r.ok) {
    $('lookup-result').textContent = '';
    return fail('Look up', r);
  }
  $('lookup-result').textContent = JSON.stringify(r.data, null, 2);
}

async function refreshAll() {
  await loadProducts();
  await loadOrders();
}

$('btn-register').onclick = register;
$('btn-login').onclick = login;
$('btn-logout').onclick = logout;
$('btn-order').onclick = placeOrder;
$('btn-refresh').onclick = loadOrders;
$('btn-lookup').onclick = lookup;
$('filter').onchange = loadOrders;
$('orders').addEventListener('click', (e) => {
  const id = e.target.getAttribute && e.target.getAttribute('data-cancel');
  if (id) cancelOrder(id);
});

if (token) {
  $('who').textContent = 'Signed in (restored session)';
  refreshAll();
}
