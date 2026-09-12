package android.os;

/** JVM-only tracing shim: Compose runtime runs normally; Android tracing has no host backend. */
public final class Trace {
    private Trace() {}
    public static void beginSection(String name) {}
    public static void endSection() {}
}
