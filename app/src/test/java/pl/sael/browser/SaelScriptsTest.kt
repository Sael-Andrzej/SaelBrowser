package pl.sael.browser

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class SaelScriptsTest {
    @Test
    fun `apply script observes dynamic content and reports its result`() {
        val script = SaelScripts.apply()
        assertTrue(script.contains("new MutationObserver"))
        assertTrue(script.contains("result.success = true"))
        assertTrue(script.contains("result.observing = true"))
        assertTrue(script.contains("return JSON.stringify(result)"))
    }

    @Test
    fun `apply script avoids broad destructive selectors`() {
        val script = SaelScripts.apply()
        assertFalse(script.contains("document.querySelectorAll('iframe')"))
        assertFalse(script.contains("[class*=\"modal\"]"))
        assertFalse(script.contains("[class*=\"banner\"]"))
        assertFalse(script.contains("[class*=\"promo\"]"))
        assertFalse(script.contains("[class*=\"cookie\"]"))
        assertFalse(script.contains("document.querySelectorAll(selector).forEach(el => el.remove())"))
    }

    @Test
    fun `restore script disconnects observer and restores hidden elements`() {
        val script = SaelScripts.restore()
        assertTrue(script.contains("existing.observer.disconnect()"))
        assertTrue(script.contains("classList.remove('sael-hidden')"))
        assertTrue(script.contains("result.success = true"))
    }
}
