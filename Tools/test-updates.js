#!/usr/bin/env node
// Tests the new real-time Cast UPDATE handlers (add_fish, remove_fish, change_bg, change_sub, change_light).
// Injects fake UPDATE messages via JS eval and checks browser console logs for expected C# responses.

const puppeteer = require('puppeteer-core');

const CHROME = process.env.CHROME_PATH ||
  'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const URL = 'http://localhost:3001/?devtest=1';

function sleep(ms) { return new Promise(r => setTimeout(r, ms)); }

(async () => {
  const consoleLogs = [];
  const browser = await puppeteer.launch({
    executablePath: CHROME,
    headless: true,
    args: ['--no-sandbox','--disable-setuid-sandbox','--use-gl=swiftshader',
           '--disable-features=VizDisplayCompositor'],
  });

  const page = await browser.newPage();
  page.on('console', m => {
    const text = m.text();
    consoleLogs.push(text);
    if (text.includes('[C#]') || text.includes('AQUARIUM') || text.includes('UPDATE'))
      console.log(`[browser] ${text}`);
  });

  await page.goto(URL, { waitUntil: 'networkidle0', timeout: 60000 });

  console.log('Waiting for AQUARIUM READY...');
  await page.waitForFunction(
    () => typeof dbg === 'function' || true, // ensure page loaded
    { timeout: 10000 }
  );

  // Wait until C# logs AQUARIUM READY
  const deadline = Date.now() + 40000;
  while (!consoleLogs.some(l => l.includes('AQUARIUM READY')) && Date.now() < deadline)
    await sleep(300);

  if (!consoleLogs.some(l => l.includes('AQUARIUM READY'))) {
    console.error('❌ AQUARIUM READY never received');
    await browser.close();
    process.exit(1);
  }
  console.log('✅ AQUARIUM READY\n');
  await sleep(500);

  // Send a Cast UPDATE message via JS eval
  async function sendUpdate(type, value) {
    await page.evaluate((_type, _value) => {
      var payload = JSON.stringify({ type: _type, value: _value });
      var msg = JSON.stringify({ type: 'UPDATE', payload: payload });
      if (window.unityInstance)
        window.unityInstance.SendMessage('CastReceiver', 'OnMessageReceived', msg);
    }, type, value);
  }

  // Wait for a string to appear in the collected console logs
  async function waitForLog(pattern, timeoutMs = 10000) {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
      if (consoleLogs.some(l => l.includes(pattern))) return true;
      await sleep(200);
    }
    return false;
  }

  const results = [];

  // ── TEST 1: add_fish (not in INIT catalog — requires bundle download from R2) ─
  console.log('TEST 1: add_fish (moorish_idol, loads from R2)...');
  await sendUpdate('add_fish', JSON.stringify({ speciesId: 'fish_moorish_idol', nickname: 'TestFish' }));
  const t1 = await waitForLog('add_fish: fish_moorish_idol', 15000); // bundle download can take a few seconds
  console.log(`  ${t1 ? '✅' : '❌'} add_fish`);
  results.push({ test: 'add_fish', pass: t1 });
  await sleep(300);

  // ── TEST 2: remove_fish ──────────────────────────────────────────────────────
  console.log('TEST 2: remove_fish (moorish_idol)...');
  await sendUpdate('remove_fish', 'fish_moorish_idol');
  const t2 = await waitForLog('remove_fish: fish_moorish_idol');
  console.log(`  ${t2 ? '✅' : '❌'} remove_fish`);
  results.push({ test: 'remove_fish', pass: t2 });
  await sleep(200);

  // ── TEST 3: change_bg ────────────────────────────────────────────────────────
  console.log('TEST 3: change_bg (bg_ocean, sync — no bundle)...');
  await sendUpdate('change_bg', 'bg_ocean');
  const t3 = await waitForLog('change_bg: bg_ocean');
  console.log(`  ${t3 ? '✅' : '❌'} change_bg`);
  results.push({ test: 'change_bg', pass: t3 });
  await sleep(200);

  // ── TEST 4: change_sub ───────────────────────────────────────────────────────
  console.log('TEST 4: change_sub (sub_sand, sync — no bundle)...');
  await sendUpdate('change_sub', 'sub_sand');
  const t4 = await waitForLog('change_sub: sub_sand');
  console.log(`  ${t4 ? '✅' : '❌'} change_sub`);
  results.push({ test: 'change_sub', pass: t4 });
  await sleep(200);

  // ── TEST 5: change_light ─────────────────────────────────────────────────────
  console.log('TEST 5: change_light (light_blue, sync — no bundle)...');
  await sendUpdate('change_light', 'light_blue');
  const t5 = await waitForLog('change_light: light_blue');
  console.log(`  ${t5 ? '✅' : '❌'} change_light`);
  results.push({ test: 'change_light', pass: t5 });
  await sleep(200);

  // ── TEST 6: re-add removed fish (tests reuse of existing catalog entry) ───────
  console.log('TEST 6: re-add banggai (already in INIT catalog, no bundle reload)...');
  await sendUpdate('add_fish', JSON.stringify({ speciesId: 'fish_banggai_cardinalfish', nickname: 'Banggai2' }));
  const t6 = await waitForLog('add_fish: fish_banggai_cardinalfish');
  console.log(`  ${t6 ? '✅' : '❌'} add_fish (catalog reuse)`);
  results.push({ test: 'add_fish_catalog_reuse', pass: t6 });

  await page.screenshot({ path: 'test-updates-result.png' });
  console.log('\nScreenshot: test-updates-result.png');
  await browser.close();

  const passed = results.filter(r => r.pass).length;
  console.log(`\n${'─'.repeat(40)}`);
  console.log(`Result: ${passed}/${results.length} passed`);
  results.forEach(r => console.log(`  ${r.pass ? '✅' : '❌'} ${r.test}`));
  if (passed < results.length) process.exit(1);
})().catch(e => { console.error(e); process.exit(1); });
