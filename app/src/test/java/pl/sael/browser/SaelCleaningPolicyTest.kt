package pl.sael.browser

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class SaelCleaningPolicyTest {
    @Test
    fun `does not hide generic frames or legitimate banners`() {
        assertFalse(SaelCleaningPolicy.shouldHide(CleaningSignals(source = "https://video.example/embed")))
        assertFalse(SaelCleaningPolicy.shouldHide(CleaningSignals(className = "hero-banner")))
        assertFalse(
            SaelCleaningPolicy.shouldHide(
                CleaningSignals(
                    role = "dialog",
                    text = "Zaloguj się do konta",
                    isOverlay = true
                )
            )
        )
    }

    @Test
    fun `hides explicit ads and nuisance overlays`() {
        assertTrue(
            SaelCleaningPolicy.shouldHide(
                CleaningSignals(source = "https://ad.doubleclick.net/frame")
            )
        )
        assertTrue(SaelCleaningPolicy.shouldHide(CleaningSignals(className = "adsbygoogle")))
        assertTrue(
            SaelCleaningPolicy.shouldHide(
                CleaningSignals(
                    className = "newsletter-popup",
                    text = "Subscribe to our newsletter",
                    isOverlay = true
                )
            )
        )
    }

    @Test
    fun `protects account and payment dialogs even when they mention cookies`() {
        assertFalse(
            SaelCleaningPolicy.shouldHide(
                CleaningSignals(
                    role = "dialog",
                    text = "Cookie settings required before payment checkout",
                    isOverlay = true
                )
            )
        )
    }
}
