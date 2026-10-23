// A call between two browsers against a running stack: two people sign in, one dials the other, the other answers, both hear each other
// (the audio is counted on both sides), one hangs up. Then the first one calls the own mailbox and hears the prompt. Last, the second person
// answers in a browser without any microphone.
//
//   node call.js http://localhost:6050
//
// The people and the setup: lib.js. The microphone is the fake one of the browser (a beep).

const { chromium, admin, callee, collectPeers, audioBytes, signIn, ensureCallee, openPhone, callState, launchOptions } = require('./lib');

async function main() {
  const browser = await chromium.launch(launchOptions);

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

  // 4. B again, now in a browser without any microphone: the call still works, B hears A
  await pa.waitForTimeout(2000);
  const plain = await chromium.launch({ channel: 'msedge', headless: true, args: ['--autoplay-policy=no-user-gesture-required'] });
  const c = await plain.newContext({ locale: 'de-DE' });
  await c.addInitScript(collectPeers);
  await c.addInitScript(() => { if (navigator.mediaDevices) navigator.mediaDevices.getUserMedia = () => Promise.reject(new DOMException('denied', 'NotAllowedError')); });
  await pb.close();
  const pc = await signIn(c, callee);
  await openPhone(pc);
  await pa.evaluate(() => { window.__peers = []; });
  await pa.fill('[data-dial-input]', callee.extension);
  await pa.click('[data-dial-call]');
  await pc.waitForSelector('[data-call] [data-call-accept]:visible', { timeout: 10000 });
  await pc.click('[data-call] [data-call-accept]');
  await pc.waitForFunction(() => document.querySelector('[data-call] [data-call-timer]')?.textContent.includes(':'), null, { timeout: 10000 });
  await pc.waitForTimeout(3000);
  const noMic = await audioBytes(pc);
  console.log('without a microphone', noMic);
  if (noMic.received < 4000) throw new Error('without a microphone nothing was heard');
  await pc.click('[data-call] [data-call-hangup]');
  await plain.close();

  await browser.close();
  console.log('OK');
}

main().catch((e) => {
  console.error('FAILED:', e.message);
  process.exit(1);
});
