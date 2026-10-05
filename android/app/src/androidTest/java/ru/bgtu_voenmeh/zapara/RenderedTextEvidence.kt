@file:Suppress("INVISIBLE_MEMBER", "INVISIBLE_REFERENCE")

package ru.bgtu_voenmeh.zapara

import android.util.Log
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.node.NodeCoordinator
import androidx.compose.ui.graphics.RectangleShape
import androidx.compose.ui.graphics.Outline
import androidx.compose.ui.layout.boundsInWindow
import androidx.compose.ui.layout.positionInWindow
import androidx.compose.ui.layout.LayoutCoordinates
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.semantics.SemanticsNode
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.text.Paragraph
import androidx.compose.ui.text.TextLayoutResult
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.IntSize
import androidx.compose.ui.platform.ViewRootForTest

internal enum class TextDefect { MISSING, UNSUPPORTED, STALE, SCALE, CONDITION, ELLIPSIS, LOST_TEXT, HORIZONTAL, VERTICAL, PARENT_CLIP, WORD_SPLIT }
internal class TextEvidenceFailure(val defect: TextDefect, detail: String) : AssertionError("$defect: $detail")

/**
 * Foundation 1.6.8 observer, NOT a re-layout at a guessed width. The exact simple-node
 * class and its private cache are required; this reads the Paragraph used by draw().
 * Version changes/private-field changes fail closed. No release code is instrumented.
 */
internal object RenderedTextEvidence {
    private const val simpleName = "androidx.compose.foundation.text.modifiers.TextStringSimpleNode"
    private val words = Regex("[\\p{L}\\p{M}]+(?:[-‑][\\p{L}\\p{M}]+)*")
    private const val rounding = 0.5f

    data class Snapshot(
        val node: SemanticsNode, val raw: TextLayoutResult, val renderer: Any,
        val cache: Any?, val paragraph: Paragraph, val box: Rect, val coordinates: LayoutCoordinates
    )

    private fun requireEvidence(ok: Boolean, defect: TextDefect, detail: String) {
        if (!ok) throw TextEvidenceFailure(defect, detail)
    }

    fun capture(node: SemanticsNode): Snapshot {
        val results = mutableListOf<TextLayoutResult>()
        val action = node.config.getOrElseNullable(SemanticsActions.GetTextLayoutResult) { null }
        requireEvidence(action?.action?.invoke(results) == true && results.size == 1, TextDefect.MISSING, "node=${node.id} layouts=${results.size}")
        var current: Modifier.Node? = node.layoutNode.nodes.head
        val renderers = mutableListOf<Modifier.Node>()
        while (current != null) {
            if (current.javaClass.name == simpleName) renderers += current
            current = current.child
        }
        if (renderers.isEmpty()) return captureField(node, results.single(), requireNotNull(action?.action))
        requireEvidence(renderers.size == 1, TextDefect.UNSUPPORTED, "Ambiguous String renderer node=${node.id}")
        val renderer = renderers.single()
        val cache = renderer.javaClass.getDeclaredField("_layoutCache").apply { isAccessible = true }.get(renderer)
        val paragraph = cache?.javaClass?.getDeclaredField("paragraph")?.apply { isAccessible = true }?.get(cache) as? Paragraph
        requireEvidence(paragraph != null, TextDefect.MISSING, "Missing painted paragraph node=${node.id}")
        val coordinates = node.layoutNode.innerCoordinator
        requireEvidence(coordinates.isAttached, TextDefect.STALE, "Detached node=${node.id}")
        val position = coordinates.positionInWindow()
        val box = Rect(position.x, position.y, position.x + coordinates.size.width, position.y + coordinates.size.height)
        return Snapshot(node, results.single(), renderer, requireNotNull(cache), requireNotNull(paragraph), box, coordinates)
    }

