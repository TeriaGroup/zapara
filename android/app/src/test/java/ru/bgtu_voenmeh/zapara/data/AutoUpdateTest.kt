package ru.bgtu_voenmeh.zapara.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class AutoUpdateTest {

    private val feed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom">
          <link rel="self" href="https://github.com/TeriaGroup/zapara/releases.atom"/>
          <entry>
            <id>tag:github.com,2008:Repository/1/windows-v1.2.2</id>
            <title>Windows v1.2.2</title>
            <link type="text/html" rel="alternate" href="https://github.com/TeriaGroup/zapara/releases/tag/windows-v1.2.2"/>
          </entry>
          <entry>
            <id>tag:github.com,2008:Repository/1/android-v1.2.18</id>
            <title>Android v1.2.18</title>
            <link type="text/html" rel="alternate" href="https://github.com/TeriaGroup/zapara/releases/tag/android-v1.2.18"/>
          </entry>
          <entry>
            <id>tag:github.com,2008:Repository/1/android-v1.2.17</id>
            <title>Android v1.2.17</title>
            <link type="text/html" rel="alternate" href="https://github.com/TeriaGroup/zapara/releases/tag/android-v1.2.17"/>
          </entry>
        </feed>
    """.trimIndent()

    @Test
    fun feedFirstMatchPerChannel() {
        assertEquals("android-v1.2.18", AutoUpdate.parseFeedTag(feed, "android-"))
        assertEquals("windows-v1.2.2", AutoUpdate.parseFeedTag(feed, "windows-"))
    }

    @Test
    fun feedNoMatch() {
        assertNull(AutoUpdate.parseFeedTag(feed, "ios-"))
        assertNull(AutoUpdate.parseFeedTag("<feed></feed>", "android-"))
    }

    @Test
    fun versionCompare() {
        assertTrue(AutoUpdate.isNewer("android-v1.2.18", "android-v1.2.17"))
        assertFalse(AutoUpdate.isNewer("android-v1.2.17", "android-v1.2.17"))
        assertFalse(AutoUpdate.isNewer("android-v1.2.17", "android-v1.2.18"))
        assertFalse(AutoUpdate.isNewer("android-v1.2.9", "android-v1.2.18"))
    }
}
