// Bootstrap inyectado por la app en el WebView2 (se ejecuta en cada carga de la página de Teams).
// Define el lector del DOM, instala un observador que publica los cambios a la app en tiempo real
// (window.chrome.webview.postMessage) y expone window.__tjRead(force, rounds) para "Analizar ahora".
(() => {
  "use strict";
  if (window.__tjInstalled) return;
  window.__tjInstalled = true;
  window.__tjAuto = true;

  const SELECTORS = {
    message: ['.fui-ChatMessage, .fui-ChatMyMessage', '[data-tid="chat-pane-message"]'],
    author: ['[data-tid="message-author-name"]', '.fui-ChatMessage__author'],
    time: ['time[datetime]', 'time'],
    text: ['[id^="content-"]', '[data-tid="messageBodyContent"]', '[data-tid="chat-pane-message"]'],
    chatTitle: ['h2[role="heading"]', 'h2']
  };
  const ATTACH_RE = /\.(xlsx|xls|pdf|docx?|pptx?|png|jpe?g|gif|csv|txt|zip)$/i;

  const sleep = (ms) => new Promise(r => setTimeout(r, ms));
  const clean = (s) => (s || "").replace(/\s+/g, " ").trim();
  const firstMatch = (root, list) => { for (const s of list) { const e = root.querySelector(s); if (e) return e; } return null; };
  const allMatches = (list) => { for (const s of list) { const e = document.querySelectorAll(s); if (e.length) return Array.from(e); } return []; };

  function currentChatName() {
    const parts = (document.title || "").split("|").map(s => s.trim()).filter(Boolean);
    const cleaned = parts.filter(p => p !== "Chat" && !/Microsoft Teams/i.test(p));
    if (cleaned.length) return cleaned.join(" ");
    const el = firstMatch(document, SELECTORS.chatTitle);
    return clean(el && el.textContent) || "Teams";
  }
  function currentChatId() {
    const m = location.href.match(/conversations\/([^?/]+)/) || location.href.match(/\/(19:[^/?]+)/);
    return (m && m[1]) || ("name:" + currentChatName());
  }

  function getScrollContainer() {
    const nodes = allMatches(SELECTORS.message);
    let el = nodes[0];
    while (el && el !== document.body) {
      const s = getComputedStyle(el);
      if ((s.overflowY === "auto" || s.overflowY === "scroll") && el.scrollHeight > el.clientHeight + 40) return el;
      el = el.parentElement;
    }
    return null;
  }

  async function loadMore(maxRounds) {
    const c = getScrollContainer();
    if (!c) return;
    let prev = allMatches(SELECTORS.message).length;
    for (let i = 0; i < maxRounds; i++) {
      c.scrollTop = 0;
      await sleep(900);
      const now = allMatches(SELECTORS.message).length;
      if (now <= prev) break;
      prev = now;
    }
    c.scrollTop = c.scrollHeight;
    await sleep(250);
  }

  function readMessages() {
    const nodes = allMatches(SELECTORS.message);
    const messages = [];
    let lastAuthor = "";
    for (const node of nodes.slice(-200)) {
      const textEl = firstMatch(node, SELECTORS.text);
      let text = clean(textEl && textEl.textContent);

      const attach = new Set();
      node.querySelectorAll('a[href], [data-tid*="file"], [class*="ttachment"], [download]').forEach(a => {
        const t = clean(a.textContent) || clean(a.getAttribute("download"));
        if (t && ATTACH_RE.test(t)) attach.add(t);
      });
      // Imágenes: sus píxeles no se pueden extraer (CORS de los CDN de Teams), pero sí su
      // texto alternativo, que da contexto al agente. Se ignoran avatares/emojis diminutos.
      node.querySelectorAll('img[src]').forEach(img => {
        const w = img.naturalWidth || img.width || 0;
        if (w && w < 40) return; // avatar/emoji
        const alt = clean(img.getAttribute("alt"));
        attach.add(alt ? "imagen: " + alt : "(imagen)");
      });
      const attachNote = attach.size ? " [adjuntos: " + [...attach].join(", ") + "]" : "";

      if (!text && !attachNote) continue;
      text = (text + attachNote).trim();

      let author = clean((firstMatch(node, SELECTORS.author) || {}).textContent);
      if (!author) author = lastAuthor; else lastAuthor = author;
      const timeEl = firstMatch(node, SELECTORS.time);
      const time = (timeEl && (timeEl.getAttribute("datetime") || timeEl.getAttribute("title") || timeEl.textContent)) || "";
      const isMe = /ChatMyMessage/i.test(node.className || "") || !!(node.closest && node.closest(".fui-ChatMyMessage"));
      if (isMe) author = author || "Yo";

      // ¿Me mencionan? Por marcado de mención de Teams o por mi nombre.
      let mention = false;
      if (!isMe) {
        if (node.querySelector('[itemtype*="mention"], [data-tid*="mention"], .fui-Mention, at')) mention = true;
        const me = (window.__tjMe || "").trim().toLowerCase();
        if (!mention && me) {
          const lc = text.toLowerCase();
          const first = me.split(" ")[0];
          if (lc.includes(me) || (first.length > 2 && new RegExp("\\b" + first + "\\b").test(lc))) mention = true;
        }
      }

      const midEl = node.querySelector("[data-mid]") || node;
      const mid = midEl.getAttribute && midEl.getAttribute("data-mid");
      messages.push({ id: mid || (author + "|" + time + "|" + text.slice(0, 40)), author, isMe, time: clean(time), text, mention });
    }
    return messages;
  }

  function post(p) { try { window.chrome.webview.postMessage(JSON.stringify(p)); } catch (e) {} }

  async function build(force, rounds) {
    if (rounds > 0) await loadMore(rounds);
    return { chatId: currentChatId(), chatName: currentChatName(), messages: readMessages(), force: !!force, v: "wv1" };
  }

  // "Analizar ahora" desde la app.
  window.__tjRead = async (force, rounds) => {
    const p = await build(force, rounds || 0);
    post(p);
    return p.messages.length;
  };

  // Chat actual (síncrono) para que la app coja al instante dónde estás (Memoria/Histórico).
  window.__tjCurrentChat = () => JSON.stringify({ chatId: currentChatId(), chatName: currentChatName() });

  // Seguimiento de navegación: al cambiar de chat, avisa enseguida (sin mensajes = solo tracking).
  let lastChatId = "";
  setInterval(() => {
    const id = currentChatId();
    if (id && id !== lastChatId) {
      lastChatId = id;
      post({ chatId: id, chatName: currentChatName(), messages: [], force: false, v: "nav" });
    }
  }, 700);

  // Observador: publica cambios en tiempo real (auto), con debounce y deduplicado.
  let lastPayload = "", deb;
  function schedule() {
    if (!window.__tjAuto) return;
    clearTimeout(deb);
    deb = setTimeout(async () => {
      const p = await build(false, 0);
      const s = JSON.stringify(p.messages);
      if (!p.messages.length || s === lastPayload) return;
      lastPayload = s;
      post(p);
    }, 2500);
  }
  const start = () => {
    if (!document.body) { setTimeout(start, 500); return; }
    new MutationObserver(schedule).observe(document.body, { childList: true, subtree: true, characterData: true });
    schedule();
  };
  start();
})();
