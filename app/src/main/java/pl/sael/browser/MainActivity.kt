package pl.sael.browser

import android.graphics.Color
import android.content.res.ColorStateList
import android.net.http.SslError
import android.os.Bundle
import android.webkit.SslErrorHandler
import android.webkit.WebResourceError
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.Button
import android.widget.EditText
import android.widget.TextView
import androidx.activity.OnBackPressedCallback
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import pl.sael.browser.fact.ArticleExtractor
import pl.sael.browser.fact.FactResult
import pl.sael.browser.fact.FactVerdict
import pl.sael.browser.fact.JsoupArticleExtractor
import pl.sael.browser.fact.ResultOrigin
import pl.sael.browser.fact.ThresholdFactEngine
import org.json.JSONObject
import org.json.JSONTokener
import java.util.concurrent.Executors
import kotlin.math.roundToInt

class MainActivity : AppCompatActivity() {
    private lateinit var webView: WebView
    private lateinit var addressBar: EditText
    private lateinit var backButton: Button
    private lateinit var forwardButton: Button
    private lateinit var originalButton: Button
    private lateinit var saelButton: Button
    private lateinit var statusBar: TextView
    private lateinit var factResultButton: Button

    private val modeState = BrowserModeState()
    private val articleExtractor: ArticleExtractor = JsoupArticleExtractor()
    private val factEngine = ThresholdFactEngine()
    private val analysisExecutor = Executors.newSingleThreadExecutor()
    private val analysisGate = AnalysisRequestGate()
    private var mainFrameLoadFailed = false
    private var latestFactResult: FactResult? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        webView = findViewById(R.id.webView)
        addressBar = findViewById(R.id.addressBar)
        backButton = findViewById(R.id.backButton)
        forwardButton = findViewById(R.id.forwardButton)
        originalButton = findViewById(R.id.originalButton)
        saelButton = findViewById(R.id.saelButton)
        statusBar = findViewById(R.id.statusBar)
        factResultButton = findViewById(R.id.factResultButton)

        savedInstanceState
            ?.getString(STATE_MODE)
            ?.let { savedMode -> runCatching { BrowserMode.valueOf(savedMode) }.getOrNull() }
            ?.let(modeState::select)

        configureWebView()
        configureControls()
        configureSystemBack()
        updateModeUi()
        updateNavigationControls()

