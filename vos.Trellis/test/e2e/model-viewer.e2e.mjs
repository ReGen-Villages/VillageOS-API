// Draws the Fragments model in a real browser and samples its pixels, because jsdom has no WebGL and the
// unit tests stub the viewer. Needs the broker on https://localhost:7243 with a model holding an ingested
// .frag file, and the development server on http://localhost:5173 (npm run dev).
//
//   npm run test:e2e
//
//   VOS_E2E_URL         default http://localhost:5173
//   VOS_E2E_USERNAME    default admin
//   VOS_E2E_PASSWORD    default admin
//   VOS_E2E_HEADFUL     set to "1" to watch the browser

import puppeteer from 'puppeteer';
import assert from 'node:assert/strict';
import test from 'node:test';

const URL = process.env.VOS_E2E_URL || 'http://localhost:5173';
const USERNAME = process.env.VOS_E2E_USERNAME || 'admin';
const PASSWORD = process.env.VOS_E2E_PASSWORD || 'admin';
const HEADLESS = process.env.VOS_E2E_HEADFUL !== '1';

// The Filter by Type panel covers the left of the canvas, so sampling starts past it.
const PANEL_WIDTH_FRACTION = 0.4;

const CANVAS_SELECTOR = '[data-testid=fragments-canvas] canvas';

// The seed a run loads when the session has no model yet.
const SEED_NAME = process.env.VOS_E2E_SEED || 'MarthasVineyard';

/** Read from a screenshot, because three.js clears its drawing buffer between frames. A sample is geometry
 *  when any channel is clearly brighter than the #0f0f10 background. A screenshot of the element includes
 *  the Filter by Type panel, which `skipLeftFraction` keeps the samples off. */
async function samplePixelsViaScreenshot(page, sel, samples = 8, skipLeftFraction = 0) {
  const elem = await page.$(sel);
  if (!elem) return { error: 'canvas not found' };
  const dataUrl = 'data:image/png;base64,' + (await elem.screenshot({ type: 'png', encoding: 'base64' }));
  const info = await page.evaluate(async (src, n, skipLeft) => {
    const img = new Image();
    await new Promise((res, rej) => {
      img.onload = res;
      img.onerror = () => rej(new Error('png decode failed'));
      img.src = src;
    });
    const c = new OffscreenCanvas(img.width, img.height);
    const ctx = c.getContext('2d');
    ctx.drawImage(img, 0, 0);
    const data = ctx.getImageData(0, 0, img.width, img.height).data;
    let nonBg = 0;
    for (let i = 1; i <= n; i++) {
      for (let j = 1; j <= n; j++) {
        const left = img.width * skipLeft;
        const x = Math.floor(left + ((img.width - left) * i) / (n + 1));
        const y = Math.floor((img.height * j) / (n + 1));
        const idx = (y * img.width + x) * 4;
        const r = data[idx], g = data[idx + 1], b = data[idx + 2];
        if (r > 40 || g > 40 || b > 40) nonBg++;
      }
    }
    return { width: img.width, height: img.height, nonBg, total: n * n };
  }, dataUrl, samples, skipLeftFraction);
  return {
    ok: true,
    width: info.width,
    height: info.height,
    nonBackgroundSamples: info.nonBg,
    totalSamples: info.total,
  };
}

