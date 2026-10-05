package ru.bgtu_voenmeh.zapara

import android.security.NetworkSecurityPolicy
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class DebugLoopbackPolicyTest {
    @Test fun debug_http_is_limited_to_explicit_loopback_hosts() {
        val policy = NetworkSecurityPolicy.getInstance()
        assertTrue(policy.isCleartextTrafficPermitted("localhost"))
        assertTrue(policy.isCleartextTrafficPermitted("127.0.0.1"))
        assertFalse(policy.isCleartextTrafficPermitted("example.com"))
        assertFalse(policy.isCleartextTrafficPermitted("10.0.2.2"))
        assertFalse(policy.isCleartextTrafficPermitted("127.0.0.2"))
        assertFalse(policy.isCleartextTrafficPermitted())
    }
}
