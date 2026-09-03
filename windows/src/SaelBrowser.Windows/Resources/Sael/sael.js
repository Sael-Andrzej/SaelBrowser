(() => {
  const action = '__ACTION__';
  const result = { success: false, hiddenCount: 0, observing: false, error: null };
  try {
    const existing = window.__saelState;
    if (action === 'restore') {
      if (existing) {
        existing.observer?.disconnect();
        if (existing.timer) clearTimeout(existing.timer);
        existing.hidden.forEach(el => {
          if (el?.isConnected) {
            el.classList.remove('sael-hidden');
            el.removeAttribute('data-sael-hidden');
          }
        });
        existing.titles.forEach((info, el) => {
          if (!el?.isConnected) return;
          if (info.originalHtml !== null) el.innerHTML = info.originalHtml;
          el.classList.remove('sael-title', 'sael-analyzing', 'sael-resolved');
          el.removeAttribute('data-sael-original-title');
          el.removeAttribute('data-sael-analyzing');
        });
        if (existing.bodyOverflow !== null && document.body) document.body.style.overflow = existing.bodyOverflow;
        if (existing.htmlOverflow !== null) document.documentElement.style.overflow = existing.htmlOverflow;
        result.hiddenCount = existing.hidden.size;
        delete window.__saelState;
      }
      document.getElementById('sael-page-status')?.remove();
      document.getElementById('sael-style')?.remove();
      result.success = true;
      return JSON.stringify(result);
    }

    const state = existing || { hidden: new Set(), titles: new Map(), observer: null, timer: null, bodyOverflow: document.body?.style.overflow ?? null, htmlOverflow: document.documentElement.style.overflow ?? null };
    window.__saelState = state;
    state.observer?.disconnect();
    let style = document.getElementById('sael-style');
    if (!style) {
      style = document.createElement('style');
      style.id = 'sael-style';
      style.textContent = `.sael-hidden{display:none!important}.sael-title{position:relative!important;border-left:4px solid #e7c65f!important;padding-left:12px!important}.sael-analysis-text{display:inline;filter:none;opacity:1;transition:filter .24s ease,opacity .24s ease}.sael-analyzing>.sael-analysis-text{filter:blur(2.2px);opacity:.72}.sael-progress{display:inline-block;margin-left:9px;padding:2px 7px;border-radius:999px;background:#12251a;color:#e7c65f;font:600 11px sans-serif;vertical-align:middle;transition:opacity .2s ease}.sael-resolved>.sael-analysis-text{filter:blur(0);opacity:1}img{max-width:100%!important}`;
      (document.head || document.documentElement).appendChild(style);
    }
    const adSelectors = ['.adsbygoogle','[data-ad-slot]','[data-ad-client]','[id^="google_ads_"]','[id*="div-gpt-ad"]','[class~="ad-container"]','[class~="advertisement"]','[aria-label*="advertisement" i]','[aria-label*="reklama" i]','iframe[src*="doubleclick.net"]','iframe[src*="googlesyndication.com"]'];
    const nuisance = ['newsletter','subscribe','subskryb','powiadomienia','notifications','cookie consent','zgoda na pliki cookie','marketing consent','premium','bez reklam','wyłącz reklamy','odblokuj dostęp','kup dostęp','oferta specjalna'];
    const nuisanceIdentity = ['advert','adform','billboard','popup','pop-up','overlay','modal','interstitial','paywall','premium','subscribe','newsletter','sticky-ad','floating-ad'];
    const protectedWords = ['log in','login','sign in','zaloguj','konto','account','payment','płatność','checkout','security','bezpieczeństwo'];
    const hide = el => {
      if (!el || el.hasAttribute('data-sael-hidden')) return;
      el.setAttribute('data-sael-hidden', 'true');
      el.classList.add('sael-hidden');
      state.hidden.add(el);
      result.hiddenCount++;
    };
    const nuisanceOverlay = el => {
      const computed = getComputedStyle(el);
      const rect = el.getBoundingClientRect();
      const overlay = el.getAttribute('aria-modal') === 'true' || el.getAttribute('role') === 'dialog' || computed.position === 'fixed' || computed.position === 'sticky' || (computed.position === 'absolute' && rect.width >= innerWidth * .55 && rect.height >= innerHeight * .25);
      if (!overlay) return false;
      const identity = `${el.id || ''} ${el.className || ''} ${el.getAttribute('aria-label') || ''}`.toLowerCase();
      const text = (el.innerText || '').toLowerCase().slice(0, 2000);
      if (protectedWords.some(word => text.includes(word))) return false;
      if (el.matches('article,main,[role="main"]') || el.querySelector('article,main,[role="main"]')) return false;
      return nuisance.some(word => text.includes(word) || identity.includes(word)) || nuisanceIdentity.some(word => identity.includes(word));
    };
    const factualSentence = text => (text || '').split(/(?<=[.!?])\s+/).map(value => value.replace(/\s+/g, ' ').trim()).find(value => value.length >= 25 && value.length <= 220 && /\b(jest|są|był|była|będzie|ma|miał|wynosi|ogłosił|opublikował|zmarł|wygrał|przegrał)\b|\d/i.test(value) && !/\b(kliknij|zobacz|sprawdź|dowiedz się|czytaj dalej)\b/i.test(value));
    const rewriteTitle = (el, original) => {
      const sensational = /\b(PILNE|SZOK|MUSISZ TO ZOBACZYĆ|NIE UWIERZYSZ|BREAKING)\b/i.test(original);
      const suggestive = /[?…]\s*$/.test(original) && /\b(czy|dlaczego|jak to możliwe|co ukrywają|co się stało)\b/i.test(original);
      const letters = original.replace(/[^A-Za-zĄĆĘŁŃÓŚŹŻąćęłńóśźż]/g, '');
      const caps = letters.length >= 12 && letters === letters.toUpperCase();
      if (!sensational && !suggestive && !caps) return original;
      let neutral = original.replace(/^\s*(PILNE|SZOK|WIDEO|VIDEO|GALERIA|ZDJĘCIA|RELACJA|LIVE|BREAKING)\s*[:!\-–—]*\s*/i, '').replace(/\s*(MUSISZ TO ZOBACZYĆ|NIE UWIERZYSZ|ZOBACZ(?:CIE)?(?:,?\s+CO\s+SIĘ\s+STAŁO)?|KLIKNIJ,?\s+ABY\s+SIĘ\s+DOWIEDZIEĆ)[.!?…]*\s*$/i, '').replace(/\s+/g, ' ').trim();
      const container = el.closest('article,[class*="article" i],[class*="card" i],[class*="tile" i],[class*="item" i],li') || el.parentElement;
      const context = [...(container?.querySelectorAll('p,[class*="lead" i],[class*="description" i],[class*="summary" i]') || [])].map(node => node.innerText || '').join(' ');
      const candidate = factualSentence(context);
      if ((neutral.length < 18 || /\b(to|tego|takiego|co się stało|nie uwierzysz|musisz)\b|[?…]$/i.test(neutral)) && candidate) neutral = candidate;
      if (caps && neutral === neutral.toUpperCase()) neutral = neutral.charAt(0).toUpperCase() + neutral.slice(1).toLowerCase();
      return neutral.length > 180 ? neutral.slice(0, 177).trimEnd() + '…' : neutral;
    };
    const clean = () => {
      adSelectors.forEach(selector => { try { document.querySelectorAll(selector).forEach(hide); } catch {} });
      document.querySelectorAll('[role="dialog"],[aria-modal="true"],[class*="newsletter" i],[class*="cookie-consent" i],[class*="subscribe" i],[id*="newsletter" i],[class*="popup" i],[class*="overlay" i],[class*="interstitial" i],[class*="premium" i],[id*="premium" i]').forEach(el => { if (nuisanceOverlay(el)) hide(el); });
      document.querySelectorAll('body > div,body > aside').forEach(el => { if (nuisanceOverlay(el)) hide(el); });
      document.querySelectorAll('h1,h2,h3').forEach(el => {
        if (el.closest('[data-sael-hidden="true"]')) return;
        const original = (el.innerText || '').trim();
        if (original.length < 18 || original.length > 220) return;
        if (el.matches('h1')) {
          if (!state.titles.has(el)) state.titles.set(el, { originalHtml: el.innerHTML });
          if (!el.querySelector(':scope > .sael-analysis-text')) {
            const text = document.createElement('span'); text.className = 'sael-analysis-text';
            while (el.firstChild) text.appendChild(el.firstChild);
            const badge = document.createElement('span'); badge.className = 'sael-progress'; badge.textContent = 'SAEL analizuje…';
            el.append(text, badge);
          }
          el.classList.add('sael-title', 'sael-analyzing');
          el.setAttribute('data-sael-analyzing', 'true');
          return;
        }
        const normalized = rewriteTitle(el, original);
        if (normalized === original) return;
        if (!state.titles.has(el)) state.titles.set(el, { originalHtml: el.innerHTML });
        el.setAttribute('data-sael-original-title', original);
        const linkedTitle = el.matches('a[href]') ? el : el.querySelector(':scope > a[href]');
        if (linkedTitle) linkedTitle.textContent = normalized;
        else el.textContent = normalized;
        el.classList.add('sael-title');
      });
      if ([...state.hidden].some(el => el?.isConnected) && document.body) { document.body.style.overflow = 'auto'; document.documentElement.style.overflow = 'auto'; }
      let bar = document.getElementById('sael-page-status');
      if (!bar && document.body) {
        bar = document.createElement('div'); bar.id = 'sael-page-status';
        bar.innerHTML = '👓 <b>SAEL</b> &nbsp; uproszczony widok strony';
        bar.style.cssText = 'position:sticky;top:0;z-index:2147483647;padding:9px 12px;background:#07130c;color:#f0e5bb;font:600 13px sans-serif;border-bottom:1px solid #8e7838;box-shadow:0 2px 10px rgba(0,0,0,.25)';
        document.body.prepend(bar);
      }
    };
    clean();
    state.observer = new MutationObserver(() => { if (state.timer) clearTimeout(state.timer); state.timer = setTimeout(clean, 180); });
    state.observer.observe(document.documentElement, { childList: true, subtree: true });
    result.success = true; result.observing = true;
    return JSON.stringify(result);
  } catch (error) {
    result.error = String(error?.message || error);
    return JSON.stringify(result);
  }
})();
