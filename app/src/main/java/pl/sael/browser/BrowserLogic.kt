package pl.sael.browser

import java.net.URLEncoder

object BrowserAddressNormalizer {
    fun normalize(input: String): String? {
        val value = input.trim()
        if (value.isEmpty()) return null

        return when {
            value.startsWith("http://", ignoreCase = true) ||
                value.startsWith("https://", ignoreCase = true) -> value
            value.contains('.') && !value.contains(' ') -> "https://$value"
            else -> "https://www.google.com/search?q=" +
                URLEncoder.encode(value, Charsets.UTF_8.name())
        }
    }
}

enum class NavigationDirection { BACK, FORWARD }

enum class NavigationCommand { GO_BACK, GO_FORWARD, NONE }

object BrowserNavigation {
    fun command(
        direction: NavigationDirection,
        canGoBack: Boolean,
        canGoForward: Boolean
    ): NavigationCommand = when (direction) {
        NavigationDirection.BACK ->
            if (canGoBack) NavigationCommand.GO_BACK else NavigationCommand.NONE
        NavigationDirection.FORWARD ->
            if (canGoForward) NavigationCommand.GO_FORWARD else NavigationCommand.NONE
    }
}

enum class BrowserMode { ORIGINAL, SAEL }

class BrowserModeState(initialMode: BrowserMode = BrowserMode.SAEL) {
    var mode: BrowserMode = initialMode
        private set

    fun select(newMode: BrowserMode): Boolean {
        if (mode == newMode) return false
        mode = newMode
        return true
    }
}

class AnalysisToken internal constructor(internal val generation: Long, internal val url: String)

class AnalysisRequestGate {
    private var generation = 0L

    fun beginNavigation() {
        generation++
    }

    fun capture(url: String): AnalysisToken = AnalysisToken(generation, url)

    fun isCurrent(token: AnalysisToken, currentUrl: String?): Boolean =
        token.generation == generation && token.url == currentUrl.orEmpty()
}

data class CleaningSignals(
    val id: String = "",
    val className: String = "",
    val role: String = "",
    val ariaLabel: String = "",
    val source: String = "",
    val text: String = "",
    val isOverlay: Boolean = false
)

object SaelCleaningPolicy {
    val adSelectors = listOf(
        ".adsbygoogle",
        "[data-ad-slot]",
        "[data-ad-client]",
        "[id^=\"google_ads_\"]",
        "[id*=\"div-gpt-ad\"]",
        "[class~=\"ad-container\"]",
        "[class~=\"advertisement\"]",
        "[aria-label*=\"advertisement\" i]",
        "[aria-label*=\"reklama\" i]",
        "iframe[src*=\"doubleclick.net\"]",
        "iframe[src*=\"googlesyndication.com\"]"
    )

    val nuisanceKeywords = listOf(
        "newsletter", "subscribe", "subskryb", "powiadomienia",
        "notifications", "cookie consent", "zgoda na pliki cookie",
        "marketing consent"
    )

    val protectedKeywords = listOf(
        "log in", "login", "sign in", "zaloguj", "konto", "account",
        "payment", "płatność", "checkout", "security", "bezpieczeństwo"
    )

    private val adHosts = listOf("doubleclick.net", "googlesyndication.com")
    private val explicitAdMarkers = listOf(
        "adsbygoogle", "google_ads_", "div-gpt-ad", "ad-container", "advertisement"
    )

    fun shouldHide(signals: CleaningSignals): Boolean {
        val source = signals.source.lowercase()
        if (adHosts.any(source::contains)) return true

        val identity = listOf(
            signals.id,
            signals.className,
            signals.role,
            signals.ariaLabel
        ).joinToString(" ").lowercase()

        if (explicitAdMarkers.any(identity::contains) ||
            signals.ariaLabel.contains("reklama", ignoreCase = true)
        ) return true

        if (!signals.isOverlay) return false
        val text = signals.text.lowercase()
        if (protectedKeywords.any(text::contains)) return false
        return nuisanceKeywords.any { keyword ->
            text.contains(keyword) || identity.contains(keyword)
        }
    }
}