    private fun captureField(node: SemanticsNode, raw: TextLayoutResult, action: Any): Snapshot {
        // CoreTextField's draw and semantics both use state.layoutResult.value (1.6.8).
        // Require identity with that real result; unlike String Text there is NO width waiver.
        val fields = action.javaClass.declaredFields.filter { it.type.name == "androidx.compose.foundation.text.TextFieldState" }
        requireEvidence(action.javaClass.name.startsWith("androidx.compose.foundation.text.CoreTextFieldKt$") && fields.size == 1,
            TextDefect.UNSUPPORTED, "Unknown layout action ${action.javaClass.name} node=${node.id}")
        val state = fields.single().apply { isAccessible = true }.get(action)
        val proxy = state.javaClass.getMethod("getLayoutResult").invoke(state)
        requireEvidence(proxy != null && proxy.javaClass.getMethod("getValue").invoke(proxy) === raw,
            TextDefect.STALE, "CoreTextField result is not the painted result")
        val coordinates = proxy.javaClass.getMethod("getInnerTextFieldCoordinates").invoke(proxy) as? LayoutCoordinates
        requireEvidence(coordinates?.isAttached == true, TextDefect.MISSING, "CoreTextField coordinates absent")
        requireEvidence(raw.multiParagraph.paragraphInfoList.size == 1, TextDefect.UNSUPPORTED, "Multi-paragraph field")
        val text = node.config.getOrElseNullable(SemanticsProperties.EditableText) { null }
        requireEvidence(text == raw.layoutInput.text, TextDefect.UNSUPPORTED, "Transformed field text")
        val position = requireNotNull(coordinates).positionInWindow()
        return Snapshot(node, raw, requireNotNull(proxy), null, raw.multiParagraph.paragraphInfoList.single().paragraph,
            Rect(position.x, position.y, position.x + raw.size.width, position.y + raw.size.height), coordinates)
    }

