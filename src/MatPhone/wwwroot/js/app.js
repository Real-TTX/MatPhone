/* MatPhone: the behaviour of the app itself. MatUi (wwwroot/ui/matui.js) does menus, dialogs, tabs, validation and pickers; this file adds what is specific to
   MatPhone: the installed app (service worker, colour of the status bar), the behaviour without a connection, the badge of the mailbox, the editor of rows
   (function keys), the recording of a greeting with the microphone, secrets that are shown on request, and pages that refresh themselves. The web client is
   phone.js. Everything is wired by data attributes; no inline styles anywhere. */
(function () {
  'use strict';

  var doc = document;
  var root = doc.documentElement;
  var $ = function (sel, scope) { return (scope || doc).querySelector(sel); };
  var $$ = function (sel, scope) { return Array.prototype.slice.call((scope || doc).querySelectorAll(sel)); };
  var csrf = function () { var m = $('meta[name="mp-csrf"]'); return m ? m.getAttribute('content') : ''; };
  // The texts in the language of the page: the JSON block #mp-texts holds every key "Js.*" (Resources/Texts/Js.json). t('Js.Key', a, b) fills {0}, {1}.
  var texts = (function () { var el = doc.getElementById('mp-texts'); try { return el ? JSON.parse(el.textContent || '{}') : {}; } catch (e) { return {}; } })();
  var t = function (key) {
    var args = Array.prototype.slice.call(arguments, 1);
    var text = Object.prototype.hasOwnProperty.call(texts, key) ? texts[key] : key;
    return text.replace(/\{(\d+)\}/g, function (m, i) { return args[+i] !== undefined ? String(args[+i]) : m; });
  };
  var toast = function (message, tone) { if (window.MatUi && window.MatUi.toast) { window.MatUi.toast(message, { tone: tone || 'danger' }); } };
  var locale = root.getAttribute('data-locale') || 'en-GB';
  var shortDate = function (d) { return d.toLocaleDateString(locale); };
  var shortTime = function (d) { return d.toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit' }); };

  // ================================================================================================ the installed app

  // The colour of the status bar follows the theme the person chose in the app (the meta tags follow the system by themselves).
  function syncThemeColor() {
    var theme = root.getAttribute('data-theme');
    $$('meta[data-theme-color]').forEach(function (m) {
      if (!m.hasAttribute('data-media')) { m.setAttribute('data-media', m.getAttribute('media') || ''); }
      if (theme === 'light' || theme === 'dark') {
        if (m.getAttribute('data-theme-color') === theme) { m.setAttribute('media', 'all'); } else { m.setAttribute('media', 'not all'); }
      } else {
        m.setAttribute('media', m.getAttribute('data-media'));
      }
    });
  }
  syncThemeColor();
  root.addEventListener('mat:theme', syncThemeColor);

  var sw = navigator.serviceWorker;
  if (sw && root.getAttribute('data-sw')) {
    window.addEventListener('load', function () {
      sw.register('/sw.js?v=' + encodeURIComponent(root.getAttribute('data-sw')), { scope: '/' }).catch(function () { /* the app works without it */ });
    });
  }

  // The kept pages hold the data of the person who was signed in: they go with signing out, and on the sign-in page.
  function clearKeptPages() {
    if (sw && sw.controller) { sw.controller.postMessage({ type: 'clear-pages' }); }
    if (window.caches) { caches.delete('mp-pages').catch(function () { }); }
  }
  if (root.hasAttribute('data-clear-kept')) { clearKeptPages(); }
  doc.addEventListener('submit', function (e) {
    if (e.target && e.target.getAttribute && /\/Account\/Logout$/i.test(e.target.getAttribute('action') || '')) { clearKeptPages(); }
  }, true);

  // ================================================================================================ without a connection

  var banner = $('[data-offline-banner]');
  var copyFrom = root.getAttribute('data-offline-copy');
  var retryTimer = null;
  var retryDelay = 3000;

  function setOffline(offline) {
    root.classList.toggle('is-offline', offline);
    if (banner) { banner.hidden = !offline && copyFrom === null; }
  }

  function ping() {
    return fetch('/health', { cache: 'no-store', credentials: 'same-origin' }).then(function (r) { return r.ok; }).catch(function () { return false; });
  }

  function back() {
    clearTimeout(retryTimer);
    retryDelay = 3000;
    setOffline(false);
    // A copy that was shown without a connection is replaced by the real page: nothing stale stays on the screen.
    if (copyFrom !== null || $('[data-offline-page]')) { location.reload(); return; }
    toast(t('Js.Connection.Back'), 'success');
    refreshBadge();
  }

  function scheduleRetry() {
    clearTimeout(retryTimer);
    retryTimer = setTimeout(function () {
      ping().then(function (ok) {
        if (ok) { back(); } else { retryDelay = Math.min(retryDelay * 2, 60000); scheduleRetry(); }
      });
    }, retryDelay);
  }

  function reconnectNow(button) {
    var status = $('[data-offline-status]');
    if (button) { button.disabled = true; }
    if (status) { status.textContent = t('Js.Connection.Connecting'); }
    ping().then(function (ok) {
      if (button) { button.disabled = false; }
      if (ok) { back(); return; }
      if (status) { status.textContent = t('Js.Connection.StillOffline'); }
      toast(t('Js.Connection.StillOfflineToast'), 'warning');
      scheduleRetry();
    });
  }

  window.addEventListener('offline', function () { setOffline(true); scheduleRetry(); });
  window.addEventListener('online', function () { ping().then(function (ok) { if (ok) { back(); } else { scheduleRetry(); } }); });
  doc.addEventListener('click', function (e) {
    var b = e.target.closest && e.target.closest('[data-reconnect]');
    if (b) { e.preventDefault(); reconnectNow(b); }
  });

  if (copyFrom !== null) {
    var text = $('[data-offline-text]');
    var when = copyFrom ? new Date(copyFrom) : null;
    if (text && when && !isNaN(when)) {
      text.textContent = t('Js.Connection.CopyFrom', shortDate(when), shortTime(when));
    }
    setOffline(true);
    scheduleRetry();
  } else if (!navigator.onLine) {
    setOffline(true);
    scheduleRetry();
  }
  if ($('[data-offline-page]')) { scheduleRetry(); }

  // A form that is sent without a connection would end in the browser's error page and lose what was typed: keep the page, say why.
  doc.addEventListener('submit', function (e) {
    var form = e.target;
    if (!form || (form.method || '').toLowerCase() !== 'post') { return; }
    if (!navigator.onLine || root.classList.contains('is-offline')) {
      e.preventDefault();
      e.stopImmediatePropagation();
      toast(t('Js.Connection.NotSaved'), 'warning');
      scheduleRetry();
    }
  }, true);

  // ================================================================================================ the badge of the mailbox

  function refreshBadge() {
    var bell = $('.mp-bell');
    if (!bell || doc.hidden || !navigator.onLine) { return; }
    fetch('/api/voicemail/count', { credentials: 'same-origin', cache: 'no-store' }).then(function (r) { return r.ok ? r.json() : null; }).then(function (data) {
      if (!data) { return; }
      var badge = $('.mp-bell__count', bell);
      if (data.unread > 0) {
        if (!badge) { badge = doc.createElement('span'); badge.className = 'mat-badge mp-bell__count'; badge.setAttribute('aria-hidden', 'true'); bell.appendChild(badge); }
        badge.textContent = data.unread > 99 ? '99+' : String(data.unread);
        bell.setAttribute('aria-label', t('Js.Voicemail.Unread', data.unread));
      } else if (badge) {
        badge.parentNode.removeChild(badge);
        bell.setAttribute('aria-label', t('Js.Voicemail.Label'));
      }
    }).catch(function () { });
  }
  setInterval(refreshBadge, 60000);
  doc.addEventListener('visibilitychange', function () { if (!doc.hidden) { refreshBadge(); } });

  // A message that is played counts as heard: the badge follows.
  doc.addEventListener('play', function (e) {
    if (e.target && e.target.hasAttribute && e.target.hasAttribute('data-heard-on-play')) {
      var item = e.target.closest('.mp-message');
      if (item) { item.classList.remove('is-new'); }
      setTimeout(refreshBadge, 1500);
    }
  }, true);

  // ================================================================================================ small helpers

  // <select data-submit-on-change> sends its form when it changes.
  doc.addEventListener('change', function (event) {
    var target = event.target;
    if (target && target.hasAttribute && target.hasAttribute('data-submit-on-change') && target.form) { target.form.requestSubmit ? target.form.requestSubmit() : target.form.submit(); }
  });

  // A secret (the password of a phone) is shown as dots until [data-reveal] next to it is pressed.
  doc.addEventListener('click', function (e) {
    var b = e.target.closest && e.target.closest('[data-reveal]');
    if (!b) { return; }
    var secret = b.parentNode && $('[data-secret]', b.parentNode);
    if (!secret) { return; }
    var shown = secret.getAttribute('data-shown') === 'true';
    secret.textContent = shown ? '••••••••••••' : secret.getAttribute('data-secret');
    secret.setAttribute('data-shown', shown ? 'false' : 'true');
  });

  // A status page reloads itself every n seconds (data-auto-refresh), only while it is in front and nothing is being typed.
  var refresh = $('[data-auto-refresh]');
  if (refresh) {
    var seconds = Math.max(5, +refresh.getAttribute('data-auto-refresh') || 15);
    setInterval(function () {
      var active = doc.activeElement;
      if (!doc.hidden && navigator.onLine && !(active && /^(INPUT|SELECT|TEXTAREA)$/.test(active.tagName))) { location.reload(); }
    }, seconds * 1000);
  }

  // ================================================================================================ the open tab is kept

  // A tab group of the page: the address (?tab=) knows which tab is open, so a reload, the way back and a save keep it (the pages read ?tab=).
  function keepTab(group) {
    var tab = $('[role=tab][aria-selected="true"][data-tab]', group);
    if (!tab) { return; }
    var key = tab.getAttribute('data-tab');
    $$('input[name="Tab"]', group.closest('form') || doc).forEach(function (field) { field.value = key; });
    var url = new URL(window.location.href);
    if (url.searchParams.get('tab') !== key) { url.searchParams.set('tab', key); history.replaceState(history.state, '', url.toString()); }
  }
  $$('[data-tabs]').forEach(function (group) {
    if (!$('[data-tab]', group)) { return; }
    group.addEventListener('click', function () { setTimeout(function () { keepTab(group); }, 0); });
    group.addEventListener('keyup', function () { keepTab(group); });
  });

  // ================================================================================================ rows (the editor of function keys)

  // [data-rows="Prefix"]: rows of fields named Prefix[0].Field ... A new row is the <template data-rows-template> with __i__; after adding, moving and
  // removing, the rows are numbered again (names, ids, labels, the data-show-if of the row), so the form posts them in the order they stand.
  function renumber(box) {
    var id = box.getAttribute('data-rows-id');
    $$('[data-row]', $('[data-rows-list]', box)).forEach(function (row, i) {
      $$('[name]', row).forEach(function (field) { field.name = field.name.replace(/\[[^\]]*\]/, '[' + i + ']'); });
      $$('[id]', row).forEach(function (field) { field.id = field.id.replace(new RegExp('^' + id + '-[^-]+'), id + '-' + i); });
      $$('label[for]', row).forEach(function (label) { label.htmlFor = label.htmlFor.replace(new RegExp('^' + id + '-[^-]+'), id + '-' + i); });
      $$('[data-show-if]', row).forEach(function (field) { field.setAttribute('data-show-if', field.getAttribute('data-show-if').replace(new RegExp('#' + id + '-[^-]+'), '#' + id + '-' + i)); });
    });
    var count = $$('[data-row]', $('[data-rows-list]', box)).length;
    var empty = $('[data-rows-empty]', box);
    if (empty) { empty.hidden = count > 0; }
    var add = $('[data-rows-add]', box);
    if (add) { add.disabled = count >= (+box.getAttribute('data-rows-max') || 1000); }
    if (window.MatUi && window.MatUi.refresh) { window.MatUi.refresh(box); }
  }

  doc.addEventListener('click', function (e) {
    var target = e.target;
    var box = target.closest && target.closest('[data-rows]');
    if (!box) { return; }
    var list = $('[data-rows-list]', box);
    var row = target.closest('[data-row]');
    if (target.closest('[data-rows-add]')) {
      var template = $('[data-rows-template]', box);
      var fresh = template.content.firstElementChild.cloneNode(true);
      list.appendChild(fresh);
      renumber(box);
      var first = $('select, input', fresh);
      if (first) { first.focus(); }
    } else if (row && target.closest('[data-row-remove]')) {
      row.parentNode.removeChild(row);
      renumber(box);
    } else if (row && target.closest('[data-row-up]') && row.previousElementSibling) {
      list.insertBefore(row, row.previousElementSibling);
      renumber(box);
      $('[data-row-up]', row).focus();
    } else if (row && target.closest('[data-row-down]') && row.nextElementSibling) {
      list.insertBefore(row.nextElementSibling, row);
      renumber(box);
      $('[data-row-down]', row).focus();
    }
  });

  // ================================================================================================ a greeting recorded with the microphone

  // [data-greeting-recorder="mailbox id"]: records with the microphone (MediaRecorder), decodes the recording with WebAudio, mixes it to mono, brings it to
  // 8 kHz 16 bit (what the telephone plays) and posts it as a WAV to /greeting/{id}. The server checks it again.
  function wav8k(buffer) {
    var channels = buffer.numberOfChannels;
    var source = buffer.getChannelData(0);
    var mono = new Float32Array(source.length);
    for (var c = 0; c < channels; c++) {
      var data = buffer.getChannelData(c);
      for (var i = 0; i < data.length; i++) { mono[i] += data[i] / channels; }
    }

    var ratio = buffer.sampleRate / 8000;
    var length = Math.floor(mono.length / ratio);
    var samples = new Int16Array(length);
    for (var j = 0; j < length; j++) {
      // An average over the samples of each new one: a simple low pass before taking every n-th sample, so the voice does not alias.
      var start = Math.floor(j * ratio);
      var end = Math.min(mono.length, Math.floor((j + 1) * ratio));
      var sum = 0;
      for (var k = start; k < end; k++) { sum += mono[k]; }
      var v = Math.max(-1, Math.min(1, sum / Math.max(1, end - start)));
      samples[j] = v < 0 ? v * 0x8000 : v * 0x7fff;
    }

    var bytes = new ArrayBuffer(44 + samples.length * 2);
    var view = new DataView(bytes);
    var write = function (offset, s) { for (var n = 0; n < s.length; n++) { view.setUint8(offset + n, s.charCodeAt(n)); } };
    write(0, 'RIFF'); view.setUint32(4, 36 + samples.length * 2, true); write(8, 'WAVE'); write(12, 'fmt ');
    view.setUint32(16, 16, true); view.setUint16(20, 1, true); view.setUint16(22, 1, true); view.setUint32(24, 8000, true);
    view.setUint32(28, 16000, true); view.setUint16(32, 2, true); view.setUint16(34, 16, true); write(36, 'data'); view.setUint32(40, samples.length * 2, true);
    for (var s = 0; s < samples.length; s++) { view.setInt16(44 + s * 2, samples[s], true); }
    return new Blob([bytes], { type: 'audio/wav' });
  }

  $$('[data-greeting-recorder]').forEach(function (box) {
    var id = box.getAttribute('data-greeting-recorder');
    var max = (+box.getAttribute('data-max-seconds') || 60) * 1000;
    var start = $('[data-record-start]', box);
    var stop = $('[data-record-stop]', box);
    var status = $('[data-record-status]', box);
    var result = $('[data-record-result]', box);
    var preview = $('[data-record-preview]', box);
    var recorder = null;
    var chunks = [];
    var wav = null;
    var timer = null;
    if (!window.MediaRecorder || !navigator.mediaDevices) { start.disabled = true; status.textContent = t('Js.Greeting.NotSupported'); return; }

    start.addEventListener('click', function () {
      navigator.mediaDevices.getUserMedia({ audio: true }).then(function (stream) {
        chunks = [];
        recorder = new MediaRecorder(stream);
        recorder.ondataavailable = function (e) { if (e.data && e.data.size) { chunks.push(e.data); } };
        recorder.onstop = function () {
          stream.getTracks().forEach(function (tr) { tr.stop(); });
          clearTimeout(timer);
          new Blob(chunks).arrayBuffer().then(function (data) {
            var Ctx = window.AudioContext || window.webkitAudioContext;
            return new Ctx().decodeAudioData(data);
          }).then(function (buffer) {
            wav = wav8k(buffer);
            preview.src = URL.createObjectURL(wav);
            result.hidden = false;
            status.textContent = t('Js.Greeting.Ready', Math.round(buffer.duration));
          }).catch(function () { status.textContent = t('Js.Greeting.Failed'); });
        };
        recorder.start();
        start.hidden = true;
        stop.hidden = false;
        result.hidden = true;
        status.textContent = t('Js.Greeting.Recording');
        timer = setTimeout(function () { if (recorder && recorder.state === 'recording') { recorder.stop(); } }, max);
      }).catch(function () { status.textContent = t('Js.Greeting.NoMicrophone'); });
    });

    stop.addEventListener('click', function () {
      if (recorder && recorder.state === 'recording') { recorder.stop(); }
      stop.hidden = true;
      start.hidden = false;
    });

    $('[data-record-discard]', box).addEventListener('click', function () { wav = null; result.hidden = true; status.textContent = ''; });

    $('[data-record-save]', box).addEventListener('click', function (e) {
      if (!wav) { return; }
      var button = e.currentTarget;
      var form = new FormData();
      form.append('file', wav, 'greeting.wav');
      button.disabled = true;
      fetch('/greeting/' + encodeURIComponent(id), { method: 'POST', credentials: 'same-origin', headers: { 'RequestVerificationToken': csrf() }, body: form })
        .then(function (response) {
          if (response.ok) { toast(t('Js.Greeting.Saved'), 'success'); setTimeout(function () { location.reload(); }, 600); return; }
          return response.json().then(function (body) { throw new Error(body && body.error || t('Js.Failed')); });
        })
        .catch(function (err) { toast(err.message || t('Js.Failed')); })
        .then(function () { button.disabled = false; });
    });
  });
})();

// Scrollbars are only visible while something scrolls: the class is set on the scrolled element and removed a moment later.
(function () {
  'use strict';
  var timers = new WeakMap();
  document.addEventListener('scroll', function (event) {
    var el = event.target === document ? document.documentElement : event.target;
    if (!el || !el.classList) { return; }
    el.classList.add('is-scrolling');
    clearTimeout(timers.get(el));
    timers.set(el, setTimeout(function () { el.classList.remove('is-scrolling'); }, 900));
  }, true);
})();
