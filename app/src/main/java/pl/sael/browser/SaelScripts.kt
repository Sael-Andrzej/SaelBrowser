package pl.sael.browser

object SaelScripts {
    fun apply(): String = script("apply")

    fun restore(): String = script("restore")

    private fun javascriptArray(values: List<String>): String = values.joinToString(
        prefix = "[",
        postfix = "]"
    ) { value ->
        "'" + value.replace("\\", "\\\\").replace("'", "\\'") + "'"
    }

    private fun script(action: String): String {
        val adSelectors = javascriptArray(SaelCleaningPolicy.adSelectors)
        val nuisanceKeywords = javascriptArray(SaelCleaningPolicy.nuisanceKeywords)
        val protectedKeywords = javascriptArray(SaelCleaningPolicy.protectedKeywords)

        return """
            (() => {
              const action = '$action';
              const result = { success: false, hiddenCount: 0, titleCount: 0, observing: false };

              try {
                const existing = window.__saelState;

                if (action === 'restore') {
                  if (existing) {
                    if (existing.observer) existing.observer.disconnect();
                    if (existing.timer) clearTimeout(existing.timer);

                    existing.hidden.forEach(el => {
                      if (el && el.isConnected) {
                        el.classList.remove('sael-hidden');
                        el.removeAttribute('data-sael-hidden');
                      }
                    });

                    existing.titles.forEach((info, el) => {
                      if (el && el.isConnected) {
                        el.querySelector(':scope > .sael-unverified')?.remove();
                        if (info.originalText !== null) el.textContent = info.originalText;
                        el.classList.remove('sael-title');
                      }
                    });

                    result.hiddenCount = existing.hidden.size;
                    result.titleCount = existing.titles.size;
                    existing.hidden.clear();
                    existing.titles.clear();
                    delete window.__saelState;
                  }

                  document.getElementById('sael-page-status')?.remove();
                  document.getElementById('sael-style')?.remove();
                  result.success = true;
                  return JSON.stringify(result);
                }

                const state = existing || {
                  hidden: new Set(),
                  titles: new Map(),
                  observer: null,
                  timer: null
                };
                window.__saelState = state;
                if (state.observer) state.observer.disconnect();

                let style = document.getElementById('sael-style');
                if (!style) {
                  style = document.createElement('style');
                  style.id = 'sael-style';
                  style.textContent = `
                    .sael-hidden { display: none !important; }
                    .sael-title {
                      position: relative !important;
                      border-left: 4px solid #e7c65f !important;
                      padding-left: 12px !important;
                    }
                    .sael-unverified {
                      display: inline-block !important;
                      margin-left: 8px !important;
                      padding: 3px 7px !important;
                      border-radius: 999px !important;
                      background: #ffd54a !important;
                      color: #151515 !important;
                      font: 700 10px/1.2 sans-serif !important;
                      vertical-align: middle !important;
                      text-transform: uppercase !important;
                      letter-spacing: .04em !important;
                    }
                    img { max-width: 100% !important; }
                  `;
                  (document.head || document.documentElement).appendChild(style);
                }

                const adSelectors = $adSelectors;
                const nuisanceKeywords = $nuisanceKeywords;
                const protectedKeywords = $protectedKeywords;

                const hide = el => {
                  if (!el || el.hasAttribute('data-sael-hidden')) return;
                  el.setAttribute('data-sael-hidden', 'true');
                  el.classList.add('sael-hidden');
                  state.hidden.add(el);
                  result.hiddenCount++;
                };

                const looksLikeNuisanceOverlay = el => {
                  const computed = getComputedStyle(el);
                  const overlay = el.getAttribute('aria-modal') === 'true' ||
                    el.getAttribute('role') === 'dialog' ||
                    computed.position === 'fixed' || computed.position === 'sticky';
                  if (!overlay) return false;

                  const identity = (
                    (el.id || '') + ' ' +
                    (el.className || '') + ' ' +
                    (el.getAttribute('aria-label') || '')
                  ).toLowerCase();
                  const text = (el.innerText || '').toLowerCase().slice(0, 2000);
                  if (protectedKeywords.some(word => text.includes(word))) return false;
                  return nuisanceKeywords.some(word => text.includes(word) || identity.includes(word));
                };

                const clean = () => {
                  adSelectors.forEach(selector => {
                    try { document.querySelectorAll(selector).forEach(hide); } catch (_) {}
                  });

                  document.querySelectorAll(
                    '[role="dialog"], [aria-modal="true"], ' +
                    '[class*="newsletter" i], [class*="cookie-consent" i], ' +
                    '[class*="subscribe" i], [id*="newsletter" i]'
                  ).forEach(el => {
                    if (looksLikeNuisanceOverlay(el)) hide(el);
                  });

                  document.querySelectorAll('h1, h2, h3').forEach(el => {
                    if (!el || el.closest('[data-sael-hidden="true"]')) return;
                    const originalText = (el.innerText || '').trim();
                    if (originalText.length < 18 || originalText.length > 220) return;

                    if (!state.titles.has(el)) {
                      state.titles.set(el, {
                        originalText: el.childElementCount === 0 ? originalText : null
                      });
                    }

                    let normalized = originalText
                      .replace(/^\s*(PILNE|WIDEO|VIDEO|GALERIA|ZDJĘCIA|RELACJA|LIVE)\s*[:\-–—]?\s*/i, '')
                      .replace(/\s+/g, ' ')
                      .trim();
                    const letters = normalized.replace(/[^A-Za-zĄĆĘŁŃÓŚŹŻąćęłńóśźż]/g, '');
                    if (letters.length > 12 && letters === letters.toUpperCase()) {
                      normalized = normalized.charAt(0).toUpperCase() + normalized.slice(1).toLowerCase();
                    }

                    if (el.childElementCount === 0 && normalized !== originalText) {
                      el.textContent = normalized;
                    }
                    el.classList.add('sael-title');

                    if (!el.querySelector(':scope > .sael-unverified')) {
                      const badge = document.createElement('span');
                      badge.className = 'sael-unverified';
                      badge.textContent = 'NIEZWERYFIKOWANE';
                      el.appendChild(badge);
                    }
                    result.titleCount++;
                  });

                  let bar = document.getElementById('sael-page-status');
                  if (!bar && document.body) {
                    bar = document.createElement('div');
                    bar.id = 'sael-page-status';
                    bar.innerHTML = '👓 <b>SAEL</b> &nbsp; <span style="color:#ffd54a">● treść jeszcze niezweryfikowana</span>';
                    bar.style.cssText = 'position:sticky;top:0;z-index:2147483647;padding:9px 12px;' +
                      'background:#07130c;color:#f0e5bb;font:600 13px sans-serif;' +
                      'border-bottom:1px solid #8e7838;box-shadow:0 2px 10px rgba(0,0,0,.25);';
                    document.body.prepend(bar);
                  }
                };

                clean();
                state.observer = new MutationObserver(() => {
                  if (state.timer) clearTimeout(state.timer);
                  state.timer = setTimeout(clean, 180);
                });
                state.observer.observe(document.documentElement, { childList: true, subtree: true });

                result.success = true;
                result.observing = true;
                return JSON.stringify(result);
              } catch (error) {
                result.error = String(error && error.message ? error.message : error);
                return JSON.stringify(result);
              }
            })();
        """.trimIndent()
    }
}