    fun verify(snapshot: Snapshot, scale: Float? = null) {
        val (node, raw, renderer, cache, paragraph, box) = snapshot
        val text = raw.layoutInput.text.text
        requireEvidence((node.root as? ViewRootForTest)?.view?.hasWindowFocus() == true,
            TextDefect.MISSING, "Text is not in the current foreground root")
        val paintedSize = if (cache == null) raw.size else IntSize(cache.javaClass.getDeclaredField("layoutSize").apply { isAccessible = true }.getLong(cache))
        val paintedDensity = if (cache == null) raw.layoutInput.density else cache.javaClass.getDeclaredField("density").apply { isAccessible = true }.get(cache) as? Density
        fun demand(ok: Boolean, defect: TextDefect, detail: String) = requireEvidence(ok, defect, "$text: $detail")
        val fresh = capture(node)
        demand(fresh.renderer === renderer && fresh.paragraph === paragraph && fresh.box == box && fresh.raw.layoutInput == raw.layoutInput,
            TextDefect.STALE, "layout changed; re-query after resize/scroll")
        demand(node.config.getOrElseNullable(SemanticsProperties.IsShowingTextSubstitution) { false } != true,
            TextDefect.UNSUPPORTED, "text substitution")
        demand(raw.layoutInput.text.spanStyles.isEmpty() && raw.layoutInput.placeholders.isEmpty(), TextDefect.UNSUPPORTED, "rich text")
        demand(paintedSize == raw.size && paintedSize.width <= snapshot.coordinates.size.width && paintedSize.height <= snapshot.coordinates.size.height,
            TextDefect.STALE, "painted/cache/semantics sizes differ")
        demand(box.width.isFinite() && box.height.isFinite() && box.width > 0 && box.height > 0, TextDefect.MISSING, "empty bounds")
        if (scale != null) demand(paintedDensity?.fontScale == scale && raw.layoutInput.density.fontScale == scale, TextDefect.SCALE, "expected=$scale actual=$paintedDensity")
        demand(paintedDensity != null && paintedDensity.density.isFinite() && paintedDensity.density > 0 &&
            paintedDensity.density == raw.layoutInput.density.density, TextDefect.SCALE, "renderer/semantics density mismatch")
        Log.i("RenderedText", "node=${node.id} text=$text rawSize=${raw.size} rawParagraph=${raw.multiParagraph.width} rawOverflow=${raw.hasVisualOverflow} painted=${paragraph.width}x${paragraph.height} box=$box scale=${paintedDensity?.fontScale} density=${paintedDensity?.density}")
        demand(paragraph.lineCount > 0, TextDefect.MISSING, "no painted lines")
        repeat(paragraph.lineCount) { line -> demand(!paragraph.isLineEllipsized(line), TextDefect.ELLIPSIS, "line=$line") }
        demand(!paragraph.didExceedMaxLines, TextDefect.LOST_TEXT, "maxLines exceeded")
        if (cache == null) {
            demand(!raw.didOverflowWidth, TextDefect.HORIZONTAL, "genuine field width overflow")
            demand(!raw.didOverflowHeight, TextDefect.VERTICAL, "genuine field height overflow")
        }
        demand(paragraph.height <= box.height + rounding, TextDefect.VERTICAL, "height=${paragraph.height} box=${box.height}")
        repeat(paragraph.lineCount) { line ->
            demand(paragraph.getLineLeft(line) >= -rounding && paragraph.getLineRight(line) <= box.width + rounding,
                TextDefect.HORIZONTAL, "line=$line range=${paragraph.getLineLeft(line)}..${paragraph.getLineRight(line)} width=${box.width}")
        }
        val last = text.indexOfLast { !it.isWhitespace() }
        demand(last < paragraph.getLineEnd(paragraph.lineCount - 1, visibleEnd = true), TextDefect.LOST_TEXT, "last meaningful character not laid out")
        words.findAll(text).forEach { word ->
            demand(paragraph.getLineForOffset(word.range.first) == paragraph.getLineForOffset(word.range.last),
                TextDefect.WORD_SPLIT, "word=${word.value}")
        }
        // boundsInWindow intersects real clip layers/viewport, unlike intersecting every parent box.
        val visible = snapshot.coordinates.boundsInWindow()
        demand(visible.left <= box.left + rounding && visible.top <= box.top + rounding &&
            visible.right >= box.right - rounding && visible.bottom >= box.bottom - rounding,
            TextDefect.PARENT_CLIP, "box=$box visible=$visible")
        val coordinates = snapshot.coordinates
        val origin = coordinates.localToWindow(Offset.Zero)
        demand(coordinates.localToWindow(Offset(1f, 1f)) - origin == Offset(1f, 1f),
            TextDefect.UNSUPPORTED, "non-translation transform")
        var ancestor = coordinates as? NodeCoordinator
        demand(ancestor != null, TextDefect.UNSUPPORTED, "unknown coordinate implementation")
        while (ancestor != null) {
            val layer = ancestor.layer
            val clips = NodeCoordinator::class.java.getDeclaredField("isClipping").apply { isAccessible = true }.getBoolean(ancestor)
            if (layer != null && clips) {
                demand(layer.javaClass.name == "androidx.compose.ui.platform.RenderNodeLayer", TextDefect.UNSUPPORTED, "unknown layer ${layer.javaClass.name}")
                val resolver = layer.javaClass.getDeclaredField("outlineResolver").apply { isAccessible = true }.get(layer)
                val shape = resolver.javaClass.getDeclaredField("shape").apply { isAccessible = true }.get(resolver)
                layer.isInLayer(Offset.Zero) // Resolve the same lazy outline that the layer clips against.
                val outline = resolver.javaClass.getDeclaredField("calculatedOutline").apply { isAccessible = true }.get(resolver)
                demand(shape === RectangleShape || outline is Outline.Rectangle || outline is Outline.Rounded ||
                    (outline is Outline.Generic && outline.path.isConvex), TextDefect.UNSUPPORTED,
                    "non-convex/unknown outline shape=${shape.javaClass.name} outline=${outline?.javaClass?.name}")
                text.indices.filter { !text[it].isWhitespace() }.forEach { offset ->
                    val glyph = paragraph.getBoundingBox(offset)
                    listOf(glyph.topLeft, Offset(glyph.right - 0.01f, glyph.top),
                        Offset(glyph.left, glyph.bottom - 0.01f), Offset(glyph.right - 0.01f, glyph.bottom - 0.01f)).forEach { point ->
                        demand(layer.isInLayer(ancestor!!.localPositionOf(coordinates, point)), TextDefect.PARENT_CLIP, "glyph=$offset outside layer")
                    }
                }
            }
            ancestor = ancestor.wrappedBy
        }
    }

    fun check(node: SemanticsNode, scale: Float? = null) = verify(capture(node), scale)
}
