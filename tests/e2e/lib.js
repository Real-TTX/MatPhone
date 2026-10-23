// What the browser checks share: the people of the dev stack, signing in, the phone page, the audio counters.
// Needs the administrator of the dev stack (admin@example.com / Start12345, as in the integration tests) and the server address set under
// Telephony. The second person is made once (e2e-call@example.test, extension 290).

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

const launchOptions = {
  channel: 'msedge',
  headless: true,
  args: ['--use-fake-ui-for-media-stream', '--use-fake-device-for-media-stream', '--autoplay-policy=no-user-gesture-required'],
};

module.exports = { chromium, base, admin, callee, collectPeers, audioBytes, signIn, ensureCallee, openPhone, callState, launchOptions };
