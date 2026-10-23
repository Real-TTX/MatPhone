// A call between two browsers against a running stack: two people sign in, one dials the other, the other answers, both hear each other
// (the audio is counted on both sides), one hangs up. Then the first one calls the own mailbox and hears the prompt.
//
//   node call.js http://localhost:6050
//
// Needs the administrator of the dev stack (admin@example.com / Start12345, as in the integration tests) and the server address set under
// Telephony. The second person is made here once (e2e-call@example.test, extension 290). The microphone is the fake one of the browser (a beep).

const { chromium } = require('playwright-core');

const base = process.argv[2] || 'http://localhost:6050';
const admin = { email: 'admin@example.com', password: 'Start12345' };
const callee = { name: 'E2E Anruf', email: 'e2e-call@example.test', password: 'E2e-Anruf-Passwort-1', extension: '290' };

// Every peer connection of the page, to read its statistics.
const collectPeers = () => {
  const Original = window.RTCPeerConnection;
  window.__peers = [];
  window.RTCPeerConnection = function (...args) {
    const pc = new Original(...args);
    window.__peers.push(pc);
    return pc;
  };
  window.RTCPeerConnection.prototype = Original.prototype;
};

async function audioBytes(page) {
  return page.evaluate(async () => {
    let received = 0;
    let sent = 0;
    for (const pc of window.__peers || []) {
      const stats = await pc.getStats();
      stats.forEach((s) => {
        if (s.type === 'inbound-rtp' && s.kind === 'audio') received += s.bytesReceived || 0;
        if (s.type === 'outbound-rtp' && s.kind === 'audio') sent += s.bytesSent || 0;
      });
    }
    return { received, sent };
  });
}

async function signIn(context, who) {
  const page = await context.newPage();
  await page.goto(base + '/Account/Login');
  await page.fill('input[name="Input.Email"]', who.email);
  await page.fill('input[name="Input.Password"]', who.password);
  await Promise.all([page.waitForNavigation(), page.click('button[type="submit"]')]);
  if (page.url().includes('/Account/Login')) throw new Error('sign-in failed for ' + who.email);
  return page;
}

async function ensureCallee(page) {
  await page.goto(base + '/Admin/Users?q=' + encodeURIComponent(callee.email));
  if ((await page.content()).includes(callee.email)) return;
  await page.goto(base + '/Admin/Users/Edit');
  // the fields are on several tabs: set them directly
  await page.evaluate((c) => {
    const set = (name, value) => { document.querySelector('[name="' + name + '"]').value = value; };
    set('Input.DisplayName', c.name);
    set('Input.Email', c.email);
    set('Input.Password', c.password);
    set('Input.Extension', c.extension);
  }, callee);
  await Promise.all([page.waitForNavigation(), page.click('button.mat-btn--primary[type="submit"]')]);
  if (page.url().includes('/Edit') && (await page.$('.mat-field__error, .mat-field-error'))) throw new Error('the second person could not be created: ' + page.url());
}

async function openPhone(page) {
  await page.goto(base + '/Phone');
  await page.waitForFunction(() => document.querySelector('[data-phone-connection]')?.className.includes('--ok'), null, { timeout: 10000 });
}

const callState = (page) => page.$eval('[data-call] [data-call-state]', (e) => e.textContent.trim()).catch(() => null);

async function main() {
  const browser = await chromium.launch({
    channel: 'msedge',
    headless: true,
    args: ['--use-fake-ui-for-media-stream', '--use-fake-device-for-media-stream', '--autoplay-policy=no-user-gesture-required'],
  });

  const options = { permissions: ['microphone'], locale: 'de-DE' };
  const a = await browser.newContext(options);
  const b = await browser.newContext(options);
  await a.addInitScript(collectPeers);
  await b.addInitScript(collectPeers);

  const pa = await signIn(a, admin);
  await ensureCallee(pa);
  const pb = await signIn(b, callee);
  await openPhone(pa);
  await openPhone(pb);
  console.log('both phones are online');

  // 1. A calls B, B answers
  await pa.fill('[data-dial-input]', callee.extension);
  await pa.click('[data-dial-call]');
  await pb.waitForSelector('[data-call] [data-call-accept]:visible', { timeout: 10000 });
  console.log('B rings, A sees:', await callState(pa));
  await pb.click('[data-call] [data-call-accept]');
  await pa.waitForFunction(() => document.querySelector('[data-call] [data-call-timer]')?.textContent.includes(':'), null, { timeout: 10000 });
  await pb.waitForTimeout(3000);

  const audioA = await audioBytes(pa);
  const audioB = await audioBytes(pb);
  console.log('A audio', audioA, '/ B audio', audioB);
  if (audioA.received < 4000 || audioB.received < 4000) throw new Error('no audio in one direction');

  // 2. B hangs up: A's call ends
  await pb.click('[data-call] [data-call-hangup]');
  await pa.waitForTimeout(1500);
  console.log('after the hang-up A sees:', await callState(pa));

  // 3. A calls the own mailbox (*86): the server answers and plays its tones
  await pa.waitForTimeout(3000);
  await pa.evaluate(() => { window.__peers = []; });
  await pa.fill('[data-dial-input]', '*86');
  await pa.click('[data-dial-call]');
  // an empty mailbox plays its closing tones and hangs up by itself: the audio is counted while the call lasts
  let mailbox = { received: 0, sent: 0 };
  for (let i = 0; i < 40; i++) {
    await pa.waitForTimeout(200);
    const now = await audioBytes(pa);
    if (now.received >= mailbox.received) mailbox = now;
  }
  console.log('mailbox audio', mailbox, '- the call is now:', await callState(pa));
  if (mailbox.received < 2000) throw new Error('the mailbox sent no audio');

  await browser.close();
  console.log('OK');
}

main().catch((e) => {
  console.error('FAILED:', e.message);
  process.exit(1);
});
