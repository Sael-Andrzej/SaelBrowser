package pl.sael.browser.fact

import java.util.Locale

class LocalClickbaitAnalyzer : ClickbaitAnalyzer {
    override fun analyze(title: String, content: String): ClickbaitResult {
        val cleanTitle = title.trim()
        if (cleanTitle.isEmpty()) return ClickbaitResult(0.0, emptyList())

        var score = 0.0
        val reasons = mutableListOf<String>()
        val letters = cleanTitle.filter(Char::isLetter)
        val uppercaseRatio = if (letters.isEmpty()) 0.0 else letters.count(Char::isUpperCase).toDouble() / letters.length

        if (letters.length >= 12 && uppercaseRatio >= 0.7) {
            score += 0.3
            reasons += "Tytuł zawiera nadmiernie dużo wielkich liter."
        }

        val sensationalPhrases = listOf(
            "pilne", "szok", "musisz to zobaczyć", "nie uwierzysz",
            "tego ci nie powiedzą", "breaking"
        )
        val lowerTitle = cleanTitle.lowercase(Locale.ROOT)
        val phrases = sensationalPhrases.filter(lowerTitle::contains)
        if (phrases.isNotEmpty()) {
            score += minOf(0.35, phrases.size * 0.18)
            reasons += "Tytuł wykorzystuje sensacyjne sformułowania: ${phrases.joinToString()}."
        }

        if (cleanTitle.count { it == '!' } >= 2) {
            score += 0.15
            reasons += "Tytuł używa wielu wykrzykników."
        }

        if (cleanTitle.endsWith('?') &&
            listOf("czy to koniec", "co ukrywają", "co się stanie", "dlaczego nikt")
                .any(lowerTitle::contains)
        ) {
            score += 0.15
            reasons += "Pytanie w tytule sugeruje sensację bez podania odpowiedzi."
        }

        val titleTokens = meaningfulTokens(cleanTitle)
        val contentTokens = meaningfulTokens(content.take(12_000))
        if (titleTokens.size >= 4 && contentTokens.size >= 8) {
            val overlap = titleTokens.count(contentTokens::contains).toDouble() / titleTokens.size
            if (overlap < 0.25) {
                score += 0.3
                reasons += "Tytuł ma niewielkie pokrycie tematyczne z treścią artykułu."
            }
        }

        return ClickbaitResult(score.coerceIn(0.0, 1.0), reasons)
    }

    private fun meaningfulTokens(text: String): Set<String> = text
        .lowercase(Locale.ROOT)
        .split(Regex("[^\\p{L}\\p{N}]+"))
        .asSequence()
        .filter { it.length >= 4 && it !in STOP_WORDS }
        .toSet()

    companion object {
        private val STOP_WORDS = setOf(
            "oraz", "jest", "który", "która", "które", "tego", "przez", "będzie",
            "with", "that", "this", "from", "have", "will", "your"
        )
    }
}