        if (savedInstanceState == null) loadAddress("https://www.google.com")
        else webView.restoreState(savedInstanceState)
    }

    override fun onSaveInstanceState(outState: Bundle) {
        webView.saveState(outState)
        outState.putString(STATE_MODE, modeState.mode.name)
        super.onSaveInstanceState(outState)
    }

    override fun onDestroy() {
        analysisGate.beginNavigation()
        analysisExecutor.shutdownNow()
        super.onDestroy()
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
            ): Boolean = false

            override fun onPageStarted(view: WebView?, url: String?, favicon: android.graphics.Bitmap?) {
                super.onPageStarted(view, url, favicon)
                mainFrameLoadFailed = false
                analysisGate.beginNavigation()
                latestFactResult = null
                showFactPending("NIE WIEM • analizuję stronę…")
                url?.let(addressBar::setText)
                setStatus("Ładowanie strony…", STATUS_NEUTRAL)
                updateNavigationControls()
            }

            override fun doUpdateVisitedHistory(view: WebView?, url: String?, isReload: Boolean) {
                super.doUpdateVisitedHistory(view, url, isReload)
                url?.let(addressBar::setText)
                updateNavigationControls()
            }

            override fun onPageFinished(view: WebView?, url: String?) {
                super.onPageFinished(view, url)
                url?.let(addressBar::setText)
                updateNavigationControls()
                if (mainFrameLoadFailed) return

                analyzeCurrentPage(url ?: webView.url.orEmpty())

                if (modeState.mode == BrowserMode.SAEL) applySaelMode()
                else setStatus("ORYGINAŁ • strona bez zmian SAEL", STATUS_NEUTRAL)
            }

            override fun onReceivedError(
                view: WebView?,
                request: WebResourceRequest?,
                error: WebResourceError?
            ) {
                super.onReceivedError(view, request, error)
                if (request?.isForMainFrame == true) {
                    mainFrameLoadFailed = true
                    val description = error?.description?.toString().orEmpty()
                    showLoadError(if (description.isBlank()) "Nie udało się załadować strony" else description)
                }
            }

            override fun onReceivedHttpError(
                view: WebView?,
                request: WebResourceRequest?,
                errorResponse: WebResourceResponse?
            ) {
                super.onReceivedHttpError(view, request, errorResponse)
                if (request?.isForMainFrame == true && (errorResponse?.statusCode ?: 0) >= 400) {
                    mainFrameLoadFailed = true
                    showLoadError("Błąd HTTP ${errorResponse?.statusCode}")
                }
            }

            override fun onReceivedSslError(
                view: WebView?,
                handler: SslErrorHandler?,
                error: SslError?
            ) {
                handler?.cancel()
                mainFrameLoadFailed = true
                showLoadError("Błąd bezpiecznego połączenia SSL")
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

        backButton.setOnClickListener { navigate(NavigationDirection.BACK) }
        forwardButton.setOnClickListener { navigate(NavigationDirection.FORWARD) }
        factResultButton.setOnClickListener { latestFactResult?.let(::showFactDetails) }

        originalButton.setOnClickListener {
            if (!modeState.select(BrowserMode.ORIGINAL)) return@setOnClickListener
            updateModeUi()
            setStatus("ORYGINAŁ • przywracam stronę…", STATUS_NEUTRAL)
            runSaelScript(SaelScripts.restore()) { result ->
                if (modeState.mode != BrowserMode.ORIGINAL) return@runSaelScript
                if (result.success) {
                    setStatus("ORYGINAŁ • przywrócono stronę bez przeładowania", STATUS_NEUTRAL)
                } else showScriptError(result.error)
            }
        }

        saelButton.setOnClickListener {
            if (!modeState.select(BrowserMode.SAEL)) return@setOnClickListener
            updateModeUi()
            applySaelMode()
        }
    }

    private fun configureSystemBack() {
        onBackPressedDispatcher.addCallback(this, object : OnBackPressedCallback(true) {
            override fun handleOnBackPressed() {
                val command = BrowserNavigation.command(
                    NavigationDirection.BACK,
                    webView.canGoBack(),
                    webView.canGoForward()
                )
                if (command == NavigationCommand.GO_BACK) webView.goBack()
                else {
                    isEnabled = false
                    onBackPressedDispatcher.onBackPressed()
                }
            }
        })
    }

    private fun navigate(direction: NavigationDirection) {
        when (BrowserNavigation.command(direction, webView.canGoBack(), webView.canGoForward())) {
            NavigationCommand.GO_BACK -> {
                beginNavigationAnalysis()
                webView.goBack()
            }
            NavigationCommand.GO_FORWARD -> {
                beginNavigationAnalysis()
                webView.goForward()
            }
            NavigationCommand.NONE -> Unit
        }
        updateNavigationControls()
    }

    private fun updateNavigationControls() {
        backButton.isEnabled = webView.canGoBack()
        forwardButton.isEnabled = webView.canGoForward()
        backButton.alpha = if (backButton.isEnabled) 1f else 0.45f
        forwardButton.alpha = if (forwardButton.isEnabled) 1f else 0.45f
    }

    private fun loadAddress(input: String) {
        BrowserAddressNormalizer.normalize(input)?.let { url ->
            beginNavigationAnalysis()
            webView.loadUrl(url)
        }
    }

    private fun beginNavigationAnalysis() {
        analysisGate.beginNavigation()
        latestFactResult = null
        showFactPending("NIE WIEM • analizuję stronę…")
    }

    private fun updateModeUi() {
        if (modeState.mode == BrowserMode.SAEL) {
            saelButton.text = "👓  SAEL ✓"
            originalButton.text = "ORYGINAŁ"
            setStatus("👓 SAEL • porządkuję stronę • treść jeszcze niezweryfikowana", STATUS_SAEL)
        } else {
            originalButton.text = "ORYGINAŁ ✓"
            saelButton.text = "👓  SAEL"
            setStatus("ORYGINAŁ • bez filtra SAEL", STATUS_NEUTRAL)
        }
    }

    private fun applySaelMode() {
        if (mainFrameLoadFailed) return
        setStatus("👓 SAEL • porządkuję stronę…", STATUS_SAEL)
        runSaelScript(SaelScripts.apply()) { result ->
            if (modeState.mode != BrowserMode.SAEL) return@runSaelScript
            if (result.success && result.observing) {
                setStatus(
                    "👓 SAEL • ukryto ${result.hiddenCount} elementów • 🟡 treść niezweryfikowana",
                    STATUS_SAEL
                )
            } else showScriptError(result.error)
        }
    }

    private fun runSaelScript(script: String, onComplete: (ScriptResult) -> Unit) {
        webView.evaluateJavascript(script) { rawResult ->
            onComplete(parseScriptResult(rawResult))
        }
    }

    private fun parseScriptResult(rawResult: String?): ScriptResult = try {
        var decoded: Any? = JSONTokener(rawResult ?: "null").nextValue()
        if (decoded is String) decoded = JSONTokener(decoded).nextValue()
        val json = decoded as? JSONObject
            ?: return ScriptResult(success = false, error = "Brak odpowiedzi skryptu")
        ScriptResult(
            success = json.optBoolean("success", false),
            observing = json.optBoolean("observing", false),
            hiddenCount = json.optInt("hiddenCount", 0),
            error = json.optString("error", "Nieznany błąd skryptu")
        )
    } catch (error: Exception) {
        ScriptResult(success = false, error = error.message ?: "Nieprawidłowa odpowiedź skryptu")
    }

    private fun showLoadError(details: String) {
        setStatus("Nie udało się załadować strony • $details", STATUS_ERROR)
        showFactPending("NIE WIEM • błąd ładowania strony")
        updateNavigationControls()
    }

    private fun showScriptError(details: String) {
        setStatus("SAEL nie mógł uporządkować strony • $details", STATUS_ERROR)
    }

    private fun setStatus(text: String, color: Int) {
        statusBar.text = text
        statusBar.setTextColor(color)
    }

    private fun analyzeCurrentPage(url: String) {
        if (url.isBlank()) {
            showFactPending("NIE WIEM • brak adresu strony")
            return
        }
        val token = analysisGate.capture(url)
        webView.evaluateJavascript(
            "(document.documentElement && document.documentElement.outerHTML) || ''"
        ) { rawHtml ->
            if (analysisExecutor.isShutdown || !analysisGate.isCurrent(token, webView.url)) {
                return@evaluateJavascript
            }
            val html = decodeJavascriptString(rawHtml)
            if (html.isBlank()) {
                if (analysisGate.isCurrent(token, webView.url)) {
                    showFactPending("NIE WIEM • brak treści do analizy")
                }
                return@evaluateJavascript
            }

            analysisExecutor.execute {
                val result = runCatching {
                    factEngine.evaluate(articleExtractor.extract(html, url))
                }.getOrNull()
                runOnUiThread {
                    if (isDestroyed || !analysisGate.isCurrent(token, webView.url)) return@runOnUiThread
                    if (result == null) showFactPending("NIE WIEM • analiza nie powiodła się")
                    else showFactResult(result)
                }
            }
        }
    }

    private fun decodeJavascriptString(raw: String?): String = try {
        JSONTokener(raw ?: "null").nextValue() as? String ?: ""
    } catch (_: Exception) {
        ""
    }

    private fun showFactPending(text: String) {
        factResultButton.text = text
        factResultButton.setTextColor(Color.rgb(21, 21, 21))
        factResultButton.backgroundTintList = ColorStateList.valueOf(FACT_UNKNOWN)
        factResultButton.isEnabled = false
    }

    private fun showFactResult(result: FactResult) {
        latestFactResult = result
        val label = when (result.verdict) {
            FactVerdict.TRUE -> "PRAWDA"
            FactVerdict.FALSE -> "FAŁSZ"
            FactVerdict.UNKNOWN -> "NIE WIEM"
        }
        val color = when (result.verdict) {
            FactVerdict.TRUE -> FACT_TRUE
            FactVerdict.FALSE -> FACT_FALSE
            FactVerdict.UNKNOWN -> FACT_UNKNOWN
        }
        factResultButton.text = "$label • ${(result.confidence * 100).roundToInt()}% • dotknij po szczegóły"
        factResultButton.setTextColor(if (result.verdict == FactVerdict.FALSE) Color.WHITE else Color.rgb(21, 21, 21))
        factResultButton.backgroundTintList = ColorStateList.valueOf(color)
        factResultButton.isEnabled = true
    }

    private fun showFactDetails(result: FactResult) {
        val verdict = when (result.verdict) {
            FactVerdict.TRUE -> "PRAWDA"
            FactVerdict.FALSE -> "FAŁSZ"
            FactVerdict.UNKNOWN -> "NIE WIEM"
        }
        val origin = when (result.origin) {
            ResultOrigin.LOCAL_HEURISTIC -> "lokalna analiza"
            ResultOrigin.EXTERNAL_SOURCE -> "źródło zewnętrzne"
            ResultOrigin.BOTH -> "lokalna analiza i źródło zewnętrzne"
        }
        val evidence = result.evidence.ifEmpty {
            listOf(pl.sael.browser.fact.FactEvidence("Brak przesłanek.", pl.sael.browser.fact.EvidenceStance.NEUTRAL, 0.0))
        }.joinToString("\n") { "• ${it.description}" }
        val sources = if (result.sources.isEmpty()) "• brak dostępnych źródeł"
        else result.sources.joinToString("\n") { "• ${it.name}: ${it.url}" }
        val clickbaitReasons = if (result.clickbait.reasons.isEmpty()) "brak wyraźnych sygnałów"
        else result.clickbait.reasons.joinToString("; ")
        val message = buildString {
            appendLine("Pewność: ${(result.confidence * 100).roundToInt()}%")
            appendLine("Pochodzenie: $origin")
            appendLine()
            appendLine(result.rationale)
            appendLine()
            appendLine("Przesłanki:")
            appendLine(evidence)
            appendLine()
            appendLine("Źródła:")
            appendLine(sources)
            appendLine()
            append("Clickbait ${(result.clickbait.score * 100).roundToInt()}%: $clickbaitReasons")
        }
        AlertDialog.Builder(this)
            .setTitle(verdict)
            .setMessage(message)
            .setPositiveButton("Zamknij", null)
            .show()
    }

    private data class ScriptResult(
        val success: Boolean,
        val observing: Boolean = false,
        val hiddenCount: Int = 0,
        val error: String = "Nieznany błąd"
    )

    companion object {
        private const val STATE_MODE = "sael.browser.mode"
        private val STATUS_SAEL = Color.rgb(230, 200, 107)
        private val STATUS_NEUTRAL = Color.LTGRAY
        private val STATUS_ERROR = Color.rgb(255, 110, 110)
        private val FACT_TRUE = Color.rgb(82, 210, 106)
        private val FACT_FALSE = Color.rgb(220, 70, 70)
        private val FACT_UNKNOWN = Color.rgb(255, 213, 74)
    }
}
