package ru.bgtu_voenmeh.zapara.ui.widgets

import org.w3c.dom.Element
import ru.bgtu_voenmeh.zapara.ui.UiCopy
import java.io.File
import javax.xml.parsers.DocumentBuilderFactory

object WidgetCopy : UiCopy {
    private val values: Map<String, String> by lazy {
        val factory = DocumentBuilderFactory.newInstance()
        listOf(
            File("src/main/res/values/strings.xml"),
            File("src/main/res/values/widget_strings.xml")
        ).fold(linkedMapOf<String, String>()) { acc, file ->
            val doc = factory.newDocumentBuilder().parse(file)
            val nodes = doc.getElementsByTagName("string")
            for (i in 0 until nodes.length) {
                val node = nodes.item(i) as Element
                acc[node.getAttribute("name")] = node.textContent
            }
            acc
        }
    }

    override fun get(name: String, vararg args: Any?): String {
        val raw = values[name] ?: error("missing string $name")
        if (args.isEmpty()) return raw
        return raw.format(*args)
    }
}