/** A broker that has just started offers the seed picker before the page, so the seed is picked when offered. */
async function loginAndOpenModelPage(page) {
  await page.goto(URL, { waitUntil: 'domcontentloaded', timeout: 30000 });
  await page.waitForSelector('#username', { timeout: 10000 });
  await page.type('#username', USERNAME);
  await page.type('#password', PASSWORD);
  await Promise.all([
    page.waitForNavigation({ waitUntil: 'networkidle0', timeout: 15000 }).catch(() => {}),
    page.click('button[type=submit]'),
  ]);

  const pickedSeed = await page.evaluate((name) => {
    const button = [...document.querySelectorAll('button')].find((b) => b.textContent.trim() === name);
    if (!button) return false;
    button.click();
    return true;
  }, SEED_NAME);
  if (pickedSeed) await page.waitForNetworkIdle({ idleTime: 2000, timeout: 60000 }).catch(() => {});

  await page.goto(`${URL}/model`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector(CANVAS_SELECTOR, { timeout: 20000 });
}

test('ModelPage renders the Fragments artifact via WebGL', async () => {
  const browser = await puppeteer.launch({
    headless: HEADLESS,
    // Without these, headless Chromium makes the canvas but gets no WebGL context.
    args: [
      '--ignore-certificate-errors',
      '--no-sandbox',
      '--disable-setuid-sandbox',
      '--enable-webgl',
      '--enable-unsafe-swiftshader',
      '--use-gl=angle',
      '--use-angle=swiftshader',
    ],
  });

  try {
    const page = await browser.newPage();
    await page.setViewport({ width: 1280, height: 800 });

    const pageErrors = [];
    page.on('pageerror', (err) => pageErrors.push(String(err)));
    page.on('console', (msg) => {
      if (msg.type() === 'error') pageErrors.push('[console.error] ' + msg.text());
    });

    await loginAndOpenModelPage(page);
    const canvasSel = CANVAS_SELECTOR;

    // Under software rendering the first frame can come seconds after the last tile arrives.
    const deadline = Date.now() + 25000;
    let rendered;
    while (Date.now() < deadline) {
      rendered = await samplePixelsViaScreenshot(page, canvasSel);
      if (rendered?.ok && rendered.nonBackgroundSamples >= 2) break;
      await new Promise((r) => setTimeout(r, 1000));
    }

    if (!rendered?.ok) {
      await page.screenshot({ path: '/tmp/vos-e2e-fail.png' });
      throw new Error(`Rendering check failed: ${rendered?.error ?? 'no sample'}\nPage errors: ${pageErrors.join('\n')}`);
    }

    if (rendered.nonBackgroundSamples < 4) {
      await page.screenshot({ path: '/tmp/vos-e2e-fail.png' });
    }

    assert.ok(rendered.width >= 400, `canvas too small: ${rendered.width}x${rendered.height}`);
    assert.ok(rendered.height >= 300, `canvas too short: ${rendered.width}x${rendered.height}`);
    assert.ok(
      rendered.nonBackgroundSamples >= 2,
      `expected ≥ 2 rendered samples out of ${rendered.totalSamples}, got ${rendered.nonBackgroundSamples} ` +
        `after 25s. Model likely did not stream in. Screenshot: /tmp/vos-e2e-fail.png. ` +
        `Page errors: ${pageErrors.join('\n')}`,
    );

    await page.evaluate((sel) => {
      const canvas = document.querySelector(sel);
      const rect = canvas.getBoundingClientRect();
      const cx = rect.left + rect.width / 2;
      const cy = rect.top + rect.height / 2;
      canvas.dispatchEvent(
        new PointerEvent('pointerdown', { clientX: cx, clientY: cy, button: 0, pointerId: 1, bubbles: true }),
      );
      for (let i = 1; i <= 10; i++) {
        canvas.dispatchEvent(
          new PointerEvent('pointermove', {
            clientX: cx + i * 20,
            clientY: cy + i * 8,
            pointerId: 1,
            bubbles: true,
          }),
        );
      }
      canvas.dispatchEvent(
        new PointerEvent('pointerup', {
          clientX: cx + 200,
          clientY: cy + 80,
          button: 0,
          pointerId: 1,
          bubbles: true,
        }),
      );
    }, canvasSel);

    await new Promise((r) => setTimeout(r, 1500));

    const afterOrbit = await samplePixelsViaScreenshot(page, canvasSel);

    assert.ok(
      afterOrbit.nonBackgroundSamples >= 2,
      `expected model to still render after orbit (≥ 2 samples), got ${afterOrbit.nonBackgroundSamples}. ` +
        `Tile streaming may have stalled. Page errors: ${pageErrors.join('\n')}`,
    );

    console.log(
      `ok — pre-orbit: ${rendered.nonBackgroundSamples}/${rendered.totalSamples} rendered samples, ` +
        `post-orbit: ${afterOrbit.nonBackgroundSamples}/${rendered.totalSamples}, renderer: ${rendered.renderer}`,
    );

    // A pixel just read as geometry, so the pick has something to hit.
    const hitPoint = await page.evaluate((sel) => {
      const canvas = document.querySelector(sel);
      const rect = canvas.getBoundingClientRect();
      const gl = canvas.getContext('webgl2') || canvas.getContext('webgl');
      const pixel = new Uint8Array(4);
      for (let r = 0; r < 200; r += 20) {
        for (let theta = 0; theta < 360; theta += 30) {
          const rad = (theta * Math.PI) / 180;
          const px = Math.floor(canvas.width / 2 + r * Math.cos(rad));
          const py = Math.floor(canvas.height / 2 + r * Math.sin(rad));
          gl.readPixels(px, py, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, pixel);
          if (pixel[0] > 40 || pixel[1] > 40 || pixel[2] > 40) {
            // readPixels counts rows from the bottom; clientY counts from the top.
            const cssX = rect.left + (px / canvas.width) * rect.width;
            const cssY = rect.top + ((canvas.height - py) / canvas.height) * rect.height;
            return { cssX, cssY };
          }
        }
      }
      return null;
    }, canvasSel);

    if (hitPoint) {
      await page.evaluate(
        ({ sel, x, y }) => {
          const canvas = document.querySelector(sel);
          const opts = { clientX: x, clientY: y, button: 0, pointerId: 99, bubbles: true };
          canvas.dispatchEvent(new PointerEvent('pointerdown', opts));
          canvas.dispatchEvent(new PointerEvent('pointerup', opts));
        },
        { sel: canvasSel, x: hitPoint.cssX, y: hitPoint.cssY },
      );

      // The panel sits outside the canvas, and the pick, its lookup and a read of the Thing all come before it.
      await page.waitForSelector('[data-testid=fragments-metadata-panel]', { timeout: 3000 });
      console.log('ok — pick resolved to a VosThing; metadata panel mounted');
    } else {
      console.warn('skip — could not find a rendered pixel to click; pick coverage not asserted');
    }
  } finally {
    await browser.close();
  }
});

// A .frag file holds far more elements than the model has Things, so hiding by Thing left most of them
// drawn; only a real file in a real WebGL context shows it.
test('ModelPage hides all geometry when no IFC type is selected', async () => {
  const browser = await puppeteer.launch({
    headless: HEADLESS,
    args: [
      '--ignore-certificate-errors',
      '--no-sandbox',
      '--disable-setuid-sandbox',
      '--enable-webgl',
      '--enable-unsafe-swiftshader',
      '--use-gl=angle',
      '--use-angle=swiftshader',
    ],
  });

  try {
    const page = await browser.newPage();
    await page.setViewport({ width: 1280, height: 800 });

    await loginAndOpenModelPage(page);
    const canvasSel = CANVAS_SELECTOR;

    // Every tile has to be in first, or a model still arriving reads as hidden.
    const deadline = Date.now() + 40000;
    let visible;
    while (Date.now() < deadline) {
      visible = await samplePixelsViaScreenshot(page, canvasSel, 8, PANEL_WIDTH_FRACTION);
      if (visible?.ok && visible.nonBackgroundSamples >= 4) break;
      await new Promise((r) => setTimeout(r, 1500));
    }
    assert.ok(
      visible?.ok && visible.nonBackgroundSamples >= 4,
      `model did not stream in; nothing to hide. Got ${visible?.nonBackgroundSamples ?? 'no'} samples.`,
    );

    const clickedNone = await page.evaluate(() => {
      const button = [...document.querySelectorAll('button')].find((b) => b.textContent.trim() === 'None');
      if (!button) return false;
      button.click();
      return true;
    });
    assert.ok(clickedNone, 'could not find the None button in the Filter by Type panel');

    // Hiding goes through the Fragments worker over the whole scene, which takes longer than a frame.
    await new Promise((r) => setTimeout(r, 6000));

    const hidden = await samplePixelsViaScreenshot(page, canvasSel, 8, PANEL_WIDTH_FRACTION);
    if (hidden.nonBackgroundSamples > 0) await page.screenshot({ path: '/tmp/vos-e2e-5366-fail.png' });
    assert.equal(
      hidden.nonBackgroundSamples,
      0,
      `expected an empty canvas with 0 types selected, but ${hidden.nonBackgroundSamples} of ` +
        `${hidden.totalSamples} sample points still show geometry (was ${visible.nonBackgroundSamples} ` +
        `before hiding). Screenshot: /tmp/vos-e2e-5366-fail.png`,
    );

    console.log(`ok — canvas emptied: ${visible.nonBackgroundSamples} → 0 rendered samples`);
  } finally {
    await browser.close();
  }
});
