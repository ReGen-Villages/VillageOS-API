// A console tab that counts. It signs in, opens one page, watches for a while, and writes how many
// requests the tab made per route and how many stream events it handled per kind — the numbers behind
// the claims about the console's request rate under a busy model, which only a tab can count.
//
//   node test/e2e/count-requests.mjs --url http://127.0.0.1:5099 --page / --seconds 60 --out counts.json
//
//   --url       the console's address (default http://localhost:5173)
//   --page      the page to open, as a path (default /, the platform dashboard)
//   --model     the model to pick when the sign-in offers several
//   --seconds   how long to watch once the page is open (default 60)
//   --until     a file whose appearance ends the watch instead; --seconds is then the longest wait
//   --out       where the JSON report goes (default stdout)
//   --headful   watch the browser
//
// Credentials from VOS_E2E_USERNAME / VOS_E2E_PASSWORD (default admin / admin), as the viewer test.

import fs from 'node:fs';
import puppeteer from 'puppeteer';
import { Tally } from './requestTally.mjs';

const chosen = parse(process.argv.slice(2));
const USERNAME = process.env.VOS_E2E_USERNAME || 'admin';
const PASSWORD = process.env.VOS_E2E_PASSWORD || 'admin';

function parse(argv) {
  const values = { url: 'http://localhost:5173', page: '/', model: null, seconds: 60, until: null, out: null, headful: false };
  for (let i = 0; i < argv.length; i++) {
    const name = argv[i].replace(/^--/, '');
    if (name === 'headful') values.headful = true;
    else if (name in values) values[name] = argv[++i];
    else throw new Error(`unknown argument: ${argv[i]}`);
  }
  values.seconds = Number(values.seconds);
  return values;
}

// Counts the stream events the page handles, by kind: every listener the console registers on an
// EventSource is wrapped so that an event it handles is counted under the kind it was registered
// for. Installed before the page's own scripts, since the streams open on sign-in.
function countStreamEvents() {
  window.__streamEvents = {};
  const register = EventSource.prototype.addEventListener;
  EventSource.prototype.addEventListener = function (kind, listener, options) {
    const counted = function (event) {
      window.__streamEvents[kind] = (window.__streamEvents[kind] ?? 0) + 1;
      return listener.call(this, event);
    };
    return register.call(this, kind, counted, options);
  };
}

async function signIn(page) {
  await page.goto(chosen.url, { waitUntil: 'domcontentloaded', timeout: 30000 });
  await page.waitForSelector('#username', { timeout: 10000 });
  await page.type('#username', USERNAME);
  await page.type('#password', PASSWORD);
  await Promise.all([
    page.waitForNavigation({ waitUntil: 'networkidle0', timeout: 15000 }).catch(() => {}),
    page.click('button[type=submit]'),
  ]);
  if (chosen.model) {
    const picked = await page.evaluate((name) => {
      const button = [...document.querySelectorAll('button')].find((b) => b.textContent.includes(name));
      if (!button) return false;
      button.click();
      return true;
    }, chosen.model);
    if (picked) await page.waitForNetworkIdle({ idleTime: 2000, timeout: 60000 }).catch(() => {});
  }
}

async function watch() {
  const deadline = Date.now() + chosen.seconds * 1000;
  while (Date.now() < deadline) {
    if (chosen.until && fs.existsSync(chosen.until)) return 'the file appeared';
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  return chosen.until ? 'the wait ran out before the file appeared' : 'the watch ended';
}

async function main() {
  const browser = await puppeteer.launch({
    headless: !chosen.headful,
    args: ['--ignore-certificate-errors', '--no-sandbox', '--disable-setuid-sandbox'],
  });
  const tally = new Tally();
  const pageErrors = [];
  try {
    const page = await browser.newPage();
    await page.setViewport({ width: 1280, height: 800 });
    await page.evaluateOnNewDocument(countStreamEvents);
    page.on('pageerror', (error) => pageErrors.push(String(error)));
    page.on('console', (message) => {
      if (message.type() === 'error') pageErrors.push(`[console.error] ${message.text()}`);
    });

    await signIn(page);
    // Counted from here: the sign-in's own requests are not the page's rate.
    page.on('request', (request) => tally.request(request.method(), request.url()));
    const startedAt = new Date().toISOString();
    await page.goto(new URL(chosen.page, chosen.url).href, { waitUntil: 'domcontentloaded' });
    const endedBecause = await watch();

    const handled = await page.evaluate(() => window.__streamEvents);
    for (const [kind, count] of Object.entries(handled ?? {}))
      for (let i = 0; i < count; i++) tally.streamEvent(kind);

    const report = {
      url: chosen.url,
      page: chosen.page,
      startedAt,
      endedAt: new Date().toISOString(),
      endedBecause,
      ...tally.report(),
      pageErrors,
    };
    const text = JSON.stringify(report, null, 2);
    if (chosen.out) fs.writeFileSync(chosen.out, text);
    else process.stdout.write(text + '\n');
  } finally {
    await browser.close();
  }
}

main().catch((failure) => {
  console.error(failure instanceof Error ? failure.message : failure);
  process.exit(1);
});
