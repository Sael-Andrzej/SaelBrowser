package pl.sael.browser

import android.graphics.Color
import android.os.Bundle
import android.view.KeyEvent
import android.webkit.WebResourceRequest
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.Button
import android.widget.EditText
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import java.net.URLEncoder

class MainActivity : AppCompatActivity() {

    private lateinit var webView: WebView
    private lateinit var addressBar: EditText
    private lateinit var originalButton: Button
    private lateinit var saelButton: Button
    private lateinit var statusBar: TextView

    private var saelMode = true

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        webView = findViewById(R.id.webView)
        addressBar = findViewById(R.id.addressBar)
        originalButton = findViewById(R.id.originalButton)
        saelButton = findViewById(R.id.saelButton)
        statusBar = findViewById(R.id.statusBar)

        configureWebView()
        configureControls()
        updateModeUi()

        loadAddress("https://www.google.com")
    }

    private fun configureWebView() {
        webView.settings.apply {
            javaScriptEnabled = true
            domStorageEnabled = true
            loadsImagesAutomatically = true
            builtInZoomControls = true
            displayZoomControls = false
            useWideViewPort = true
            loadWithOverviewMode = true
        }

        webView.setBackgroundColor(Color.BLACK)

        webView.webViewClient = object : WebViewClient() {

            override fun shouldOverrideUrlLoading(
                view: WebView?,
                request: WebResourceRequest?
            ): Boolean {
                return false
            }

            override fun onPageFinished(view: WebView?, url: String?) {
                super.onPageFinished(view, url)

                url?.let {
                    addressBar.setText(it)
                }

                if (saelMode) {
                    injectSaelMode()
                } else {
                    statusBar.text = "ORYGINAŁ • strona bez zmian SAEL"
                }
            }
        }
    }

    private fun configureControls() {

        findViewById<Button>(R.id.goButton).setOnClickListener {
            loadAddress(addressBar.text.toString())
        }

        addressBar.setOnEditorActionListener { _, _, _ ->
            loadAddress(addressBar.text.toString())
            true
        }

        findViewById<Button>(R.id.backButton).setOnClickListener {
            if (webView.canGoBack()) webView.goBack()
        }

        originalButton.setOnClickListener {
            if (!saelMode) return@setOnClickListener

            saelMode = false
            updateModeUi()

            // Najpewniejsze odtworzenie oryginału:
            webView.reload()
        }

        saelButton.setOnClickListener {
            saelMode = true
            updateModeUi()

            // Nie przeładowujemy strony.
            // Nakładamy SAEL od razu na aktualny dokument.
            injectSaelMode()
        }
    }

    private fun loadAddress(input: String) {
        val s = input.trim()
        if (s.isEmpty()) return

        val url = when {
            s.startsWith("http://", true) ||
            s.startsWith("https://", true) -> s

            s.contains(".") && !s.contains(" ") ->
                "https://$s"

            else ->
                "https://www.google.com/search?q=" +
                    URLEncoder.encode(s, "UTF-8")
        }

        webView.loadUrl(url)
    }

    private fun updateModeUi() {
        if (saelMode) {
            saelButton.text = "👓  SAEL ✓"
            originalButton.text = "ORYGINAŁ"

            statusBar.text =
                "👓 SAEL • porządkuję stronę • treść jeszcze niezweryfikowana"

            statusBar.setTextColor(Color.rgb(230, 200, 107))
        } else {
            originalButton.text = "ORYGINAŁ ✓"
            saelButton.text = "👓  SAEL"

            statusBar.text = "ORYGINAŁ • bez filtra SAEL"
            statusBar.setTextColor(Color.LTGRAY)
        }
    }

    private fun injectSaelMode() {

        val js = """
        (() => {

          try {

            /* ===============================================
               1. USUWANIE TYPOWYCH ŚMIECI
               =============================================== */

            const selectors = [
              'iframe',
              'ins',

              '[class*="advert"]',
              '[id*="advert"]',

              '[class*="advertisement"]',
              '[id*="advertisement"]',

              '[class*="banner"]',
              '[id*="banner"]',

              '[class*="popup"]',
              '[id*="popup"]',

              '[class*="modal"]',
              '[id*="modal"]',

              '[class*="newsletter"]',
              '[id*="newsletter"]',

              '[class*="sponsor"]',
              '[id*="sponsor"]',

              '[class*="promo"]',
              '[id*="promo"]',

              '[class*="cookie"]',
              '[id*="cookie"]',

              '[aria-label*="reklam" i]',
              '[aria-label*="advert" i]'
            ];

            selectors.forEach(selector => {
              try {
                document.querySelectorAll(selector).forEach(el => {
                  el.remove();
                });
              } catch(e) {}
            });


            /* ===============================================
               2. STYL SAEL
               =============================================== */

            let style = document.getElementById('sael-style');

            if (!style) {
              style = document.createElement('style');
              style.id = 'sael-style';

              style.textContent = `

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

                img {
                  max-width: 100% !important;
                }
              `;

              document.head.appendChild(style);
            }


            /* ===============================================
               3. PORZĄDKOWANIE TYTUŁÓW
               =============================================== */

            const candidates =
              document.querySelectorAll(
                'h1, h2, h3, article a, main a'
              );

            const seen = new Set();

            candidates.forEach(el => {

              if (!el) return;

              let txt = (el.innerText || '').trim();

              if (txt.length < 18 || txt.length > 220)
                return;

              if (seen.has(txt))
                return;

              seen.add(txt);


              /* usunięcie typowych krzykliwych prefiksów */

              txt = txt
                .replace(/^\s*(PILNE|WIDEO|VIDEO|GALERIA|ZDJĘCIA|RELACJA|LIVE)\s*[:\-–—]?\s*/i, '')
                .replace(/\s+/g, ' ')
                .trim();


              /*
               * Jeżeli tekst jest niemal w całości CAPS LOCKIEM,
               * ograniczamy krzykliwość.
               */

              const letters =
                txt.replace(/[^A-Za-zĄĆĘŁŃÓŚŹŻąćęłńóśźż]/g,'');

              if (
                letters.length > 12 &&
                letters === letters.toUpperCase()
              ) {
                txt =
                  txt.charAt(0).toUpperCase() +
                  txt.slice(1).toLowerCase();
              }


              /*
               * Nie nadpisujemy całych złożonych kart.
               * Tekst zmieniamy głównie na nagłówkach.
               */

              if (/^H[123]$/.test(el.tagName)) {
                el.childNodes.forEach(node => {
                  if (node.nodeType === Node.TEXT_NODE) {
                    node.textContent = txt;
                  }
                });
              }


              el.classList.add('sael-title');


              /*
               * WAŻNE:
               * bez FactEngine NIE oznaczamy nic jako
               * prawda/fałsz.
               *
               * Wszystko jest żółte = jeszcze niezweryfikowane.
               */

              if (!el.querySelector('.sael-unverified')) {

                const badge =
                  document.createElement('span');

                badge.className = 'sael-unverified';

                badge.textContent =
                  'NIEZWERYFIKOWANE';

                el.appendChild(badge);
              }

            });


            /* ===============================================
               4. PASEK SAEL W SAMEJ STRONIE
               =============================================== */

            let bar =
              document.getElementById('sael-page-status');

            if (!bar) {

              bar = document.createElement('div');

              bar.id = 'sael-page-status';

              bar.innerHTML =
                '👓 <b>SAEL</b> &nbsp; ' +
                '<span style="color:#ffd54a">' +
                '● treść jeszcze niezweryfikowana' +
                '</span>';

              bar.style.cssText =
                'position:sticky;' +
                'top:0;' +
                'z-index:2147483647;' +
                'padding:9px 12px;' +
                'background:#07130c;' +
                'color:#f0e5bb;' +
                'font:600 13px sans-serif;' +
                'border-bottom:1px solid #8e7838;' +
                'box-shadow:0 2px 10px rgba(0,0,0,.25);';

              document.body.prepend(bar);
            }

          } catch(err) {
            console.log('SAEL:', err);
          }

        })();
        """.trimIndent()

        webView.evaluateJavascript(js, null)

        statusBar.text =
            "👓 SAEL • uporządkowano • 🟡 treść niezweryfikowana"
    }

    @Deprecated("Deprecated in Java")
    override fun onBackPressed() {
        if (webView.canGoBack()) {
            webView.goBack()
        } else {
            super.onBackPressed()
        }
    }
}
