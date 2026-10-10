// Chat page behaviour. The server draws every message and card (the same partials as the page load);
// this script only sends requests, shows "thinking", and puts the returned HTML on the page.
(function () {
  'use strict';

  var form = document.getElementById('chat-form');
  var box = document.getElementById('messages');
  if (!form || !box) return;

  var text = document.getElementById('chat-text');
  var send = document.getElementById('chat-send');
  var errorEl = document.getElementById('chat-error');
  var token = form.querySelector('input[name="__RequestVerificationToken"]').value;
  var sendUrl = form.dataset.sendUrl;
  var conversationId = form.dataset.conversationId || '';

  function showError(message) {
    errorEl.textContent = message || '';
    errorEl.classList.toggle('hidden', !message);
  }

  function scrollToEnd() { box.scrollTop = box.scrollHeight; }

  // The danger button on a final card stays disabled for a moment after it appears.
  function armDelayedButtons(root) {
    root.querySelectorAll('button[data-delay][disabled]').forEach(function (b) {
      setTimeout(function () { b.disabled = false; }, parseInt(b.dataset.delay, 10) || 1000);
    });
  }

  function post(url, body) {
    return fetch(url, {
      method: 'POST',
      headers: { 'RequestVerificationToken': token },
      body: body || new FormData(),
      credentials: 'same-origin'
    });
  }

  function thinking() {
    var el = document.createElement('div');
    el.dataset.temp = 'true';
    el.className = 'chat-bubble-assistant';
    el.setAttribute('role', 'status');
    el.innerHTML = '<span class="sr-only">Thinking…</span><span class="thinking-dot"></span> <span class="thinking-dot"></span> <span class="thinking-dot"></span>';
    return el;
  }

  function userBubble(value) {
    var wrap = document.createElement('div');
    wrap.dataset.temp = 'true';
    wrap.className = 'flex justify-end';
    var b = document.createElement('div');
    b.className = 'chat-bubble-user whitespace-pre-wrap break-words';
    b.textContent = value;
    wrap.appendChild(b);
    return wrap;
  }

  function clearTemp() {
    box.querySelectorAll('[data-temp]').forEach(function (n) { n.remove(); });
  }

  function append(html) {
    var t = document.createElement('template');
    t.innerHTML = html;
    var added = [];
    // Cards that changed state are swapped in place; everything else is a new message.
    t.content.querySelectorAll('[data-card-replace]').forEach(function (r) {
      var target = document.getElementById(r.dataset.cardReplace);
      var fresh = r.firstElementChild;
      if (target && fresh) { target.replaceWith(fresh); added.push(fresh); }
      r.remove();
    });
    var notice = t.content.querySelector('[data-notice]');
    if (notice) { showError(notice.textContent); notice.remove(); }
    Array.from(t.content.children).forEach(function (n) { box.appendChild(n); added.push(n); });
    added.forEach(armDelayedButtons);
  }

  form.addEventListener('submit', function (e) {
    e.preventDefault();
    var value = text.value.trim();
    if (!value) { showError('Type a message first.'); return; }
    showError('');

    var hint = document.getElementById('empty-hint');
    if (hint) hint.remove();
    send.disabled = true;
    text.disabled = true;
    box.appendChild(userBubble(value));
    box.appendChild(thinking());
    scrollToEnd();

    var data = new FormData();
    data.append('text', value);
    if (conversationId) data.append('conversationId', conversationId);

    post(sendUrl, data).then(function (res) {
      return res.text().then(function (body) { return { res: res, body: body }; });
    }).then(function (r) {
      clearTemp();
      if (!r.res.ok) {
        showError(r.res.status === 404 ? 'That conversation was not found.' : (r.body || 'Something went wrong.'));
        return;
      }
      var id = r.res.headers.get('X-Conversation-Id');
      if (id && id !== conversationId) {
        conversationId = id;
        history.replaceState(null, '', '?c=' + id);
      }
      text.value = '';
      append(r.body);
    }).catch(function () {
      clearTemp();
      showError('The server could not be reached. Try again.');
    }).finally(function () {
      send.disabled = false;
      text.disabled = false;
      text.focus();
      scrollToEnd();
    });
  });

  // Enter sends, Shift+Enter adds a new line.
  text.addEventListener('keydown', function (e) {
    if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); form.requestSubmit(); }
  });

  // Card buttons: Confirm, Cancel, and the final confirmation.
  box.addEventListener('click', function (e) {
    var btn = e.target.closest('button[data-card-action]');
    if (!btn || btn.disabled) return;
    var url = sendUrl.replace('handler=Send', 'handler=' + btn.dataset.cardAction) + '&id=' + encodeURIComponent(btn.dataset.id);
    box.querySelectorAll('button[data-id="' + btn.dataset.id + '"]').forEach(function (b) { b.disabled = true; });
    showError('');
    post(url).then(function (res) {
      return res.text().then(function (body) { return { res: res, body: body }; });
    }).then(function (r) {
      if (!r.res.ok) { showError('That card could not be found.'); return; }
      append(r.body);
    }).catch(function () {
      showError('The server could not be reached. Try again.');
    }).finally(scrollToEnd);
  });

  armDelayedButtons(box);

  // "View in chat" links from the audit page land on #m{id}.
  if (location.hash) {
    var target = document.getElementById(location.hash.slice(1));
    if (target) { target.scrollIntoView({ block: 'center' }); target.classList.add('ring-2', 'ring-amber-500'); }
  } else {
    scrollToEnd();
  }
})();
