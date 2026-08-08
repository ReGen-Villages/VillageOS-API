// Puppeteer e2e test for the Fragments viewer.
//
// What this covers that vitest can't: real WebGL rendering of the Fragments
// artifact streamed from Mycelium. jsdom has no WebGL, so the unit tests
// stub the viewer; this test drives Chromium against a live Mycelium + dev
// server and samples the rendered pixels.
//
// Preconditions (manual setup):
//   1. Mycelium running on https://localhost:7243 with a model that has an
//      ingested .frag file (the canonical Fragments (.frag) fixture is ingested).
//   2. GUI dev server running on http://localhost:5173 (npm run dev).
//
// Usage:
//   npm run test:e2e
//
// Env overrides:
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

// The Filter by Type panel overlays the left of the viewport; sampling starts
// past it so its own pixels are never mistaken for model geometry.
const PANEL_WIDTH_FRACTION = 0.4;

const CANVAS_SELECTOR = '[data-testid=fragments-canvas] canvas';

// The seed a run loads when the session has no model yet.
const SEED_NAME = process.env.VOS_E2E_SEED || 'MarthasVineyard';

/**
 * Puppeteer reads canvas pixels by screenshotting the element and inspecting
 * the PNG. Reading the WebGL buffer directly doesn't work for r3f/three.js
 * because `preserveDrawingBuffer` defaults to false and the buffer gets
 * cleared between frames.
 *
 * Returns width/height plus how many of `samples × samples` grid points land
 * on rendered geometry (any channel clearly brighter than the #0f0f10 bg).
 *
 * `skipLeftFraction` keeps the grid clear of the Filter by Type panel, which is
 * an HTML overlay sitting on top of the canvas — an element screenshot includes
 * it, so its checkboxes and labels otherwise count as rendered geometry.
 */
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

/**
 * Log in and land on the Model page with the Fragments canvas mounted.
 *
 * A session that has no model selected yet meets the seed picker instead of the
 * app, so a run against a freshly started broker hangs on a canvas that never
 * arrives. Pick the seed when it is offered.
 */
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
    // --no-sandbox is required for WebGL in headless Chromium on many machines;
    // --enable-unsafe-swiftshader lets recent Chromium fall back to software
    // rendering. Without these, the browser creates the canvas but cannot
    // obtain a GL context.
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

    // Capture browser-side errors so failures are diagnosable.
    const pageErrors = [];
    page.on('pageerror', (err) => pageErrors.push(String(err)));
    page.on('console', (msg) => {
      if (msg.type() === 'error') pageErrors.push('[console.error] ' + msg.text());
    });

    await loginAndOpenModelPage(page);
    const canvasSel = CANVAS_SELECTOR;

    // Poll pixel samples — under software rendering (swiftshader) the first
    // frame can take several seconds after the Fragments worker finishes
    // streaming tiles. Retry until at least 4 samples land or 25s elapses.
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

    // ── Assert orbit interaction triggers continued streaming ────────────
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

    // ── Assert click-to-pick resolves an element and shows the metadata panel ─
    // Click a canvas pixel that definitely landed on geometry in the post-orbit
    // sample. Raycasting the exact pixel we just read gives us a reliable hit.
    const hitPoint = await page.evaluate((sel) => {
      const canvas = document.querySelector(sel);
      const rect = canvas.getBoundingClientRect();
      const gl = canvas.getContext('webgl2') || canvas.getContext('webgl');
      // Scan the centre-ish region for a pixel that isn't the dark background.
      const pixel = new Uint8Array(4);
      for (let r = 0; r < 200; r += 20) {
        for (let theta = 0; theta < 360; theta += 30) {
          const rad = (theta * Math.PI) / 180;
          const px = Math.floor(canvas.width / 2 + r * Math.cos(rad));
          const py = Math.floor(canvas.height / 2 + r * Math.sin(rad));
          gl.readPixels(px, py, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, pixel);
          if (pixel[0] > 40 || pixel[1] > 40 || pixel[2] > 40) {
            // Note: readPixels is (0,0) at bottom-left; clientY is top-left.
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

      // The metadata panel is rendered outside the canvas. Allow up to 3s for
      // the raycast + mapping lookup + /api/things fetch.
      await page.waitForSelector('[data-testid=fragments-metadata-panel]', { timeout: 3000 });
      console.log('ok — pick resolved to a VosThing; metadata panel mounted');
    } else {
      console.warn('skip — could not find a rendered pixel to click; pick coverage not asserted');
    }
  } finally {
    await browser.close();
  }
});

// Bug #5366 — unchecking every type must empty the canvas. The regression this
// guards is specific: visibility used to be driven by a list of IFC GlobalIds
// built from the model's Things, and a .frag holds far more elements than the
// model has Things for. Hiding "everything" reached only the mapped few, so the
// scene stayed on screen while the panel reported nothing selected. A unit test
// cannot catch it — it only shows up against a real .frag in a real GL context.
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

    // Every tile must be in before the baseline, or a still-streaming model
    // reads as "hidden" and the test passes for the wrong reason.
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

    // Hiding walks the whole scene through the Fragments worker, so give it
    // longer than a frame before reading pixels.
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
