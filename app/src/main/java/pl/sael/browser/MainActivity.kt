package pl.sael.browser

import android.annotation.SuppressLint
import android.graphics.Bitmap
import android.os.Bundle
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.Button
import android.widget.EditText
import androidx.appcompat.app.AppCompatActivity
import java.io.ByteArrayInputStream

class MainActivity : AppCompatActivity() {

    private lateinit var webView: WebView
    private lateinit var addressBar: EditText
    private var cleanMode = true

    private val blockedFragments = listOf(
        "doubleclick.net", "googlesyndication.com", "googleadservices.com",
        "adservice.google.com", "facebook.net/tr", "analytics", "/ads/", "adserver"
    )

    @SuppressLint("SetJavaScriptEnabled")
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        webView = findViewById(R.id.webView)
        addressBar = findViewById(R.id.addressBar)

        webView.settings.javaScriptEnabled = true
        webView.settings.domStorageEnabled = true
        webView.settings.loadsImagesAutomatically = true
        webView.settings.userAgentString = webView.settings.userAgentString + " SaelBrowser/0.1"

        webView.webViewClient = object : WebViewClient() {
            override fun shouldInterceptRequest(view: WebView?, request: WebResourceRequest?): WebResourceResponse? {
                val url = request?.url?.toString().orEmpty().lowercase()
                if (blockedFragments.any { url.contains(it) }) {
                    return WebResourceResponse("text/plain", "utf-8", ByteArrayInputStream(ByteArray(0)))
                }
                return super.shouldInterceptRequest(view, request)
            }

            override fun onPageStarted(view: WebView?, url: String?, favicon: Bitmap?) {
                super.onPageStarted(view, url, favicon)
                addressBar.setText(url ?: "")
            }

            override fun onPageFinished(view: WebView?, url: String?) {
                super.onPageFinished(view, url)
                if (cleanMode) injectCleanMode()
            }
        }

        findViewById<Button>(R.id.goButton).setOnClickListener { navigate(addressBar.text.toString()) }
        addressBar.setOnEditorActionListener { _, _, _ -> navigate(addressBar.text.toString()); true }
        findViewById<Button>(R.id.backButton).setOnClickListener { if (webView.canGoBack()) webView.goBack() }
        findViewById<Button>(R.id.forwardButton).setOnClickListener { if (webView.canGoForward()) webView.goForward() }
        findViewById<Button>(R.id.cleanButton).setOnClickListener { cleanMode = true; injectCleanMode() }
        findViewById<Button>(R.id.originalButton).setOnClickListener { cleanMode = false; webView.reload() }

        navigate("https://www.google.com")
    }

    private fun navigate(input: String) {
        val s = input.trim()
        val url = when {
            s.startsWith("https://") || s.startsWith("http://") -> s
            s.contains(".") && !s.contains(" ") -> "https://$s"
            else -> "https://www.google.com/search?q=" + java.net.URLEncoder.encode(s, "UTF-8")
        }
        webView.loadUrl(url)
    }

    private fun injectCleanMode() {
        val js = """
            (() => {
              const selectors = [
                'iframe','ins','[class*="advert"]','[id*="advert"]','[class*="cookie"]',
                '[id*="cookie"]','[class*="banner"]','[class*="popup"]','[class*="modal"]',
                '[class*="newsletter"]','[class*="sponsor"]','[class*="promo"]'
              ];
              selectors.forEach(s => document.querySelectorAll(s).forEach(e => e.remove()));

              const title = document.querySelector('h1') || document.querySelector('title');
              if (title && !document.getElementById('sael-status')) {
                const bar = document.createElement('div');
                bar.id = 'sael-status';
                bar.innerHTML = '🟡 Treść jeszcze nieweryfikowana przez FactEngine &nbsp;•&nbsp; reklamy i śmieci ukryte';
                bar.style.cssText = 'position:relative;z-index:2147483647;padding:10px 14px;background:#fff3b0;color:#111;font:600 14px sans-serif;border-bottom:1px solid #d5c35c';
                document.body.prepend(bar);
              }
            })();
        """.trimIndent()
        webView.evaluateJavascript(js, null)
    }

    override fun onBackPressed() {
        if (webView.canGoBack()) webView.goBack() else super.onBackPressed()
    }
}
