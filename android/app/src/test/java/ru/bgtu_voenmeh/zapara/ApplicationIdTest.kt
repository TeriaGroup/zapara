package ru.bgtu_voenmeh.zapara

import org.junit.Assert.assertEquals
import org.junit.Test

class ApplicationIdTest {
    @Test fun installed_package_is_zapara_org() {
        assertEquals("ru.zapara.app", BuildConfig.APPLICATION_ID)
    }
}
