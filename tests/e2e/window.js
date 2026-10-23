// The phone in its own window: the phone page opens it ("Own window") and steps aside; a call runs in the window while the main window goes from page
// to page; a "call back" link of another page puts the number into the window instead of leaving the page; "Phone here instead" takes the phone back.
//
//   node window.js http://localhost:6050
//
// The people and the setup: lib.js.

const { chromium, base, admin, callee, collectPeers, audioBytes, signIn, ensureCallee, openPhone, launchOptions } = require('./lib');

const connected = (page) => page.waitForFunction(() => document.querySelector('[data-call] [data-call-timer]')?.textContent.includes(':'), null, { timeout: 10000 });

async function main() {
  const browser = await chromium.launch(launchOptions);
  const options = { permissions: ['microphone'], locale: 'de-DE', viewport: { width: 1280, height: 860 } };
  const a = await browser.newContext(options);
  const b = await browser.newContext(options);
  await a.addInitScript(collectPeers);
  await b.addInitScript(collectPeers);

  const main = await signIn(a, admin);
  await ensureCallee(main);
  const pb = await signIn(b, callee);
  await openPhone(pb);
  await openPhone(main);

  // 1. the phone page opens the window and steps aside
  const [phone] = await Promise.all([a.waitForEvent('page'), main.click('[data-phone-popout]')]);
  await phone.waitForLoadState();
  await phone.waitForFunction(() => document.querySelector('[data-phone-connection]')?.className.includes('--ok'), null, { timeout: 10000 });
  await main.waitForSelector('[data-phone-elsewhere]:visible', { timeout: 5000 });
  if (!phone.url().includes('window=1')) throw new Error('the window shows ' + phone.url());
  if (!(await phone.$('[data-phone-window]')) || (await phone.$('main.mat-page'))) throw new Error('the window is the full phone page');
  console.log('the window is open, the phone page stepped aside');

  // 2. a call from the window; the main window goes from page to page
  await phone.fill('[data-dial-input]', callee.extension);
  await phone.click('[data-dial-call]');
  await pb.waitForSelector('[data-call] [data-call-accept]:visible', { timeout: 10000 });
  await pb.click('[data-call] [data-call-accept]');
  await connected(phone);
  for (const path of ['/', '/Calls', '/Admin/Users', '/Directory']) {
    await main.goto(base + path);
    await main.waitForTimeout(700);
  }

  const before = await audioBytes(phone);
  await phone.waitForTimeout(2000);
  const after = await audioBytes(phone);
  console.log('audio in the window while the main window moved:', before.received, '->', after.received);
  if (after.received - before.received < 8000) throw new Error('the call in the window lost its audio');

  // 3. "call" in the directory of the main window: the number goes into the window, the main window stays
  await main.waitForTimeout(500);
  const link = await main.$('a[href^="/Phone?dial="]');
  if (!link) throw new Error('no call link in the directory');
  const number = decodeURIComponent((await link.getAttribute('href')).split('dial=')[1]);
  await link.click();
  await phone.waitForFunction((n) => document.querySelector('[data-dial-input]').value === n, number, { timeout: 5000 });
  if (!main.url().endsWith('/Directory')) throw new Error('the main window left the page: ' + main.url());
  console.log('the call link put', number, 'into the window');

  // 4. hang up; "phone here instead" closes the window and the phone page takes over
  await phone.click('[data-call] [data-call-hangup]');
  await phone.waitForTimeout(500);
  await main.goto(base + '/Phone');
  await main.waitForSelector('[data-phone-elsewhere]:visible', { timeout: 5000 });
  await Promise.all([phone.waitForEvent('close', { timeout: 5000 }), main.click('[data-phone-take]')]);
  await main.waitForFunction(() => document.querySelector('[data-phone-connection]')?.className.includes('--ok'), null, { timeout: 10000 });
  console.log('the window closed, the phone page took over');

  await browser.close();
  console.log('OK');
}

main().catch((e) => {
  console.error('FAILED:', e.message);
  process.exit(1);
});
