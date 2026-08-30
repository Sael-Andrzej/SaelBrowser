package pl.sael.browser

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class BrowserLogicTest {
    @Test
    fun `normalizes addresses and search queries`() {
        assertNull(BrowserAddressNormalizer.normalize("   "))
        assertEquals(
            "https://example.com/path",
            BrowserAddressNormalizer.normalize("example.com/path")
        )
        assertEquals(
            "HTTP://example.com",
            BrowserAddressNormalizer.normalize("HTTP://example.com")
        )
        assertEquals(
            "https://www.google.com/search?q=sael+browser",
            BrowserAddressNormalizer.normalize("sael browser")
        )
    }

    @Test
    fun `chooses only available navigation actions`() {
        assertEquals(
            NavigationCommand.GO_BACK,
            BrowserNavigation.command(NavigationDirection.BACK, true, false)
        )
        assertEquals(
            NavigationCommand.GO_FORWARD,
            BrowserNavigation.command(NavigationDirection.FORWARD, false, true)
        )
        assertEquals(
            NavigationCommand.NONE,
            BrowserNavigation.command(NavigationDirection.BACK, false, true)
        )
        assertEquals(
            NavigationCommand.NONE,
            BrowserNavigation.command(NavigationDirection.FORWARD, true, false)
        )
    }

    @Test
    fun `mode transitions are explicit and idempotent`() {
        val state = BrowserModeState()
        assertEquals(BrowserMode.SAEL, state.mode)
        assertFalse(state.select(BrowserMode.SAEL))
        assertTrue(state.select(BrowserMode.ORIGINAL))
        assertEquals(BrowserMode.ORIGINAL, state.mode)
        assertFalse(state.select(BrowserMode.ORIGINAL))
        assertTrue(state.select(BrowserMode.SAEL))
    }

    @Test
    fun `analysis token is rejected after navigation or for another url`() {
        val gate = AnalysisRequestGate()
        val first = gate.capture("https://example.com/first")
        assertTrue(gate.isCurrent(first, "https://example.com/first"))
        assertFalse(gate.isCurrent(first, "https://example.com/other"))

        gate.beginNavigation()
        assertFalse(gate.isCurrent(first, "https://example.com/first"))
        val second = gate.capture("https://example.com/second")
        assertTrue(gate.isCurrent(second, "https://example.com/second"))
    }
}
