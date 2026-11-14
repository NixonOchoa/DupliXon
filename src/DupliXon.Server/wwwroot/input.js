/*
 * DupliXon remote input: turns touches, trackpad/mouse and keyboard on the device into
 * mouse/keyboard events for the PC. Coordinates are normalized (0..1) to the visible video.
 *
 * Touch gestures
 *   tap ................ left click            long press ......... right click
 *   drag ............... click-and-drag        two-finger tap ..... right click
 *   two-finger drag .... scroll                ⌨ button ........... on-screen keyboard
 */
window.DupliXonInput = (() => {
  'use strict';
  const TAP_MS = 250, LONG_MS = 550, MOVE_PX = 10;

  function attach({ surface, video, keyboardInput, send }) {
    const touches = new Map();
    let mode = 'idle';            // idle | pending | drag | scroll | consumed
    let startAt = 0, longTimer = 0, lastScroll = null, twoStart = 0;
    const held = new Set();       // keys currently held down (released on blur)

    // ---- Geometry -------------------------------------------------------------------
    function point(clientX, clientY) {
      const r = video.getBoundingClientRect();
      const vw = video.videoWidth || r.width, vh = video.videoHeight || r.height;
      const scale = Math.min(r.width / vw, r.height / vh);
      const w = vw * scale, h = vh * scale;
      const left = r.left + (r.width - w) / 2, top = r.top + (r.height - h) / 2;
      return { x: clamp((clientX - left) / w), y: clamp((clientY - top) / h), scale };
    }
    const clamp = (v) => Math.min(1, Math.max(0, v));
    const at = (t, p, extra) => send(Object.assign({ t, x: p.x, y: p.y }, extra));

    // ---- Mouse / trackpad / Apple Pencil hover --------------------------------------
    surface.addEventListener('pointermove', (e) => {
      if (e.pointerType === 'touch') return onTouchMove(e);
      at('m', point(e.clientX, e.clientY));
    });
    surface.addEventListener('pointerdown', (e) => {
      if (e.target.closest('.hud, .handle')) return;
      e.preventDefault();
      surface.setPointerCapture?.(e.pointerId);
      if (e.pointerType === 'touch') return onTouchDown(e);
      at('d', point(e.clientX, e.clientY), { b: e.button === 2 ? 2 : 0 });
    });
    const up = (e) => {
      if (e.pointerType === 'touch') return onTouchUp(e);
      at('u', point(e.clientX, e.clientY), { b: e.button === 2 ? 2 : 0 });
    };
    surface.addEventListener('pointerup', up);
    surface.addEventListener('pointercancel', (e) => { if (e.pointerType === 'touch') reset(); });
    surface.addEventListener('contextmenu', (e) => e.preventDefault());
    surface.addEventListener('wheel', (e) => {
      e.preventDefault();
      const s = point(e.clientX, e.clientY).scale || 1;
      send({ t: 'w', dx: e.deltaX / s, dy: e.deltaY / s });
    }, { passive: false });

    // ---- Touch gestures ---------------------------------------------------------------
    function onTouchDown(e) {
      touches.set(e.pointerId, { x: e.clientX, y: e.clientY, sx: e.clientX, sy: e.clientY });
      if (touches.size === 1) {
        mode = 'pending';
        startAt = performance.now();
        at('m', point(e.clientX, e.clientY));
        clearTimeout(longTimer);
        longTimer = setTimeout(() => {
          if (mode !== 'pending') return;
          const p = point(e.clientX, e.clientY);
          at('d', p, { b: 2 }); at('u', p, { b: 2 });
          mode = 'consumed';
          navigator.vibrate?.(15);
        }, LONG_MS);
      } else if (touches.size === 2) {
        clearTimeout(longTimer);
        if (mode === 'drag') at('u', point(e.clientX, e.clientY), { b: 0 });
        mode = 'scroll';
        twoStart = performance.now();
        lastScroll = centroid();
      }
    }

    function onTouchMove(e) {
      const t = touches.get(e.pointerId);
      if (!t) return;
      t.x = e.clientX; t.y = e.clientY;

      if (mode === 'scroll' && touches.size >= 2) {
        const c = centroid();
        const s = point(c.x, c.y).scale || 1;
        const dx = (lastScroll.x - c.x) / s, dy = (lastScroll.y - c.y) / s;
        if (Math.abs(dx) + Math.abs(dy) > 2) { send({ t: 'w', dx, dy }); lastScroll = c; }
        return;
      }
      if (mode === 'pending' && Math.hypot(t.x - t.sx, t.y - t.sy) > MOVE_PX) {
        clearTimeout(longTimer);
        mode = 'drag';
        at('d', point(t.sx, t.sy), { b: 0 });
      }
      if (mode === 'drag') at('m', point(t.x, t.y));
    }

    function onTouchUp(e) {
      const t = touches.get(e.pointerId);
      touches.delete(e.pointerId);
      if (!t) return;
      const p = point(t.x, t.y);

      if (mode === 'scroll') {
        const quick = performance.now() - twoStart < TAP_MS + 100;
        const still = Math.hypot(t.x - t.sx, t.y - t.sy) < MOVE_PX;
        if (touches.size === 0) {
          if (quick && still) { at('d', p, { b: 2 }); at('u', p, { b: 2 }); }
          reset();
        }
        return;
      }
      clearTimeout(longTimer);
      if (mode === 'pending') { at('d', p, { b: 0 }); at('u', p, { b: 0 }); }
      else if (mode === 'drag') at('u', p, { b: 0 });
      if (touches.size === 0) reset();
    }

    function centroid() {
      let x = 0, y = 0;
      touches.forEach((t) => { x += t.x; y += t.y; });
      return { x: x / touches.size, y: y / touches.size };
    }
    function reset() { touches.clear(); mode = 'idle'; clearTimeout(longTimer); }

    // ---- Keyboard ---------------------------------------------------------------------
    const SENTINEL = '  ';
    let lastKeyDown = 0;
    keyboardInput.value = SENTINEL;

    const isPrintable = (e) => e.key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey;

    document.addEventListener('keydown', (e) => {
      if (!surface.isConnected || surface.closest('[hidden]')) return;
      if (e.target !== keyboardInput && e.target.tagName === 'INPUT') return;
      if (e.key === 'Unidentified' || e.key === 'Process' || e.isComposing) return; // let `input` handle it
      if (isPrintable(e)) {
        if (e.target === keyboardInput) return;             // soft keyboard: handled by `input`
        e.preventDefault();
        send({ t: 'txt', s: e.key });
        return;
      }
      e.preventDefault();
      lastKeyDown = performance.now();
      held.add(e.code);
      send({ t: 'k', c: e.code, d: true });
    });
    document.addEventListener('keyup', (e) => {
      if (!held.has(e.code)) return;
      held.delete(e.code);
      send({ t: 'k', c: e.code, d: false });
    });
    window.addEventListener('blur', releaseAll);

    keyboardInput.addEventListener('input', (e) => {
      const type = e.inputType || '';
      if (type.startsWith('insert') && e.data) send({ t: 'txt', s: e.data });
      else if (type === 'insertLineBreak' || type === 'insertParagraph') tap('Enter');
      else if (type.startsWith('delete') && performance.now() - lastKeyDown > 60) tap('Backspace');
      keyboardInput.value = SENTINEL;
    });

    function tap(code) { send({ t: 'k', c: code, d: true }); send({ t: 'k', c: code, d: false }); }
    function releaseAll() { held.forEach((c) => send({ t: 'k', c, d: false })); held.clear(); }

    return {
      toggleKeyboard() {
        if (document.activeElement === keyboardInput) keyboardInput.blur();
        else { keyboardInput.value = SENTINEL; keyboardInput.focus({ preventScroll: true }); }
      },
      releaseAll,
    };
  }

  return { attach };
})();
