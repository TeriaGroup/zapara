package ru.bgtu_voenmeh.zapara.ui.maps

import java.io.File
import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.campus.Route

class MapDecodeFailureTest {
    @Test fun failure_is_local_preserves_route_and_never_retries() {
        val floor = FloorKey("ГК", 1)
        val other = FloorKey("ГК", 2)
        val file = File("one.jpg")
        val state = MapsUiState(building = "ГК", floor = 1, planFile = file,
            route = Route(0, emptyList()), rasterCatalog = mapOf(
                floor to FloorRaster(file, RasterSize(10, 10)),
                other to FloorRaster(File("two.jpg"), RasterSize(10, 10))))
        assertSame(state, state.mapDecodeFailed(other))
        val failed = state.mapDecodeFailed(floor)
        assertSame(state.route, failed.route)
        assertNull(failed.planFile)
        assertFalse(failed.rasterCatalog.containsKey(floor))
        assertTrue(failed.rasterCatalog.containsKey(other))
        assertEquals(setOf(floor), failed.decodeFailedFloors)
        assertSame(failed, failed.mapDecodeFailed(floor))
        assertFalse(failed.routeLoading)
    }
}
